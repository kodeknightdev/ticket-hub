import logging
from typing import Any

from sqlalchemy.exc import IntegrityError

from . import service
from .db import Database
from .messaging import EventPublisher
from .models import ProcessedEvent

log = logging.getLogger(__name__)

PRICE_UPDATED = "pricing.price-updated"


class PricingHandlers:
    """Each handler runs in one transaction together with the processed-event marker."""

    consumer = "pricing"

    def __init__(self, db: Database, publisher: EventPublisher, min_change_ratio: float):
        self.db = db
        self.publisher = publisher
        self.min_change_ratio = min_change_ratio

    def topics(self) -> dict[str, Any]:
        return {
            "events.event-published": self.on_event_published,
            "events.event-cancelled": self.on_event_cancelled,
            "seating.seat-held": self.on_seat_held,
            "seating.seat-released": self.on_hold_ended,
            "seating.hold-expired": self.on_hold_ended,
            "seating.seats-sold": self.on_seats_sold,
        }

    def _once(self, env: dict[str, Any], work) -> list[service.PriceUpdate]:
        with self.db.session() as s:
            if s.get(ProcessedEvent, (env["id"], self.consumer)) is not None:
                return []
            updates = work(s, env["data"]) or []
            s.add(ProcessedEvent(event_id=env["id"], consumer=self.consumer, processed_at=service.now()))
            try:
                s.commit()
            except IntegrityError:
                s.rollback()
                return []
        for u in updates:
            self.publisher.publish(PRICE_UPDATED, u.event_id, u.payload())
        return updates

    def on_event_published(self, env: dict[str, Any]):
        return self._once(env, lambda s, d: service.register_event(s, d))

    def on_event_cancelled(self, env: dict[str, Any]):
        return self._once(env, lambda s, d: service.cancel_event(s, d["eventId"]))

    def on_seat_held(self, env: dict[str, Any]):
        def work(s, d):
            seats = [(seat["seatId"], seat["tierId"]) for seat in d["seats"]]
            try:
                service.lock_prices(s, d["holdId"], d["eventId"], service.parse_dt(d["heldAt"]), seats)
            except service.UnknownEventError:
                log.warning("seat-held for unknown event %s, skipping", d["eventId"])
                return []
            return service.recompute(s, d["eventId"], self.min_change_ratio)
        return self._once(env, work)

    def on_hold_ended(self, env: dict[str, Any]):
        return self._once(env, lambda s, d: service.release_hold(s, d["holdId"]))

    def on_seats_sold(self, env: dict[str, Any]):
        def work(s, d):
            service.mark_sold(s, d["holdId"], d["seatIds"])
            s.flush()
            return service.recompute(s, d["eventId"], self.min_change_ratio)
        return self._once(env, work)

    def recompute_all(self) -> list[service.PriceUpdate]:
        updates: list[service.PriceUpdate] = []
        with self.db.session() as s:
            for event_id in service.active_event_ids(s):
                updates += service.recompute(s, event_id, self.min_change_ratio)
            s.commit()
        for u in updates:
            self.publisher.publish(PRICE_UPDATED, u.event_id, u.payload())
        return updates
