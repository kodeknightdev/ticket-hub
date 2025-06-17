"""Kafka plumbing shared by the consumer loop: envelope format, retry topic and DLQ, trace headers."""

import json
import logging
import threading
import time
import uuid
from collections.abc import Callable
from datetime import UTC, datetime
from typing import Any

from opentelemetry import propagate, trace

log = logging.getLogger(__name__)
tracer = trace.get_tracer("tickethub.analytics.messaging")

Handler = Callable[[dict[str, Any]], None]


def envelope(topic: str, key: str, data: dict[str, Any]) -> dict[str, Any]:
    return {
        "id": str(uuid.uuid4()),
        "type": topic,
        "version": 1,
        "occurredAt": datetime.now(UTC).isoformat(),
        "key": key,
        "data": data,
    }


def _headers_to_dict(headers: list[tuple[str, bytes]] | None) -> dict[str, str]:
    return {k: v.decode() for k, v in (headers or []) if v is not None}


class EventPublisher:
    def __init__(self, bootstrap: str | None):
        self._producer = None
        if bootstrap:
            from confluent_kafka import Producer

            self._producer = Producer({"bootstrap.servers": bootstrap, "enable.idempotence": True, "acks": "all"})
        self.sent: list[tuple[str, dict[str, Any]]] = []

    def publish(self, topic: str, key: str, data: dict[str, Any]) -> dict[str, Any]:
        env = envelope(topic, key, data)
        self.sent.append((topic, env))
        if self._producer is None:
            return env
        with tracer.start_as_current_span(f"{topic} publish", kind=trace.SpanKind.PRODUCER):
            carrier: dict[str, str] = {}
            propagate.inject(carrier)
            self._producer.produce(topic, key=key, value=json.dumps(env), headers=list(carrier.items()))
            self._producer.poll(0)
        return env

    def flush(self) -> None:
        if self._producer is not None:
            self._producer.flush(5)


class ConsumerLoop:
    def __init__(self, bootstrap: str, group_id: str, handlers: dict[str, Handler], max_attempts: int = 3):
        self.bootstrap = bootstrap
        self.group_id = group_id
        self.handlers = handlers
        self.max_attempts = max_attempts
        self.retry_topic = f"{group_id}.retry"
        self.dlq_topic = f"{group_id}.dlq"
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        self._thread = threading.Thread(target=self._run, name=f"kafka-{self.group_id}", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=10)

    def _run(self) -> None:
        from confluent_kafka import Consumer, Producer

        consumer = Consumer({
            "bootstrap.servers": self.bootstrap,
            "group.id": self.group_id,
            "auto.offset.reset": "earliest",
            "enable.auto.commit": False,
            "allow.auto.create.topics": True,
        })
        forwarder = Producer({"bootstrap.servers": self.bootstrap})
        consumer.subscribe([*self.handlers.keys(), self.retry_topic])
        log.info("consumer %s subscribed to %s", self.group_id, ", ".join(self.handlers))

        while not self._stop.is_set():
            msg = consumer.poll(1.0)
            if msg is None:
                continue
            if msg.error():
                log.warning("kafka error: %s", msg.error())
                time.sleep(1)
                continue
            self._process(msg, forwarder)
            consumer.commit(message=msg, asynchronous=False)

        consumer.close()
        forwarder.flush(5)

    def _process(self, msg, forwarder) -> None:
        headers = _headers_to_dict(msg.headers())
        topic = headers.get("x-original-topic", msg.topic()) if msg.topic() == self.retry_topic else msg.topic()
        attempt = int(headers.get("x-attempt", "1"))
        handler = self.handlers.get(topic)
        if handler is None:
            return

        ctx = propagate.extract(headers)
        with tracer.start_as_current_span(f"{topic} process", context=ctx, kind=trace.SpanKind.CONSUMER):
            try:
                if attempt > 1:
                    time.sleep(0.25 * attempt)
                handler(json.loads(msg.value()))
            except Exception as exc:  # noqa: BLE001 - anything that escapes a handler gets retried
                target = self.dlq_topic if attempt >= self.max_attempts else self.retry_topic
                log.warning("handler for %s failed (attempt %s), forwarding to %s: %s", topic, attempt, target, exc)
                out = [(k, v.encode()) for k, v in headers.items() if k in ("traceparent", "tracestate")]
                out += [("x-original-topic", topic.encode()), ("x-attempt", str(attempt + 1).encode()), ("x-error", str(exc)[:500].encode())]
                forwarder.produce(target, key=msg.key(), value=msg.value(), headers=out)
                forwarder.flush(5)
