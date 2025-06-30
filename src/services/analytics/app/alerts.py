from dataclasses import dataclass
from datetime import timedelta

from sqlalchemy import func, select, update
from sqlalchemy.orm import Session

from .models import Alert, DimEvent, FactCheckin, FactRefund, FactTicketSale, ForecastSummary
from .timeutil import now

LOW_SELL_THROUGH = 0.60
LOW_CHECKIN = 0.70
REFUND_RATE_SPIKE = 0.10


@dataclass
class AlertCandidate:
    rule: str
    severity: str
    event_id: str | None
    message: str
    value: float


def evaluate(s: Session) -> list[AlertCandidate]:
    at = now()
    out: list[AlertCandidate] = []
    events = {e.event_id: e for e in s.scalars(select(DimEvent).where(DimEvent.status == "published"))}

    for summary in s.scalars(select(ForecastSummary)):
        evt = events.get(summary.event_id)
        if evt is None or evt.starts_at <= at or evt.starts_at > at + timedelta(days=60):
            continue
        if summary.projected_sell_through < LOW_SELL_THROUGH:
            pct = round(summary.projected_sell_through * 100)
            out.append(AlertCandidate("low-sell-through", "high" if pct < 40 else "medium", evt.event_id,
                                      f"{evt.title} is forecast to reach only {pct}% sell-through", summary.projected_sell_through))
        if summary.predicted_sellout_date and summary.predicted_sellout_date <= (at + timedelta(days=3)).date():
            out.append(AlertCandidate("sell-out-soon", "info", evt.event_id,
                                      f"{evt.title} is predicted to sell out by {summary.predicted_sellout_date:%d %b}", 1.0))

    week_ago = at - timedelta(days=7)
    refunds = dict(s.execute(select(FactRefund.event_id, func.sum(FactRefund.tickets)).where(FactRefund.refunded_at >= week_ago)
                             .group_by(FactRefund.event_id)).all())
    sold = dict(s.execute(select(FactTicketSale.event_id, func.count()).group_by(FactTicketSale.event_id)).all())
    for event_id, refunded in refunds.items():
        total = sold.get(event_id, 0)
        evt = events.get(event_id)
        if evt is None or total == 0:
            continue
        rate = (refunded or 0) / total
        if rate >= REFUND_RATE_SPIKE and (refunded or 0) >= 5:
            out.append(AlertCandidate("refund-spike", "high", event_id,
                                      f"Refund spike for {evt.title}: {refunded} tickets refunded in the last 7 days ({rate:.0%})", rate))

    checkins = dict(s.execute(select(FactCheckin.event_id, func.count()).group_by(FactCheckin.event_id)).all())
    for evt in events.values():
        if not (at - timedelta(days=14) <= evt.starts_at <= at):
            continue
        total = sold.get(evt.event_id, 0)
        if total < 20:
            continue
        rate = checkins.get(evt.event_id, 0) / total
        if rate < LOW_CHECKIN:
            out.append(AlertCandidate("low-checkin", "medium", evt.event_id,
                                      f"Check-in rate for {evt.title} was {rate:.0%}, under the 70% threshold", rate))
    return out


def refresh_alerts(s: Session) -> int:
    """Alerts are a snapshot: the previous set is deactivated and the current rules re-evaluated."""
    candidates = evaluate(s)
    s.execute(update(Alert).where(Alert.active.is_(True)).values(active=False))
    created = now()
    s.add_all(Alert(rule=c.rule, severity=c.severity, event_id=c.event_id, message=c.message[:400], value=c.value,
                    created_at=created, active=True) for c in candidates)
    s.commit()
    return len(candidates)
