"""Read models for the dashboard. Everything takes a Filters object; event_ids is how the
dashboard BFF scopes an organizer to their own events."""

from dataclasses import dataclass
from datetime import date, datetime, timedelta

import numpy as np
import pandas as pd
from sqlalchemy import func, select, true
from sqlalchemy.orm import Session

from . import forecasting
from .models import (
    Alert, DailyEventSales, DimEvent, DimVenueSection, FactCheckin, FactHold, FactOrder, FactPriceChange,
    FactTicketSale, ForecastBacktest, ForecastDaily, ForecastPlatformRevenue, ForecastSummary,
)
from .timeutil import now, today

LEAD_BUCKETS = [(0, 1, "0-1 days"), (2, 7, "2-7 days"), (8, 14, "8-14 days"), (15, 30, "15-30 days"),
                (31, 60, "31-60 days"), (61, 90, "61-90 days"), (91, 10_000, "90+ days")]


@dataclass
class Filters:
    start: date
    end: date
    event_ids: list[str] | None = None
    city: str | None = None
    category: str | None = None
    venue: str | None = None
    organizer_id: str | None = None

    def previous(self) -> "Filters":
        length = (self.end - self.start).days + 1
        return Filters(self.start - timedelta(days=length), self.start - timedelta(days=1), self.event_ids,
                       self.city, self.category, self.venue, self.organizer_id)


def scoped_event_ids(s: Session, f: Filters) -> list[str] | None:
    """None means unrestricted. An empty list means the scope matches nothing."""
    if f.event_ids is None and not any([f.city, f.category, f.venue, f.organizer_id]):
        return None
    q = select(DimEvent.event_id)
    if f.event_ids is not None:
        if not f.event_ids:
            return []
        q = q.where(DimEvent.event_id.in_([e.lower() for e in f.event_ids]))
    if f.city:
        q = q.where(DimEvent.city == f.city)
    if f.category:
        q = q.where(DimEvent.category == f.category)
    if f.venue:
        q = q.where((DimEvent.venue_id == f.venue.lower()) | (DimEvent.venue_name == f.venue))
    if f.organizer_id:
        q = q.where(DimEvent.organizer_id == f.organizer_id.lower())
    return list(s.scalars(q))


def _scope(q, column, ids: list[str] | None):
    return q if ids is None else q.where(column.in_(ids or ["__none__"]))


def daily_sales(s: Session, f: Filters, ids: list[str] | None = None) -> pd.DataFrame:
    ids = scoped_event_ids(s, f) if ids is None else ids
    q = select(DailyEventSales.event_id, DailyEventSales.date, DailyEventSales.tickets, DailyEventSales.revenue,
               DailyEventSales.refund_amount, DailyEventSales.refunded_tickets).where(DailyEventSales.date.between(f.start, f.end))
    q = _scope(q, DailyEventSales.event_id, ids)
    df = pd.DataFrame(s.execute(q).all(), columns=["event_id", "date", "tickets", "revenue", "refund_amount", "refunded_tickets"])
    for col in ("revenue", "refund_amount"):
        df[col] = df[col].astype(float)
    return df


def _period_kpis(s: Session, f: Filters, ids: list[str] | None) -> dict[str, float]:
    df = daily_sales(s, f, ids)
    gross = float(df["revenue"].sum())
    refunds = float(df["refund_amount"].sum())
    tickets = int(df["tickets"].sum())
    refunded = int(df["refunded_tickets"].sum())

    start_dt, end_dt = datetime.combine(f.start, datetime.min.time()), datetime.combine(f.end + timedelta(days=1), datetime.min.time())
    evq = _scope(select(DimEvent.event_id, DimEvent.capacity, DimEvent.starts_at)
                 .where(DimEvent.starts_at >= start_dt, DimEvent.starts_at < end_dt, DimEvent.status == "published"), DimEvent.event_id, ids)
    events = s.execute(evq).all()
    event_ids = [e[0] for e in events]
    sold_by_event = dict(s.execute(select(FactTicketSale.event_id, func.count()).where(FactTicketSale.event_id.in_(event_ids or ["__none__"]))
                                   .group_by(FactTicketSale.event_id)).all())
    capacity = sum(e[1] for e in events)
    sold_for_events = sum(sold_by_event.values())

    past_ids = [e[0] for e in events if e[2] <= now()]
    checked = s.execute(select(func.count()).select_from(FactCheckin).where(FactCheckin.event_id.in_(past_ids or ["__none__"]))).scalar() or 0
    past_sold = sum(sold_by_event.get(i, 0) for i in past_ids)

    return {
        "grossRevenue": round(gross, 2),
        "netRevenue": round(gross - refunds, 2),
        "ticketsSold": tickets,
        "avgTicketPrice": round(gross / tickets, 2) if tickets else 0.0,
        "sellThrough": round(sold_for_events / capacity, 4) if capacity else 0.0,
        "checkInRate": round(checked / past_sold, 4) if past_sold else 0.0,
        "refundRate": round(refunded / tickets, 4) if tickets else 0.0,
    }


