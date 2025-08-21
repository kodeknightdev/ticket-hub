#!/bin/bash
# Creates every business topic plus a retry and dead-letter topic per consumer group.
set -euo pipefail

BOOTSTRAP="${BOOTSTRAP:-kafka:9092}"
PARTITIONS="${PARTITIONS:-6}"
KAFKA_BIN="${KAFKA_BIN:-/opt/kafka/bin}"

topics=(
  seating.seat-held seating.seat-released seating.hold-expired seating.hold-expiring seating.seats-sold
  orders.order-created orders.order-paid orders.order-expired orders.order-refunded orders.tickets-issued orders.ticket-checked-in
  events.event-published events.event-updated events.event-cancelled
  pricing.price-updated
  health-checks
)
groups=(events seating seating-broadcast orders notifier pricing analytics)
for g in "${groups[@]}"; do topics+=("$g.retry" "$g.dlq"); done

until "$KAFKA_BIN/kafka-topics.sh" --bootstrap-server "$BOOTSTRAP" --list >/dev/null 2>&1; do
  echo "waiting for kafka at $BOOTSTRAP"
  sleep 2
done

for t in "${topics[@]}"; do
  "$KAFKA_BIN/kafka-topics.sh" --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic "$t" \
    --partitions "$PARTITIONS" --replication-factor 1 >/dev/null
  echo "topic ready: $t"
done
