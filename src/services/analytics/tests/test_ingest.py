import uuid
from datetime import timedelta

from sqlalchemy import func, select

from app.models import DimEvent, DimVenueSection, FactCheckin, FactHold, FactOrder, FactRefund, FactTicketSale
from app.timeutil import now

from .conftest import env, iso, publish_event, sell


def count(db, model):
    with db.session() as s:
        return s.execute(select(func.count()).select_from(model)).scalar()


def test_event_published_builds_dimensions(ingestor, db):
    event_id, _ = publish_event(ingestor)
    with db.session() as s:
        assert s.get(DimEvent, event_id).capacity == 100
    assert count(db, DimVenueSection) == 2


def test_order_paid_creates_one_fact_per_seat(ingestor, db):
    event_id, sections = publish_event(ingestor)
    sell(ingestor, event_id, sections[0]["sectionId"], seats=3, price=40)
    with db.session() as s:
        rows = list(s.scalars(select(FactTicketSale)))
        hold = s.scalars(select(FactHold)).one()
        order = s.scalars(select(FactOrder)).one()
    assert len(rows) == 3
    assert {float(r.price) for r in rows} == {40.0}
    assert all(r.lead_days >= 29 for r in rows)
    assert hold.outcome == "converted"
    assert order.status == "paid"


def test_duplicate_envelope_is_ignored(ingestor, db):
    event_id, sections = publish_event(ingestor)
    message = env("orders.ticket-checked-in", {"ticketId": str(uuid.uuid4()), "eventId": event_id, "seatId": str(uuid.uuid4()),
                                               "staffId": str(uuid.uuid4()), "checkedInAt": iso(now())})
    assert ingestor.handle(message) is True
    assert ingestor.handle(message) is False
    assert count(db, FactCheckin) == 1


def test_late_release_does_not_downgrade_converted_hold(ingestor, db):
    event_id, sections = publish_event(ingestor)
    _, hold_id, seat_ids = sell(ingestor, event_id, sections[0]["sectionId"])
    ingestor.handle(env("seating.seat-released", {"holdId": hold_id, "eventId": event_id, "seatIds": seat_ids, "reason": "late"}))
    with db.session() as s:
        assert s.scalars(select(FactHold)).one().outcome == "converted"


def test_refund_recorded_once(ingestor, db):
    event_id, sections = publish_event(ingestor)
    order_id, _, _ = sell(ingestor, event_id, sections[0]["sectionId"])
    data = {"orderId": order_id, "eventId": event_id, "buyerId": str(uuid.uuid4()), "amount": 100, "ticketCount": 2,
            "reason": "requested", "refundedAt": iso(now())}
    ingestor.handle(env("orders.order-refunded", data))
    ingestor.handle(env("orders.order-refunded", data))
    assert count(db, FactRefund) == 1
    with db.session() as s:
        assert s.scalars(select(FactOrder)).one().status == "refunded"


def test_hold_expiry_marks_outcome(ingestor, db):
    event_id, _ = publish_event(ingestor)
    hold_id = str(uuid.uuid4())
    ingestor.handle(env("seating.seat-held", {"holdId": hold_id, "eventId": event_id, "buyerId": str(uuid.uuid4()),
                                              "heldAt": iso(now() - timedelta(minutes=11)), "expiresAt": iso(now()), "seats": [{}, {}]}))
    ingestor.handle(env("seating.hold-expired", {"holdId": hold_id, "eventId": event_id, "buyerId": str(uuid.uuid4()), "seatIds": []}))
    with db.session() as s:
        hold = s.scalars(select(FactHold)).one()
    assert hold.outcome == "expired" and hold.seats == 2
