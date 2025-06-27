from datetime import date, timedelta
from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from sqlalchemy.orm import Session

from . import jobs, queries
from .timeutil import today


def get_session(request: Request):
    yield from request.app.state.db.dependency()


def get_filters(
    from_: Annotated[date | None, Query(alias="from")] = None,
    to: date | None = None,
    event_ids: Annotated[str | None, Query(alias="eventIds")] = None,
    city: str | None = None,
    category: str | None = None,
    venue: str | None = None,
    organizer_id: Annotated[str | None, Query(alias="organizerId")] = None,
) -> queries.Filters:
    end = to or today()
    start = from_ or end - timedelta(days=29)
    if start > end:
        raise HTTPException(400, "'from' must not be after 'to'")
    ids = None if event_ids is None else [e for e in event_ids.split(",") if e]
    return queries.Filters(start, end, ids, city or None, category or None, venue or None, organizer_id or None)


Db = Annotated[Session, Depends(get_session)]
F = Annotated[queries.Filters, Depends(get_filters)]

router = APIRouter(prefix="/analytics", tags=["analytics"])


@router.get("/kpis")
def kpis(s: Db, f: F):
    return queries.kpis(s, f)


@router.get("/sales/timeseries")
def sales_timeseries(s: Db, f: F, granularity: str = "day"):
    if granularity not in ("day", "week", "month"):
        raise HTTPException(400, "granularity must be day, week or month")
    return queries.timeseries(s, f, granularity)


@router.get("/breakdowns")
def breakdowns(s: Db, f: F, by: str = "event", limit: int = 10):
    try:
        return queries.breakdown(s, f, by, min(max(limit, 1), 50))
    except ValueError as exc:
        raise HTTPException(400, str(exc)) from exc


@router.get("/sections/heatmap")
def section_heatmap(s: Db, f: F):
    return queries.section_heatmap(s, f)


@router.get("/events/performance")
def performance(s: Db, f: F):
    return queries.performance(s, f)


@router.get("/events/ids")
def event_ids(s: Db, organizer_id: Annotated[str, Query(alias="organizerId")]):
    return queries.event_ids_for_organizer(s, organizer_id)


@router.get("/events/{event_id}/insights")
def insights(event_id: str, s: Db):
    result = queries.event_insights(s, event_id)
    if result is None:
        raise HTTPException(404, "Unknown event")
    return result


@router.get("/forecast/revenue")
def revenue_forecast(s: Db, f: F, horizon: int = 30):
    if horizon not in (30, 60, 90):
        raise HTTPException(400, "horizon must be 30, 60 or 90")
    return queries.revenue_forecast(s, f, horizon)


@router.get("/forecast/accuracy")
def accuracy(s: Db):
    return queries.latest_backtest(s) or {"model": None, "mape": None, "events": 0}


@router.get("/demand/heatmap")
def demand_heatmap(s: Db, f: F):
    return queries.demand_heatmap(s, f)


@router.get("/demand/lead-time")
def lead_time(s: Db, f: F):
    return queries.lead_time(s, f)


@router.get("/funnel")
def funnel(s: Db, f: F):
    return queries.funnel(s, f)


@router.get("/pricing-impact")
def pricing_impact(s: Db, f: F):
    return queries.pricing_impact(s, f)


@router.get("/alerts")
def alerts(s: Db, f: F):
    return queries.active_alerts(s, f)


@router.get("/filters")
def filters(s: Db, f: F):
    return queries.filter_options(s, f.event_ids)


@router.post("/jobs/refresh")
def run_refresh(request: Request):
    return jobs.refresh(request.app.state.db)


@router.post("/jobs/retrain")
def run_retrain(request: Request):
    return jobs.retrain_all(request.app.state.db)