def _trend(df: pd.DataFrame, f: Filters) -> dict[str, list]:
    index = pd.date_range(f.start, f.end, freq="D")
    daily = df.groupby("date").agg(revenue=("revenue", "sum"), tickets=("tickets", "sum"), refunds=("refund_amount", "sum")) if not df.empty \
        else pd.DataFrame(columns=["revenue", "tickets", "refunds"])
    daily.index = pd.to_datetime(daily.index)
    daily = daily.reindex(index, fill_value=0)
    if len(index) > 62:
        daily = daily.resample("W-MON", label="left", closed="left").sum()
    avg = (daily["revenue"] / daily["tickets"].replace(0, np.nan)).fillna(0)
    return {
        "dates": [d.date().isoformat() for d in daily.index],
        "grossRevenue": [round(float(x), 2) for x in daily["revenue"]],
        "netRevenue": [round(float(x), 2) for x in daily["revenue"] - daily["refunds"]],
        "ticketsSold": [int(x) for x in daily["tickets"]],
        "avgTicketPrice": [round(float(x), 2) for x in avg],
    }


def kpis(s: Session, f: Filters) -> dict:
    ids = scoped_event_ids(s, f)
    current = _period_kpis(s, f, ids)
    previous = _period_kpis(s, f.previous(), ids)
    cards = []
    for key, value in current.items():
        prev = previous[key]
        change = None if prev == 0 else round((value - prev) / abs(prev), 4)
        cards.append({"key": key, "value": value, "previous": prev, "change": change})
    return {"from": f.start.isoformat(), "to": f.end.isoformat(), "kpis": cards, "trend": _trend(daily_sales(s, f, ids), f)}


def timeseries(s: Session, f: Filters, granularity: str = "day") -> dict:
    df = daily_sales(s, f)
    rule = {"day": "D", "week": "W-MON", "month": "MS"}.get(granularity, "D")
    index = pd.date_range(f.start, f.end, freq="D")
    daily = df.groupby("date").agg(revenue=("revenue", "sum"), tickets=("tickets", "sum"), refunds=("refund_amount", "sum")) if not df.empty \
        else pd.DataFrame(columns=["revenue", "tickets", "refunds"])
    daily.index = pd.to_datetime(daily.index)
    daily = daily.reindex(index, fill_value=0)
    if rule != "D":
        daily = daily.resample(rule, label="left", closed="left").sum()
    return {
        "granularity": granularity,
        "points": [{"date": d.date().isoformat(), "revenue": round(float(r.revenue), 2), "tickets": int(r.tickets),
                    "refunds": round(float(r.refunds), 2), "netRevenue": round(float(r.revenue - r.refunds), 2)}
                   for d, r in daily.iterrows()],
    }


def _with_events(s: Session, df: pd.DataFrame) -> pd.DataFrame:
    events = pd.DataFrame(s.execute(select(DimEvent.event_id, DimEvent.title, DimEvent.artist, DimEvent.category, DimEvent.city,
                                           DimEvent.venue_id, DimEvent.venue_name, DimEvent.organizer_id)).all(),
                          columns=["event_id", "title", "artist", "category", "city", "venue_id", "venue_name", "organizer_id"])
    return df.merge(events, on="event_id", how="left")


