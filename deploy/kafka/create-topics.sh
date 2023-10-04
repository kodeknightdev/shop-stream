#!/bin/bash
# auto-create is off on the broker, so create everything up front
set -euo pipefail

BOOTSTRAP="${KAFKA_BOOTSTRAP:-kafka:9092}"
PARTITIONS="${TOPIC_PARTITIONS:-3}"

TOPICS=(
  orders.order-placed
  orders.order-confirmed
  orders.order-cancelled
  inventory.stock-reserved
  inventory.stock-rejected
  payments.payment-succeeded
  payments.payment-failed
  catalog.product-price-changed
)

# plus <topic>.retry and <topic>.dlq for each
for topic in "${TOPICS[@]}"; do
  TOPICS+=("${topic}.retry" "${topic}.dlq")
done

for topic in "${TOPICS[@]}"; do
  kafka-topics.sh --bootstrap-server "$BOOTSTRAP" --create --if-not-exists \
    --topic "$topic" --partitions "$PARTITIONS" --replication-factor 1 \
    --config retention.ms=604800000
done

echo "Topics:"
kafka-topics.sh --bootstrap-server "$BOOTSTRAP" --list
