from datetime import timedelta

from sqlalchemy import select

from app.models import DailyEventSales, DailyPlatformSales
from app.rollups import refresh_rollups
from app.timeutil import now

from .conftest import publish_event, sell


def test_daily_rollups_and_cumulative(ingestor, db):
    event_id, sections = publish_event(ingestor)
    other_id, other_sections = publish_event(ingestor)
    yesterday = now() - timedelta(days=1)
    sell(ingestor, event_id, sections[0]["sectionId"], seats=2, price=50, at=yesterday)
    sell(ingestor, event_id, sections[1]["sectionId"], seats=3, price=30, at=now())
    sell(ingestor, other_id, other_sections[0]["sectionId"], seats=1, price=80, at=now())

    with db.session() as s:
        refresh_rollups(s)
        rows = list(s.scalars(select(DailyEventSales).where(DailyEventSales.event_id == event_id).order_by(DailyEventSales.date)))
        platform = {r.date: r for r in s.scalars(select(DailyPlatformSales))}

    assert [r.tickets for r in rows] == [2, 3]
    assert [r.cumulative_tickets for r in rows] == [2, 5]
    assert float(rows[0].revenue) == 100.0 and float(rows[1].revenue) == 90.0
    assert float(platform[now().date()].revenue) == 170.0
    assert platform[now().date()].tickets == 4


def test_rollup_refresh_is_repeatable(ingestor, db):
    event_id, sections = publish_event(ingestor)
    sell(ingestor, event_id, sections[0]["sectionId"], seats=2)
    with db.session() as s:
        refresh_rollups(s)
        refresh_rollups(s)
        assert len(list(s.scalars(select(DailyEventSales)))) == 1

