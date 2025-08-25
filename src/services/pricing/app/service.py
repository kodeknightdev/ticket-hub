from dataclasses import dataclass
from datetime import UTC, datetime
from decimal import Decimal
from typing import Any

from sqlalchemy import delete, false, select
from sqlalchemy.orm import Session

from . import rules
from .models import PriceChange, PriceLock, PricingEvent, PricingTier


def utc(dt: datetime) -> datetime:
    """Naive UTC everywhere: SQL Server DATETIME2 via pyodbc does not round-trip offsets."""
    return dt if dt.tzinfo is None else dt.astimezone(UTC).replace(tzinfo=None)


def parse_dt(value: str) -> datetime:
    return utc(datetime.fromisoformat(value.replace("Z", "+00:00")))


def now() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)


def iso(dt: datetime) -> str:
    return utc(dt).isoformat() + "Z"


@dataclass
class PriceUpdate:
    event_id: str
    tier_id: str
    old_price: Decimal
    new_price: Decimal
    sell_through: float
    reason: str
    changed_at: datetime

    def payload(self) -> dict[str, Any]:
        return {
            "eventId": self.event_id,
            "tierId": self.tier_id,
            "oldPrice": float(self.old_price),
            "newPrice": float(self.new_price),
            "sellThrough": round(self.sell_through, 4),
            "reason": self.reason,
            "changedAt": iso(self.changed_at),
        }


class UnknownEventError(Exception):
    pass


def register_event(db: Session, data: dict[str, Any]) -> None:
    event_id = data["eventId"].lower()
    if db.get(PricingEvent, event_id) is not None:
        return
    db.add(PricingEvent(
        event_id=event_id,
        title=data["title"],
        starts_at=parse_dt(data["startsAt"]),
        published_at=parse_dt(data["publishedAt"]) if data.get("publishedAt") else now(),
        capacity=int(data["capacity"]),
        cancelled=False,
    ))
    for tier in data.get("tiers", []):
        base = Decimal(str(tier["basePrice"]))
        db.add(PricingTier(
            tier_id=tier["tierId"].lower(),
            event_id=event_id,
            name=tier["name"],
            base_price=base,
            min_price=Decimal(str(tier["minPrice"])),
            max_price=Decimal(str(tier["maxPrice"])),
            current_price=base,
            capacity=int(tier.get("capacity", 0)),
            sold=0,
            held=0,
        ))


def cancel_event(db: Session, event_id: str) -> None:
    event = db.get(PricingEvent, event_id.lower())
    if event is not None:
        event.cancelled = True


def price_at(db: Session, tier: PricingTier, at: datetime) -> Decimal:
    """Price that was in effect at a moment in time, so a hold keeps the price it saw."""
    change = db.scalars(
        select(PriceChange)
        .where(PriceChange.tier_id == tier.tier_id, PriceChange.changed_at <= at)
        .order_by(PriceChange.changed_at.desc())
        .limit(1)
    ).first()
    return Decimal(change.new_price) if change is not None else Decimal(tier.base_price)


def lock_prices(db: Session, hold_id: str, event_id: str, held_at: datetime, seats: list[tuple[str, str]]) -> list[PriceLock]:
    hold_id, event_id = hold_id.lower(), event_id.lower()
    existing = {l.seat_id: l for l in db.scalars(select(PriceLock).where(PriceLock.hold_id == hold_id))}
    if existing and all(seat_id.lower() in existing for seat_id, _ in seats):
        return [existing[s.lower()] for s, _ in seats]

    tiers = {t.tier_id: t for t in db.scalars(select(PricingTier).where(PricingTier.event_id == event_id))}
    if not tiers:
        raise UnknownEventError(event_id)

    locks: list[PriceLock] = []
    new_per_tier: dict[str, int] = {}
    for seat_id, tier_id in seats:
        seat_id, tier_id = seat_id.lower(), tier_id.lower()
        if seat_id in existing:
            locks.append(existing[seat_id])
            continue
        tier = tiers.get(tier_id)
        if tier is None:
            raise UnknownEventError(f"tier {tier_id}")
        lock = PriceLock(hold_id=hold_id, seat_id=seat_id, event_id=event_id, tier_id=tier_id,
                         price=price_at(db, tier, utc(held_at)), locked_at=now(), state="held")
        db.add(lock)
        locks.append(lock)
        new_per_tier[tier_id] = new_per_tier.get(tier_id, 0) + 1

    for tier_id, count in new_per_tier.items():
        tiers[tier_id].held += count
    return locks


def release_hold(db: Session, hold_id: str) -> None:
    locks = list(db.scalars(select(PriceLock).where(PriceLock.hold_id == hold_id.lower(), PriceLock.state == "held")))
    for lock in locks:
        tier = db.get(PricingTier, lock.tier_id)
        if tier is not None:
            tier.held = max(0, tier.held - 1)
    db.execute(delete(PriceLock).where(PriceLock.hold_id == hold_id.lower(), PriceLock.state == "held"))


def mark_sold(db: Session, hold_id: str, seat_ids: list[str]) -> str | None:
    locks = {l.seat_id: l for l in db.scalars(select(PriceLock).where(PriceLock.hold_id == hold_id.lower()))}
    event_id = None
    for seat_id in (s.lower() for s in seat_ids):
        lock = locks.get(seat_id)
        if lock is None or lock.state == "sold":
            continue
        tier = db.get(PricingTier, lock.tier_id)
        if tier is not None:
            tier.held = max(0, tier.held - 1)
            tier.sold += 1
        lock.state = "sold"
        event_id = lock.event_id
    return event_id


def recompute(db: Session, event_id: str, min_change_ratio: float, at: datetime | None = None) -> list[PriceUpdate]:
    at = at or now()
    event = db.get(PricingEvent, event_id.lower())
    if event is None or event.cancelled or utc(event.starts_at) <= at:
        return []

    updates: list[PriceUpdate] = []
    days_on_sale = max(0.0, (at - utc(event.published_at)).total_seconds() / 86400)
    days_to_event = max(0.0, (utc(event.starts_at) - at).total_seconds() / 86400)

    for tier in db.scalars(select(PricingTier).where(PricingTier.event_id == event.event_id)):
        decision = rules.decide(rules.TierDemand(
            base_price=Decimal(tier.base_price), min_price=Decimal(tier.min_price), max_price=Decimal(tier.max_price),
            capacity=tier.capacity, sold=tier.sold, held=tier.held,
            days_on_sale=days_on_sale, days_to_event=days_to_event,
        ))
        old = Decimal(tier.current_price)
        if not rules.is_material_change(old, decision.price, min_change_ratio):
            continue
        tier.current_price = decision.price
        db.add(PriceChange(event_id=event.event_id, tier_id=tier.tier_id, old_price=old, new_price=decision.price,
                           sell_through=decision.sell_through, reason=decision.reason, changed_at=at))
        updates.append(PriceUpdate(event.event_id, tier.tier_id, old, decision.price, decision.sell_through, decision.reason, at))
    return updates


def active_event_ids(db: Session) -> list[str]:
    return list(db.scalars(select(PricingEvent.event_id).where(PricingEvent.cancelled == false(), PricingEvent.starts_at > now())))