def breakdown(s: Session, f: Filters, by: str, limit: int = 10) -> dict:
    key = {"event": "event_id", "artist": "artist", "category": "category", "city": "city", "venue": "venue_name"}.get(by)
    if key is None:
        raise ValueError(f"unknown breakdown '{by}'")
    df = _with_events(s, daily_sales(s, f))
    if df.empty:
        return {"by": by, "rows": []}
    grouped = df.groupby(key).agg(revenue=("revenue", "sum"), tickets=("tickets", "sum"), refunds=("refund_amount", "sum"),
                                  events=("event_id", "nunique"), label=("title" if by == "event" else key, "first"))
    grouped = grouped.sort_values("revenue", ascending=False).head(limit)
    return {"by": by, "rows": [
        {"key": k, "label": r.label, "revenue": round(float(r.revenue), 2), "tickets": int(r.tickets),
         "avgPrice": round(float(r.revenue / r.tickets), 2) if r.tickets else 0.0, "events": int(r.events)}
        for k, r in grouped.iterrows()]}


def section_heatmap(s: Session, f: Filters, limit: int = 12) -> dict:
    ids = scoped_event_ids(s, f)
    top = breakdown(s, f, "event", limit)["rows"]
    event_ids = [r["key"] for r in top]
    if ids is not None:
        event_ids = [e for e in event_ids if e in ids]
    sections = s.execute(select(DimVenueSection.section_id, DimVenueSection.event_id, DimVenueSection.name, DimVenueSection.capacity)
                         .where(DimVenueSection.event_id.in_(event_ids or ["__none__"]))).all()
    sold = dict(s.execute(select(FactTicketSale.section_id, func.count()).where(FactTicketSale.event_id.in_(event_ids or ["__none__"]))
                          .group_by(FactTicketSale.section_id)).all())
    names = sorted({sec[2] for sec in sections})
    titles = {r["key"]: r["label"] for r in top}
    cells = [{"eventId": sec[1], "event": titles.get(sec[1], sec[1]), "section": sec[2], "sold": sold.get(sec[0], 0), "capacity": sec[3],
              "fill": round(sold.get(sec[0], 0) / sec[3], 4) if sec[3] else 0} for sec in sections]
    return {"events": [titles[e] for e in event_ids if e in titles], "sections": names, "cells": cells}


def performance(s: Session, f: Filters) -> dict:
    ids = scoped_event_ids(s, f)
    start_dt = datetime.combine(f.start, datetime.min.time())
    q = _scope(select(DimEvent).where(DimEvent.starts_at >= start_dt), DimEvent.event_id, ids)
    events = list(s.scalars(q.order_by(DimEvent.starts_at)))
    event_ids = [e.event_id for e in events]
    summaries = {x.event_id: x for x in s.scalars(select(ForecastSummary).where(ForecastSummary.event_id.in_(event_ids or ["__none__"])))}
    totals = {r[0]: (int(r[1]), float(r[2] or 0)) for r in s.execute(
        select(FactTicketSale.event_id, func.count(), func.sum(FactTicketSale.price))
        .where(FactTicketSale.event_id.in_(event_ids or ["__none__"])).group_by(FactTicketSale.event_id)).all()}

    current = today()
    rows = []
    for e in events:
        sold, revenue = totals.get(e.event_id, (0, 0.0))
        summary = summaries.get(e.event_id)
        status = "Cancelled" if e.status == "cancelled" else summary.pace_status if summary else ("Sold out" if sold >= e.capacity else "On track")
        rows.append({
            "eventId": e.event_id, "title": e.title, "artist": e.artist, "category": e.category, "city": e.city, "venue": e.venue_name,
            "organizerId": e.organizer_id, "startsAt": e.starts_at.isoformat() + "Z", "capacity": e.capacity, "sold": sold,
            "revenue": round(revenue, 2), "avgPrice": round(revenue / sold, 2) if sold else 0.0,
            "daysToEvent": (e.starts_at.date() - current).days, "paceStatus": status,
            "projectedSellThrough": summary.projected_sell_through if summary else round(sold / e.capacity, 4) if e.capacity else 0,
            "expectedToDate": summary.expected_to_date if summary else None,
            "predictedSelloutDate": summary.predicted_sellout_date.isoformat() if summary and summary.predicted_sellout_date else None,
            "model": summary.model if summary else None,
        })
    return {"rows": rows}


