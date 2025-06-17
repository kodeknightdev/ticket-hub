import uuid
from datetime import timedelta

import pytest
from fastapi.testclient import TestClient

from app.config import Settings
from app.db import Base, Database
from app.ingest import Ingestor
from app.main import create_app
from app.timeutil import now


@pytest.fixture
def db():
    database = Database("sqlite:///:memory:")
    Base.metadata.create_all(database.engine)
    return database


@pytest.fixture
def ingestor(db):
    return Ingestor(db)


@pytest.fixture
def client():
    settings = Settings(database_url="sqlite:///:memory:", kafka_enabled=False, scheduler_enabled=False)
    with TestClient(create_app(settings)) as c:
        yield c


def env(topic: str, data: dict) -> dict:
    return {"id": str(uuid.uuid4()), "type": topic, "version": 1, "data": data}


def iso(dt) -> str:
    return dt.isoformat() + "Z"


def publish_event(ingestor, organizer_id=None, days_ahead=30, published_days_ago=20, capacity=100, category="Concert", title="Test Gig"):
    event_id = str(uuid.uuid4())
    sections = [{"sectionId": str(uuid.uuid4()), "name": "Floor", "capacity": capacity // 2},
                {"sectionId": str(uuid.uuid4()), "name": "Balcony", "capacity": capacity - capacity // 2}]
    ingestor.handle(env("events.event-published", {
        "eventId": event_id, "organizerId": organizer_id or str(uuid.uuid4()), "title": title, "artist": "The Testers",
        "category": category, "city": "Leeds", "venueId": str(uuid.uuid4()), "venueName": "Test Hall",
        "startsAt": iso(now() + timedelta(days=days_ahead)), "publishedAt": iso(now() - timedelta(days=published_days_ago)),
        "capacity": capacity, "sections": sections, "tiers": [],
    }))
    return event_id, sections


def sell(ingestor, event_id, section_id, seats=2, price=50.0, at=None):
    at = at or now()
    hold_id, order_id = str(uuid.uuid4()), str(uuid.uuid4())
    buyer = str(uuid.uuid4())
    seat_ids = [str(uuid.uuid4()) for _ in range(seats)]
    tier = str(uuid.uuid4())
    ingestor.handle(env("seating.seat-held", {"holdId": hold_id, "eventId": event_id, "buyerId": buyer, "heldAt": iso(at),
                                              "expiresAt": iso(at + timedelta(minutes=10)),
                                              "seats": [{"seatId": s, "tierId": tier, "sectionId": section_id, "label": "x"} for s in seat_ids]}))
    ingestor.handle(env("orders.order-created", {"orderId": order_id, "holdId": hold_id, "eventId": event_id, "buyerId": buyer,
                                                 "total": price * seats, "currency": "GBP", "createdAt": iso(at)}))
    ingestor.handle(env("orders.order-paid", {
        "orderId": order_id, "holdId": hold_id, "eventId": event_id, "buyerId": buyer, "subtotal": price * seats, "fees": 0,
        "total": price * seats, "currency": "GBP", "paidAt": iso(at),
        "lines": [{"seatId": s, "tierId": tier, "sectionId": section_id, "label": "x", "price": price} for s in seat_ids]}))
    ingestor.handle(env("seating.seats-sold", {"eventId": event_id, "orderId": order_id, "holdId": hold_id, "seatIds": seat_ids, "soldTotal": seats}))
    return order_id, hold_id, seat_ids
