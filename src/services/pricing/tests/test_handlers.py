import uuid
from datetime import UTC, datetime, timedelta
from decimal import Decimal

from sqlalchemy import select

from app import service
from app.models import PriceChange, PricingTier

from .conftest import envelope, published_event


def test_event_published_creates_tiers(handlers, db):
    evt = published_event()
    handlers.on_event_published(evt)
    with db.session() as s:
        tiers = list(s.scalars(select(PricingTier)))
    assert {t.name for t in tiers} == {"Front", "Standard"}
    assert all(t.current_price == t.base_price for t in tiers)


def test_duplicate_messages_are_ignored(handlers, db):
    evt = published_event()
    handlers.on_event_published(evt)
    tier = evt["data"]["tiers"][1]
    held = envelope("seating.seat-held", {
        "holdId": str(uuid.uuid4()), "eventId": evt["data"]["eventId"], "heldAt": datetime.now(UTC).isoformat(),
        "seats": [{"seatId": str(uuid.uuid4()), "tierId": tier["tierId"]}],
    })
    handlers.on_seat_held(held)
    handlers.on_seat_held(held)
    with db.session() as s:
        assert s.get(PricingTier, tier["tierId"]).held == 1


def test_lock_uses_price_in_effect_at_hold_time(db):
    evt = published_event()["data"]
    with db.session() as s:
        service.register_event(s, evt)
        s.commit()
        tier = s.get(PricingTier, evt["tiers"][0]["tierId"])
        held_at = datetime.now(UTC) - timedelta(minutes=5)
        s.add(PriceChange(event_id=evt["eventId"], tier_id=tier.tier_id, old_price=Decimal("80"), new_price=Decimal("95"),
                          sell_through=0.5, reason="demand-high", changed_at=service.now()))
        tier.current_price = Decimal("95")
        s.commit()

        locks = service.lock_prices(s, str(uuid.uuid4()), evt["eventId"], held_at, [(str(uuid.uuid4()), tier.tier_id)])
        assert locks[0].price == Decimal("80")


def test_lock_is_idempotent_per_hold(db):
    evt = published_event()["data"]
    hold_id, seat = str(uuid.uuid4()), str(uuid.uuid4())
    with db.session() as s:
        service.register_event(s, evt)
        first = service.lock_prices(s, hold_id, evt["eventId"], datetime.now(UTC), [(seat, evt["tiers"][1]["tierId"])])
        second = service.lock_prices(s, hold_id, evt["eventId"], datetime.now(UTC), [(seat, evt["tiers"][1]["tierId"])])
        s.commit()
        assert first[0].price == second[0].price
        assert s.get(PricingTier, evt["tiers"][1]["tierId"]).held == 1


def test_fast_sales_publish_price_update(handlers, db):
    evt = published_event(capacity_per_tier=20, days_ahead=40, days_on_sale=1)
    handlers.on_event_published(evt)
    tier = evt["data"]["tiers"][1]
    hold_id = str(uuid.uuid4())
    seats = [str(uuid.uuid4()) for _ in range(8)]
    handlers.on_seat_held(envelope("seating.seat-held", {
        "holdId": hold_id, "eventId": evt["data"]["eventId"], "heldAt": datetime.now(UTC).isoformat(),
        "seats": [{"seatId": sid, "tierId": tier["tierId"]} for sid in seats],
    }))
    handlers.on_seats_sold(envelope("seating.seats-sold", {
        "eventId": evt["data"]["eventId"], "orderId": str(uuid.uuid4()), "holdId": hold_id, "seatIds": seats, "soldTotal": 8,
    }))

    with db.session() as s:
        t = s.get(PricingTier, tier["tierId"])
        assert t.sold == 8 and t.held == 0
        assert t.current_price > Decimal("50")
    assert any(topic == "pricing.price-updated" for topic, _ in handlers.publisher.sent)


def test_expired_hold_releases_held_count(handlers, db):
    evt = published_event()
    handlers.on_event_published(evt)
    tier = evt["data"]["tiers"][0]
    hold_id = str(uuid.uuid4())
    handlers.on_seat_held(envelope("seating.seat-held", {
        "holdId": hold_id, "eventId": evt["data"]["eventId"], "heldAt": datetime.now(UTC).isoformat(),
        "seats": [{"seatId": str(uuid.uuid4()), "tierId": tier["tierId"]}],
    }))
    handlers.on_hold_ended(envelope("seating.hold-expired", {"holdId": hold_id, "eventId": evt["data"]["eventId"], "seatIds": []}))
    with db.session() as s:
        assert s.get(PricingTier, tier["tierId"]).held == 0