def event_insights(s: Session, event_id: str) -> dict | None:
    evt = s.get(DimEvent, event_id.lower())
    if evt is None:
        return None
    daily = pd.DataFrame(s.execute(select(DailyEventSales.date, DailyEventSales.tickets, DailyEventSales.revenue)
                                   .where(DailyEventSales.event_id == evt.event_id).order_by(DailyEventSales.date)).all(),
                         columns=["date", "tickets", "revenue"])
    actual, velocity = [], []
    if not daily.empty:
        end = min(today(), evt.starts_at.date())
        idx = pd.date_range(evt.published_at.date(), end, freq="D")
        series = daily.set_index(pd.to_datetime(daily["date"]))["tickets"].reindex(idx, fill_value=0)
        cum = series.cumsum()
        roll = series.rolling(7, min_periods=1).mean()
        actual = [{"date": d.date().isoformat(), "tickets": int(t), "cumulative": int(c)} for d, t, c in zip(idx, series, cum)]
        velocity = [{"date": d.date().isoformat(), "value": round(float(v), 2)} for d, v in zip(idx, roll)]

    forecast = [{"date": r.date.isoformat(), "predicted": r.predicted, "lower": r.lower, "upper": r.upper, "cumulative": r.cumulative,
                 "cumulativeLower": r.cumulative_lower, "cumulativeUpper": r.cumulative_upper}
                for r in s.scalars(select(ForecastDaily).where(ForecastDaily.event_id == evt.event_id).order_by(ForecastDaily.date))]
    summary = s.get(ForecastSummary, evt.event_id)
    price_changes = [{"tierId": p.tier_id, "oldPrice": float(p.old_price), "newPrice": float(p.new_price), "reason": p.reason,
                      "sellThrough": p.sell_through, "changedAt": p.changed_at.isoformat() + "Z"}
                     for p in s.scalars(select(FactPriceChange).where(FactPriceChange.event_id == evt.event_id).order_by(FactPriceChange.changed_at))]
    sections = s.execute(select(DimVenueSection.section_id, DimVenueSection.name, DimVenueSection.capacity).where(DimVenueSection.event_id == evt.event_id)).all()
    sold = dict(s.execute(select(FactTicketSale.section_id, func.count()).where(FactTicketSale.event_id == evt.event_id).group_by(FactTicketSale.section_id)).all())

    return {
        "event": {"eventId": evt.event_id, "title": evt.title, "artist": evt.artist, "category": evt.category, "city": evt.city,
                  "venue": evt.venue_name, "capacity": evt.capacity, "startsAt": evt.starts_at.isoformat() + "Z",
                  "publishedAt": evt.published_at.isoformat() + "Z", "status": evt.status, "organizerId": evt.organizer_id},
        "actual": actual,
        "velocity": velocity,
        "forecast": forecast,
        "summary": None if summary is None else {
            "model": summary.model, "soldToDate": summary.sold_to_date, "projectedFinal": summary.projected_final,
            "projectedSellThrough": summary.projected_sell_through, "paceStatus": summary.pace_status,
            "predictedSelloutDate": summary.predicted_sellout_date.isoformat() if summary.predicted_sellout_date else None,
            "generatedAt": summary.generated_at.isoformat() + "Z"},
        "priceChanges": price_changes,
        "sections": [{"sectionId": sid, "name": name, "capacity": cap, "sold": sold.get(sid, 0),
                      "fill": round(sold.get(sid, 0) / cap, 4) if cap else 0} for sid, name, cap in sections],
    }


def latest_backtest(s: Session) -> dict | None:
    b = s.scalars(select(ForecastBacktest).order_by(ForecastBacktest.generated_at.desc()).limit(1)).first()
    return None if b is None else {"model": b.model, "mape": b.mape, "events": b.events, "generatedAt": b.generated_at.isoformat() + "Z"}


