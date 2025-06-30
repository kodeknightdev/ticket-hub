import uuid
from datetime import timedelta

from app.alerts import evaluate
from app.models import ForecastSummary
from app.timeutil import now

from .conftest import env, iso, publish_event, sell


def test_low_projected_sell_through_raises_alert(ingestor, db):
    event_id, _ = publish_event(ingestor, title="Quiet Night")
    with db.session() as s:
        s.add(ForecastSummary(event_id=event_id, model="holt-winters", sold_to_date=20, expected_to_date=40, projected_final=55,
                              projected_sell_through=0.55, predicted_sellout_date=None, pace_status="At risk", generated_at=now()))
        s.commit()
        alerts = evaluate(s)
    assert any(a.rule == "low-sell-through" and "55%" in a.message and "Quiet Night" in a.message for a in alerts)


def test_low_checkin_rate_after_event(ingestor, db):
    event_id, sections = publish_event(ingestor, days_ahead=-2, published_days_ago=40, title="Past Show")
    for _ in range(10):
        sell(ingestor, event_id, sections[0]["sectionId"], seats=3, at=now() - timedelta(days=10))
    for _ in range(12):
        ingestor.handle(env("orders.ticket-checked-in", {"ticketId": str(uuid.uuid4()), "eventId": event_id, "seatId": str(uuid.uuid4()),
                                                         "staffId": str(uuid.uuid4()), "checkedInAt": iso(now() - timedelta(days=2))}))
    with db.session() as s:
        alerts = evaluate(s)
    assert any(a.rule == "low-checkin" and a.event_id == event_id for a in alerts)


def test_refund_spike(ingestor, db):
    event_id, sections = publish_event(ingestor, title="Messy Gig")
    orders = [sell(ingestor, event_id, sections[0]["sectionId"], seats=2)[0] for _ in range(10)]
    for order_id in orders[:4]:
        ingestor.handle(env("orders.order-refunded", {"orderId": order_id, "eventId": event_id, "buyerId": str(uuid.uuid4()),
                                                      "amount": 100, "ticketCount": 2, "reason": "requested", "refundedAt": iso(now())}))
    with db.session() as s:
        alerts = evaluate(s)
    assert any(a.rule == "refund-spike" and "Messy Gig" in a.message for a in alerts)
