#!/usr/bin/env bash
# Entrypoint of the kafka container: starts the broker exactly as the image does, and makes sure the SASL/SCRAM users exist.
#
# Every client logs in with SCRAM-SHA-512 (user and salted password hash in KRaft's metadata, nothing in files). The users are
# created, or their passwords updated, at every start over a listener that is bound to 127.0.0.1 inside this container
# (BROKER, PLAINTEXT): nothing on the network can reach it. That way the users also appear in Kafka data that was formatted before
# authentication existed; the image formats the storage itself and has no option to add SCRAM users at that moment.
# The same local listener is the broker's own (inter-broker) listener, so the broker never has to log in to itself.
#
# Without TLS (SASL_PLAINTEXT) the SCRAM exchange does not send the password itself, but everything after the login travels in
# clear text, and anyone on the network can read the messages. Production would use SASL_SSL (TLS) on both client listeners.
set -euo pipefail

READY=/tmp/scram-users-ready
rm -f "$READY"

# For the scripts and tools that talk to Kafka from inside this container (pipeline-status.sh, the demo, docs):
#   docker compose exec kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server kafka:9092 --command-config /tmp/admin-client.properties --list
cat > /tmp/admin-client.properties <<PROPERTIES
security.protocol=SASL_PLAINTEXT
sasl.mechanism=SCRAM-SHA-512
sasl.jaas.config=org.apache.kafka.common.security.scram.ScramLoginModule required username="kafka-admin" password="${KAFKA_ADMIN_PASSWORD}";
PROPERTIES

create_users() {
  # The tools are short-lived JVMs: a small heap, not the broker's.
  export KAFKA_HEAP_OPTS="-Xmx128m"
  until /opt/kafka/bin/kafka-broker-api-versions.sh --bootstrap-server localhost:9095 > /dev/null 2>&1; do sleep 1; done
  for user in kafka-admin catalog recommendation batch; do
    case "$user" in
      kafka-admin) password=$KAFKA_ADMIN_PASSWORD ;;
      catalog) password=$KAFKA_CATALOG_PASSWORD ;;
      recommendation) password=$KAFKA_RECOMMENDATION_PASSWORD ;;
      batch) password=$KAFKA_BATCH_PASSWORD ;;
    esac
    /opt/kafka/bin/kafka-configs.sh --bootstrap-server localhost:9095 --alter --entity-type users --entity-name "$user" \
      --add-config "SCRAM-SHA-512=[iterations=8192,password=${password}]" > /dev/null
  done
  touch "$READY" # the healthcheck waits for this
  echo "SCRAM users ready: kafka-admin, catalog, recommendation, batch"
}

create_users &
exec /__cacert_entrypoint.sh /etc/kafka/docker/run