def revenue_forecast(s: Session, f: Filters, horizon: int) -> dict:
    ids = scoped_event_ids(s, f)
    current = today()
    history_start = current - timedelta(days=180)
    if ids is None:
        rows = s.scalars(select(ForecastPlatformRevenue).order_by(ForecastPlatformRevenue.date)).all()
        fc = forecasting.RevenueForecast([r.date for r in rows], [r.predicted for r in rows], [r.lower for r in rows], [r.upper for r in rows])
    else:
        # scoped forecasts are cheap enough to fit on request; the BFF caches the response
        hist = daily_sales(s, Filters(history_start, current - timedelta(days=1)), ids)
        series = hist.groupby("date")["revenue"].sum() if not hist.empty else pd.Series(dtype=float)
        series = series.reindex(pd.date_range(history_start, current - timedelta(days=1), freq="D").date, fill_value=0.0)
        fc = forecasting.forecast_revenue(series, current, 90)

    hist = daily_sales(s, Filters(current - timedelta(days=90), current - timedelta(days=1)), ids)
    actual = hist.groupby("date")["revenue"].sum() if not hist.empty else pd.Series(dtype=float)
    n = min(horizon, len(fc.dates))
    return {
        "horizon": horizon,
        "history": [{"date": d.isoformat(), "revenue": round(float(v), 2)} for d, v in actual.items()],
        "forecast": [{"date": d.isoformat(), "predicted": p, "lower": lo, "upper": up}
                     for d, p, lo, up in zip(fc.dates[:n], fc.predicted[:n], fc.lower[:n], fc.upper[:n])],
        "totals": {str(h): fc.total(h) for h in (30, 60, 90)},
        "accuracy": latest_backtest(s),
    }


def demand_heatmap(s: Session, f: Filters) -> dict:
    ids = scoped_event_ids(s, f)
    q = select(FactTicketSale.weekday, FactTicketSale.hour, func.count()).where(
        FactTicketSale.sold_at >= datetime.combine(f.start, datetime.min.time()),
        FactTicketSale.sold_at < datetime.combine(f.end + timedelta(days=1), datetime.min.time()))
    q = _scope(q, FactTicketSale.event_id, ids).group_by(FactTicketSale.weekday, FactTicketSale.hour)
    matrix = [[0] * 24 for _ in range(7)]
    for weekday, hour, count in s.execute(q).all():
        matrix[weekday][hour] = int(count)
    return {"weekdays": ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], "hours": list(range(24)), "matrix": matrix}


def lead_time(s: Session, f: Filters) -> dict:
    ids = scoped_event_ids(s, f)
    q = select(FactTicketSale.lead_days).where(
        FactTicketSale.sold_at >= datetime.combine(f.start, datetime.min.time()),
        FactTicketSale.sold_at < datetime.combine(f.end + timedelta(days=1), datetime.min.time()))
    leads = np.array(list(s.scalars(_scope(q, FactTicketSale.event_id, ids))), dtype=float)
    buckets = [{"label": label, "tickets": int(((leads >= lo) & (leads <= hi)).sum())} for lo, hi, label in LEAD_BUCKETS]
    return {"buckets": buckets, "median": float(np.median(leads)) if len(leads) else 0.0, "mean": round(float(leads.mean()), 1) if len(leads) else 0.0}


def funnel(s: Session, f: Filters) -> dict:
    ids = scoped_event_ids(s, f)
    lo, hi = datetime.combine(f.start, datetime.min.time()), datetime.combine(f.end + timedelta(days=1), datetime.min.time())
    holds = s.execute(_scope(select(FactHold.hold_id, FactHold.seats, FactHold.outcome).where(FactHold.held_at >= lo, FactHold.held_at < hi),
                             FactHold.event_id, ids)).all()
    seats_by_hold = {h[0]: h[1] for h in holds}
    orders = s.execute(_scope(select(FactOrder.hold_id, FactOrder.status, FactOrder.paid_at).where(FactOrder.created_at >= lo, FactOrder.created_at < hi),
                              FactOrder.event_id, ids)).all()
    sold = s.execute(_scope(select(func.count()).select_from(FactTicketSale).where(FactTicketSale.sold_at >= lo, FactTicketSale.sold_at < hi),
                            FactTicketSale.event_id, ids)).scalar() or 0
    checked = s.execute(_scope(select(func.count()).select_from(FactCheckin).where(FactCheckin.checked_in_at >= lo, FactCheckin.checked_in_at < hi),
                               FactCheckin.event_id, ids)).scalar() or 0

    held_seats = sum(seats_by_hold.values())
    ordered_seats = sum(seats_by_hold.get(o[0], 0) for o in orders)
    expired = sum(1 for h in holds if h[2] == "expired")
    stages = [
        {"stage": "Seats held", "value": held_seats},
        {"stage": "Orders created", "value": ordered_seats},
        {"stage": "Paid", "value": int(sold)},
        {"stage": "Checked in", "value": int(checked)},
    ]
    return {
        "stages": stages,
        "holds": len(holds),
        "orders": len(orders),
        "paidOrders": sum(1 for o in orders if o[2] is not None),
        "holdExpiryRate": round(expired / len(holds), 4) if holds else 0.0,
        "holdConversionRate": round(sum(1 for h in holds if h[2] == "converted") / len(holds), 4) if holds else 0.0,
    }


