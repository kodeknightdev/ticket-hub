import uuid
from datetime import UTC, datetime, timedelta

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.db import Base, Database
from app.handlers import PricingHandlers
from app.main import create_app
from app.messaging import EventPublisher


@pytest.fixture
def db():
    database = Database("sqlite:///:memory:")
    Base.metadata.create_all(database.engine)
    return database


@pytest.fixture
def handlers(db):
    return PricingHandlers(db, EventPublisher(None), min_change_ratio=0.02)


@pytest.fixture
def client():
    settings = Settings(database_url="sqlite:///:memory:", kafka_enabled=False, recompute_interval_seconds=3600)
    with TestClient(create_app(settings)) as c:
        yield c


def published_event(capacity_per_tier: int = 100, days_ahead: int = 30, days_on_sale: int = 10) -> dict:
    now = datetime.now(UTC)
    return {
        "id": str(uuid.uuid4()),
        "type": "events.event-published",
        "data": {
            "eventId": str(uuid.uuid4()),
            "title": "Test Gig",
            "startsAt": (now + timedelta(days=days_ahead)).isoformat(),
            "publishedAt": (now - timedelta(days=days_on_sale)).isoformat(),
            "capacity": capacity_per_tier * 2,
            "tiers": [
                {"tierId": str(uuid.uuid4()), "name": "Front", "basePrice": 80, "minPrice": 60, "maxPrice": 140, "capacity": capacity_per_tier},
                {"tierId": str(uuid.uuid4()), "name": "Standard", "basePrice": 50, "minPrice": 35, "maxPrice": 90, "capacity": capacity_per_tier},
            ],
        },
    }


def envelope(topic: str, data: dict) -> dict:
    return {"id": str(uuid.uuid4()), "type": topic, "data": data}
