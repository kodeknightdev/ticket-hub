"""Turns integration events into fact and dimension rows. Every handler is idempotent on the
envelope id, and most also on a natural key (hold id, order id, ticket id)."""

import logging
from collections.abc import Callable
from datetime import date, timedelta
from decimal import Decimal
from typing import Any

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from .db import Database
from .models import (
    DimDate, DimEvent, DimVenueSection, FactCheckin, FactHold, FactOrder, FactPriceChange,
    FactRefund, FactTicketSale, ProcessedEvent,
)
from .timeutil import date_key, now, parse_dt

log = logging.getLogger(__name__)
CONSUMER = "analytics"


def ensure_dates(s: Session, start: date, end: date) -> None:
    existing = set(s.scalars(select(DimDate.date_key).where(DimDate.date_key.between(date_key(start), date_key(end)))))
    d = start
    while d <= end:
        if date_key(d) not in existing:
            s.add(DimDate(date_key=date_key(d), date=d, year=d.year, month=d.month, day=d.day,
                          weekday=d.weekday(), is_weekend=d.weekday() >= 5))
        d += timedelta(days=1)


def on_event_published(s: Session, d: dict[str, Any]) -> None:
    event_id = d["eventId"].lower()
    existing = s.get(DimEvent, event_id)
    if existing is None:
        s.add(DimEvent(
            event_id=event_id, organizer_id=d["organizerId"].lower(), title=d["title"], artist=d["artist"],
            category=d["category"], city=d["city"], venue_id=d["venueId"].lower(), venue_name=d["venueName"],
            capacity=int(d["capacity"]), starts_at=parse_dt(d["startsAt"]), published_at=parse_dt(d["publishedAt"]),
            status="published",
        ))
    for section in d.get("sections", []):
        if s.get(DimVenueSection, section["sectionId"].lower()) is None:
            s.add(DimVenueSection(section_id=section["sectionId"].lower(), event_id=event_id, venue_id=d["venueId"].lower(),
                                  name=section["name"], capacity=int(section["capacity"])))


def on_event_updated(s: Session, d: dict[str, Any]) -> None:
    evt = s.get(DimEvent, d["eventId"].lower())
    if evt is None:
        return
    evt.title, evt.artist, evt.category, evt.city = d["title"], d["artist"], d["category"], d["city"]
    evt.starts_at = parse_dt(d["startsAt"])


def on_event_cancelled(s: Session, d: dict[str, Any]) -> None:
    evt = s.get(DimEvent, d["eventId"].lower())
    if evt is not None:
        evt.status = "cancelled"


def on_seat_held(s: Session, d: dict[str, Any]) -> None:
    hold_id = d["holdId"].lower()
    if s.scalars(select(FactHold).where(FactHold.hold_id == hold_id)).first() is not None:
        return
    held_at = parse_dt(d["heldAt"])
    s.add(FactHold(hold_id=hold_id, event_id=d["eventId"].lower(), buyer_id=d["buyerId"].lower(), seats=len(d["seats"]),
                   held_at=held_at, date_key=date_key(held_at), outcome="held"))


def _end_hold(s: Session, hold_id: str, outcome: str) -> None:
    hold = s.scalars(select(FactHold).where(FactHold.hold_id == hold_id.lower())).first()
    # A converted hold must never be downgraded by a late release message.
    if hold is not None and hold.outcome == "held":
        hold.outcome = outcome
        hold.ended_at = now()


def on_seat_released(s: Session, d: dict[str, Any]) -> None:
    _end_hold(s, d["holdId"], "released")


def on_hold_expired(s: Session, d: dict[str, Any]) -> None:
    _end_hold(s, d["holdId"], "expired")


def on_seats_sold(s: Session, d: dict[str, Any]) -> None:
    hold = s.scalars(select(FactHold).where(FactHold.hold_id == d["holdId"].lower())).first()
    if hold is not None:
        hold.outcome = "converted"
        hold.ended_at = now()


def on_order_created(s: Session, d: dict[str, Any]) -> None:
    order_id = d["orderId"].lower()
    if s.scalars(select(FactOrder).where(FactOrder.order_id == order_id)).first() is not None:
        return
    created = parse_dt(d["createdAt"])
    s.add(FactOrder(order_id=order_id, hold_id=d["holdId"].lower(), event_id=d["eventId"].lower(),
                    total=Decimal(str(d["total"])), status="created", created_at=created, date_key=date_key(created)))


