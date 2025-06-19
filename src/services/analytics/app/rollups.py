from datetime import date
from decimal import Decimal

import pandas as pd
from sqlalchemy import delete, func, select
from sqlalchemy.orm import Session

from .ingest import ensure_dates
from .models import DailyEventSales, DailyPlatformSales, FactOrder, FactRefund, FactTicketSale


def key_to_date(series: pd.Series) -> pd.Series:
    return pd.to_datetime(series.astype(str), format="%Y%m%d").dt.date


def sales_frame(s: Session) -> pd.DataFrame:
    rows = s.execute(
        select(FactTicketSale.event_id, FactTicketSale.date_key, func.count().label("tickets"), func.sum(FactTicketSale.price).label("revenue"))
        .group_by(FactTicketSale.event_id, FactTicketSale.date_key)
    ).all()
    df = pd.DataFrame(rows, columns=["event_id", "date_key", "tickets", "revenue"])
    if df.empty:
        return df.assign(date=pd.Series(dtype="object"))
    df["revenue"] = df["revenue"].astype(float)
    df["date"] = key_to_date(df["date_key"])
    return df


def refunds_frame(s: Session) -> pd.DataFrame:
    rows = s.execute(
        select(FactRefund.event_id, FactRefund.date_key, func.sum(FactRefund.amount).label("refund_amount"), func.sum(FactRefund.tickets).label("refunded_tickets"))
        .group_by(FactRefund.event_id, FactRefund.date_key)
    ).all()
    df = pd.DataFrame(rows, columns=["event_id", "date_key", "refund_amount", "refunded_tickets"])
    if not df.empty:
        df["refund_amount"] = df["refund_amount"].astype(float)
        df["date"] = key_to_date(df["date_key"])
    return df


def refresh_rollups(s: Session) -> dict[str, int]:
    """Rebuilds both rollup tables from the facts. Cheap at this scale and never drifts."""
    sales = sales_frame(s)
    refunds = refunds_frame(s)

    daily = sales[["event_id", "date", "tickets", "revenue"]] if not sales.empty else pd.DataFrame(columns=["event_id", "date", "tickets", "revenue"])
    if not refunds.empty:
        daily = daily.merge(refunds[["event_id", "date", "refund_amount", "refunded_tickets"]], on=["event_id", "date"], how="outer")
    else:
        daily = daily.assign(refund_amount=0.0, refunded_tickets=0)
    daily = daily.fillna({"tickets": 0, "revenue": 0.0, "refund_amount": 0.0, "refunded_tickets": 0})
    daily = daily.sort_values(["event_id", "date"])
    daily["cumulative_tickets"] = daily.groupby("event_id")["tickets"].cumsum()

    s.execute(delete(DailyEventSales))
    s.execute(delete(DailyPlatformSales))

    s.add_all(DailyEventSales(
        event_id=r.event_id, date=r.date, tickets=int(r.tickets), revenue=Decimal(str(round(r.revenue, 2))),
        refund_amount=Decimal(str(round(r.refund_amount, 2))), refunded_tickets=int(r.refunded_tickets),
        cumulative_tickets=int(r.cumulative_tickets),
    ) for r in daily.itertuples())

    orders = pd.DataFrame(s.execute(select(FactOrder.paid_at).where(FactOrder.paid_at.is_not(None))).all(), columns=["paid_at"])
    order_counts = orders.groupby(pd.to_datetime(orders["paid_at"]).dt.date).size() if not orders.empty else pd.Series(dtype=int)

    platform = daily.groupby("date").agg(tickets=("tickets", "sum"), revenue=("revenue", "sum"), refund_amount=("refund_amount", "sum")) if not daily.empty else pd.DataFrame()
    s.add_all(DailyPlatformSales(
        date=d, tickets=int(r.tickets), revenue=Decimal(str(round(r.revenue, 2))),
        refund_amount=Decimal(str(round(r.refund_amount, 2))), orders=int(order_counts.get(d, 0)),
    ) for d, r in platform.iterrows())

    if not daily.empty:
        ensure_dates(s, min(daily["date"]), max(daily["date"]))
    s.commit()
    return {"daily_event_sales": len(daily), "daily_platform_sales": len(platform)}


def event_daily_series(s: Session, event_id: str, start: date, end: date) -> pd.Series:
    rows = s.execute(select(DailyEventSales.date, DailyEventSales.tickets).where(DailyEventSales.event_id == event_id)).all()
    index = pd.date_range(start, end, freq="D").date
    series = pd.Series(0.0, index=index)
    for d, tickets in rows:
        if start <= d <= end:
            series[d] = float(tickets)
    return series