def pricing_impact(s: Session, f: Filters, limit: int = 50) -> dict:
    ids = scoped_event_ids(s, f)
    lo, hi = datetime.combine(f.start, datetime.min.time()), datetime.combine(f.end + timedelta(days=1), datetime.min.time())
    changes = list(s.scalars(_scope(select(FactPriceChange).where(FactPriceChange.changed_at >= lo, FactPriceChange.changed_at < hi),
                                    FactPriceChange.event_id, ids).order_by(FactPriceChange.changed_at.desc()).limit(limit)))
    titles = dict(s.execute(select(DimEvent.event_id, DimEvent.title)).all())
    rows = []
    for c in changes:
        before = s.execute(select(func.count()).select_from(FactTicketSale).where(
            FactTicketSale.event_id == c.event_id, FactTicketSale.tier_id == c.tier_id,
            FactTicketSale.sold_at >= c.changed_at - timedelta(days=7), FactTicketSale.sold_at < c.changed_at)).scalar() or 0
        after_end = min(c.changed_at + timedelta(days=7), now())
        days_after = max((after_end - c.changed_at).total_seconds() / 86400, 1 / 24)
        after = s.execute(select(func.count()).select_from(FactTicketSale).where(
            FactTicketSale.event_id == c.event_id, FactTicketSale.tier_id == c.tier_id,
            FactTicketSale.sold_at >= c.changed_at, FactTicketSale.sold_at < after_end)).scalar() or 0
        v_before, v_after = before / 7, after / days_after
        rows.append({
            "eventId": c.event_id, "event": titles.get(c.event_id, c.event_id), "tierId": c.tier_id,
            "changedAt": c.changed_at.isoformat() + "Z", "reason": c.reason,
            "oldPrice": float(c.old_price), "newPrice": float(c.new_price),
            "priceChange": round((float(c.new_price) - float(c.old_price)) / float(c.old_price), 4) if c.old_price else 0,
            "velocityBefore": round(v_before, 2), "velocityAfter": round(v_after, 2),
            "velocityChange": round((v_after - v_before) / v_before, 4) if v_before else None,
        })

    def avg(xs):
        xs = [x for x in xs if x is not None]
        return round(sum(xs) / len(xs), 4) if xs else None

    ups = [r for r in rows if r["priceChange"] > 0]
    downs = [r for r in rows if r["priceChange"] < 0]
    return {"rows": rows, "summary": {
        "increases": len(ups), "decreases": len(downs),
        "avgVelocityChangeAfterIncrease": avg([r["velocityChange"] for r in ups]),
        "avgVelocityChangeAfterDecrease": avg([r["velocityChange"] for r in downs]),
    }}


def active_alerts(s: Session, f: Filters) -> list[dict]:
    ids = scoped_event_ids(s, f)
    q = select(Alert).where(Alert.active == true())
    if ids is not None:
        q = q.where(Alert.event_id.in_(ids or ["__none__"]))
    order = {"high": 0, "medium": 1, "info": 2}
    alerts = sorted(s.scalars(q), key=lambda a: (order.get(a.severity, 3), a.message))
    return [{"id": a.id, "rule": a.rule, "severity": a.severity, "eventId": a.event_id, "message": a.message, "value": a.value,
             "createdAt": a.created_at.isoformat() + "Z"} for a in alerts]


def event_ids_for_organizer(s: Session, organizer_id: str) -> list[str]:
    return list(s.scalars(select(DimEvent.event_id).where(DimEvent.organizer_id == organizer_id.lower())))


def filter_options(s: Session, ids: list[str] | None) -> dict:
    q = _scope(select(DimEvent.city, DimEvent.category, DimEvent.venue_id, DimEvent.venue_name, DimEvent.organizer_id), DimEvent.event_id, ids)
    rows = s.execute(q).all()
    return {
        "cities": sorted({r[0] for r in rows}),
        "categories": sorted({r[1] for r in rows}),
        "venues": sorted([{"id": vid, "name": name} for vid, name in {(r[2], r[3]) for r in rows}], key=lambda v: v["name"]),
        "organizers": sorted({r[4] for r in rows}),
    }