def on_order_paid(s: Session, d: dict[str, Any]) -> None:
    order_id = d["orderId"].lower()
    paid_at = parse_dt(d["paidAt"])
    order = s.scalars(select(FactOrder).where(FactOrder.order_id == order_id)).first()
    if order is None:
        order = FactOrder(order_id=order_id, hold_id=d["holdId"].lower(), event_id=d["eventId"].lower(),
                          total=Decimal(str(d["total"])), status="paid", created_at=paid_at, date_key=date_key(paid_at))
        s.add(order)
    order.status, order.paid_at = "paid", paid_at

    if s.scalars(select(FactTicketSale.id).where(FactTicketSale.order_id == order_id)).first() is not None:
        return
    evt = s.get(DimEvent, d["eventId"].lower())
    lead = (evt.starts_at.date() - paid_at.date()).days if evt is not None else 0
    for line in d["lines"]:
        s.add(FactTicketSale(
            order_id=order_id, event_id=d["eventId"].lower(), seat_id=line["seatId"].lower(),
            section_id=line["sectionId"].lower(), tier_id=line["tierId"].lower(), buyer_id=d["buyerId"].lower(),
            price=Decimal(str(line["price"])), sold_at=paid_at, date_key=date_key(paid_at),
            weekday=paid_at.weekday(), hour=paid_at.hour, lead_days=max(0, lead),
        ))


def on_order_expired(s: Session, d: dict[str, Any]) -> None:
    order = s.scalars(select(FactOrder).where(FactOrder.order_id == d["orderId"].lower())).first()
    if order is not None and order.status == "created":
        order.status = "expired"


def on_order_refunded(s: Session, d: dict[str, Any]) -> None:
    order_id = d["orderId"].lower()
    order = s.scalars(select(FactOrder).where(FactOrder.order_id == order_id)).first()
    if order is not None:
        order.status = "refunded"
    if s.scalars(select(FactRefund).where(FactRefund.order_id == order_id)).first() is not None:
        return
    refunded_at = parse_dt(d["refundedAt"])
    s.add(FactRefund(order_id=order_id, event_id=d["eventId"].lower(), amount=Decimal(str(d["amount"])),
                     tickets=int(d.get("ticketCount", 0)), reason=d["reason"], refunded_at=refunded_at,
                     date_key=date_key(refunded_at)))


def on_tickets_issued(s: Session, d: dict[str, Any]) -> None:
    order = s.scalars(select(FactOrder).where(FactOrder.order_id == d["orderId"].lower())).first()
    if order is not None:
        order.tickets_issued_at = parse_dt(d["issuedAt"])


def on_ticket_checked_in(s: Session, d: dict[str, Any]) -> None:
    ticket_id = d["ticketId"].lower()
    if s.scalars(select(FactCheckin).where(FactCheckin.ticket_id == ticket_id)).first() is not None:
        return
    at = parse_dt(d["checkedInAt"])
    s.add(FactCheckin(ticket_id=ticket_id, event_id=d["eventId"].lower(), seat_id=d["seatId"].lower(),
                      checked_in_at=at, date_key=date_key(at)))


def on_price_updated(s: Session, d: dict[str, Any]) -> None:
    at = parse_dt(d["changedAt"])
    s.add(FactPriceChange(event_id=d["eventId"].lower(), tier_id=d["tierId"].lower(), old_price=Decimal(str(d["oldPrice"])),
                          new_price=Decimal(str(d["newPrice"])), sell_through=float(d["sellThrough"]),
                          reason=d["reason"], changed_at=at, date_key=date_key(at)))


HANDLERS: dict[str, Callable[[Session, dict[str, Any]], None]] = {
    "events.event-published": on_event_published,
    "events.event-updated": on_event_updated,
    "events.event-cancelled": on_event_cancelled,
    "seating.seat-held": on_seat_held,
    "seating.seat-released": on_seat_released,
    "seating.hold-expired": on_hold_expired,
    "seating.seats-sold": on_seats_sold,
    "orders.order-created": on_order_created,
    "orders.order-paid": on_order_paid,
    "orders.order-expired": on_order_expired,
    "orders.order-refunded": on_order_refunded,
    "orders.tickets-issued": on_tickets_issued,
    "orders.ticket-checked-in": on_ticket_checked_in,
    "pricing.price-updated": on_price_updated,
}


class Ingestor:
    def __init__(self, db: Database):
        self.db = db

    def handle(self, env: dict[str, Any]) -> bool:
        handler = HANDLERS.get(env.get("type", ""))
        if handler is None:
            return False
        with self.db.session() as s:
            if s.get(ProcessedEvent, (env["id"], CONSUMER)) is not None:
                return False
            handler(s, env["data"])
            s.add(ProcessedEvent(event_id=env["id"], consumer=CONSUMER, processed_at=now()))
            try:
                s.commit()
            except IntegrityError:
                # competing consumer on another replica committed the same message first
                s.rollback()
                return False
        return True

    def consumer_handlers(self) -> dict[str, Callable[[dict[str, Any]], Any]]:
        return {topic: self.handle for topic in HANDLERS}
