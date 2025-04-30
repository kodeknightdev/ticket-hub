import uuid
from datetime import UTC, datetime

from .conftest import published_event


def test_health_and_alive(client):
    assert client.get("/alive").json()["status"] == "Healthy"
    assert client.get("/health").status_code == 200


def test_prices_and_locks_roundtrip(client):
    evt = published_event()
    client.app.state.handlers.on_event_published(evt)
    event_id = evt["data"]["eventId"]

    prices = client.get(f"/prices/{event_id}").json()
    assert len(prices["tiers"]) == 2
    assert "tierId" in prices["tiers"][0]

    hold_id, seat_id = str(uuid.uuid4()), str(uuid.uuid4())
    body = {"holdId": hold_id, "eventId": event_id, "heldAt": datetime.now(UTC).isoformat(),
            "seats": [{"seatId": seat_id, "tierId": evt["data"]["tiers"][0]["tierId"]}]}
    locked = client.post("/prices/locks", json=body)
    assert locked.status_code == 200
    assert float(locked.json()["prices"][0]["price"]) == 80.0
    assert client.get(f"/prices/locks/{hold_id}").status_code == 200


def test_lock_for_unknown_event_is_retryable(client):
    body = {"holdId": str(uuid.uuid4()), "eventId": str(uuid.uuid4()), "heldAt": datetime.now(UTC).isoformat(),
            "seats": [{"seatId": str(uuid.uuid4()), "tierId": str(uuid.uuid4())}]}
    assert client.post("/prices/locks", json=body).status_code == 503
