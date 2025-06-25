"""Scheduled jobs. Also runnable as `python -m app.jobs refresh|retrain|all` from the Helm CronJob."""

import logging
import sys
from collections import defaultdict
from datetime import timedelta

import pandas as pd
from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from . import forecasting
from .alerts import refresh_alerts
from .db import Database
from .models import (
    DailyPlatformSales, DimEvent, ForecastBacktest, ForecastDaily, ForecastPlatformRevenue, ForecastSummary,
)
from .rollups import event_daily_series, refresh_rollups
from .timeutil import now, today

log = logging.getLogger(__name__)


def size_bucket(capacity: int) -> str:
    return "small" if capacity < 500 else "medium" if capacity < 2000 else "large"


def _completed_series(s: Session, events: list[DimEvent]) -> dict[str, tuple[DimEvent, pd.Series]]:
    out = {}
    current = today()
    for evt in events:
        if evt.status != "published" or evt.starts_at.date() >= current:
            continue
        start, end = evt.published_at.date(), evt.starts_at.date() - timedelta(days=1)
        if end <= start:
            continue
        out[evt.event_id] = (evt, event_daily_series(s, evt.event_id, start, end))
    return out


def retrain(s: Session) -> dict[str, float | int]:
    events = list(s.scalars(select(DimEvent)))
    completed = _completed_series(s, events)

    by_group: dict[tuple[str, str], list[str]] = defaultdict(list)
    for event_id, (evt, _) in completed.items():
        by_group[(evt.category, size_bucket(evt.capacity))].append(event_id)

    def reference_for(evt: DimEvent, exclude: str | None = None) -> forecasting.ReferenceCurve | None:
        ids = [i for i in by_group.get((evt.category, size_bucket(evt.capacity)), []) if i != exclude]
        if len(ids) < 2:
            ids = [i for i, (e, _) in completed.items() if e.category == evt.category and i != exclude]
        return forecasting.build_reference_curve([completed[i][1] for i in ids])

    current = today()
    generated = now()
    s.execute(delete(ForecastDaily))
    s.execute(delete(ForecastSummary))

    upcoming = [e for e in events if e.status == "published" and e.starts_at.date() > current]
    for evt in upcoming:
        start = evt.published_at.date()
        history = event_daily_series(s, evt.event_id, start, current - timedelta(days=1)) if start < current else pd.Series(dtype=float)
        sold_to_date = int(event_daily_series(s, evt.event_id, start, current).sum()) if start <= current else 0
        fc = forecasting.forecast_event(history, sold_to_date, evt.capacity, evt.starts_at.date(), current, reference_for(evt))

        s.add_all(ForecastDaily(event_id=evt.event_id, date=d, predicted=p, lower=lo, upper=up, cumulative=c, cumulative_lower=cl, cumulative_upper=cu)
                  for d, p, lo, up, c, cl, cu in zip(fc.dates, fc.predicted, fc.lower, fc.upper, fc.cumulative, fc.cumulative_lower, fc.cumulative_upper))
        s.add(ForecastSummary(
            event_id=evt.event_id, model=fc.model, sold_to_date=sold_to_date, expected_to_date=fc.expected_to_date,
            projected_final=round(fc.projected_final, 1), projected_sell_through=round(fc.projected_sell_through, 4),
            predicted_sellout_date=fc.sellout_date, pace_status=fc.pace_status, generated_at=generated,
        ))

    for event_id, (evt, series) in completed.items():
        sold = int(series.sum())
        s.add(ForecastSummary(event_id=event_id, model="complete", sold_to_date=sold, expected_to_date=float(sold),
                              projected_final=float(sold), projected_sell_through=round(sold / evt.capacity, 4) if evt.capacity else 0,
                              predicted_sellout_date=None, pace_status="Sold out" if sold >= evt.capacity else "Completed",
                              generated_at=generated))

    mapes = []
    for event_id, (evt, series) in completed.items():
        m = forecasting.backtest_event(series, evt.capacity, evt.starts_at.date(), reference_for(evt, exclude=event_id))
        if m is not None:
            mapes.append(m)
    mape = round(sum(mapes) / len(mapes), 2) if mapes else 0.0
    s.add(ForecastBacktest(model="holt-winters+reference", mape=mape, events=len(mapes), generated_at=generated))

    rows = s.execute(select(DailyPlatformSales.date, DailyPlatformSales.revenue).where(
        DailyPlatformSales.date >= current - timedelta(days=365), DailyPlatformSales.date < current)).all()
    if rows:
        first = min(r[0] for r in rows)
        series = pd.Series(0.0, index=pd.date_range(first, current - timedelta(days=1), freq="D").date)
        for d, revenue in rows:
            series[d] = float(revenue)
    else:
        series = pd.Series(dtype=float)
    revenue = forecasting.forecast_revenue(series, current, 90)
    s.execute(delete(ForecastPlatformRevenue))
    s.add_all(ForecastPlatformRevenue(date=d, predicted=p, lower=lo, upper=up, generated_at=generated)
              for d, p, lo, up in zip(revenue.dates, revenue.predicted, revenue.lower, revenue.upper))

    s.commit()
    log.info("retrained forecasts for %s upcoming events, backtest MAPE %.2f%% over %s events", len(upcoming), mape, len(mapes))
    return {"upcoming": len(upcoming), "backtestMape": mape, "backtestEvents": len(mapes)}


def refresh(db: Database) -> dict:
    with db.session() as s:
        stats = refresh_rollups(s)
        stats["alerts"] = refresh_alerts(s)
    return stats


def retrain_all(db: Database) -> dict:
    with db.session() as s:
        stats = retrain(s)
        stats["alerts"] = refresh_alerts(s)
    return stats


if __name__ == "__main__":
    from .config import get_settings

    logging.basicConfig(level=logging.INFO)
    database = Database(get_settings().database_url)
    job = sys.argv[1] if len(sys.argv) > 1 else "all"
    if job in ("refresh", "all"):
        print(refresh(database))
    if job in ("retrain", "all"):
        print(retrain_all(database))
