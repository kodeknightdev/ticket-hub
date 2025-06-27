from datetime import timedelta

from app import jobs
from app.timeutil import now

from .conftest import publish_event, sell


def seed(client):
    ingestor = client.app.state.ingestor
    mine, mine_sections = publish_event(ingestor, organizer_id="11111111-1111-4111-8111-000000000002", title="Mine")
    theirs, theirs_sections = publish_event(ingestor, title="Theirs")
    for days in range(10):
        sell(ingestor, mine, mine_sections[0]["sectionId"], seats=2, price=50, at=now() - timedelta(days=days))
        sell(ingestor, theirs, theirs_sections[0]["sectionId"], seats=1, price=90, at=now() - timedelta(days=days))
    jobs.refresh(client.app.state.db)
    jobs.retrain_all(client.app.state.db)
    return mine, theirs


def test_kpis_include_previous_period_and_trend(client):
    seed(client)
    body = client.get("/analytics/kpis").json()
    cards = {k["key"]: k for k in body["kpis"]}
    assert cards["ticketsSold"]["value"] == 30
    assert cards["grossRevenue"]["value"] == 1900.0
    assert "previous" in cards["netRevenue"]
    assert len(body["trend"]["dates"]) == 30


def test_event_scope_limits_every_query(client):
    mine, theirs = seed(client)
    perf = client.get("/analytics/events/performance", params={"eventIds": mine}).json()
    assert [r["eventId"] for r in perf["rows"]] == [mine]
    kpis = {k["key"]: k["value"] for k in client.get("/analytics/kpis", params={"eventIds": mine}).json()["kpis"]}
    assert kpis["ticketsSold"] == 20
    breakdown = client.get("/analytics/breakdowns", params={"by": "event", "eventIds": mine}).json()
    assert [r["key"] for r in breakdown["rows"]] == [mine]


def test_empty_scope_returns_nothing(client):
    seed(client)
    perf = client.get("/analytics/events/performance", params={"eventIds": ""}).json()
    assert perf["rows"] == []


def test_organizer_ids_lookup(client):
    mine, _ = seed(client)
    assert client.get("/analytics/events/ids", params={"organizerId": "11111111-1111-4111-8111-000000000002"}).json() == [mine]


def test_insights_forecast_and_dashboard_endpoints(client):
    mine, _ = seed(client)
    insights = client.get(f"/analytics/events/{mine}/insights").json()
    assert insights["summary"]["model"] in ("holt-winters", "similar-events", "generic-curve")
    assert len(insights["forecast"]) > 0 and len(insights["actual"]) > 0
    assert client.get("/analytics/forecast/revenue", params={"horizon": 60}).json()["horizon"] == 60
    assert len(client.get("/analytics/demand/heatmap").json()["matrix"]) == 7
    assert client.get("/analytics/funnel").json()["stages"][2]["value"] == 30
    assert client.get("/analytics/sales/timeseries", params={"granularity": "week"}).status_code == 200
    assert client.get("/analytics/forecast/revenue", params={"horizon": 45}).status_code == 400
