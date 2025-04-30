from datetime import UTC, datetime
from decimal import Decimal

from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel
from sqlalchemy import select
from sqlalchemy.orm import Session

from . import service
from .models import PriceChange, PriceLock, PricingEvent, PricingTier


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class TierPrice(CamelModel):
    tier_id: str
    name: str
    price: Decimal
    base_price: Decimal
    min_price: Decimal
    max_price: Decimal
    sold: int
    held: int
    capacity: int


class EventPrices(CamelModel):
    event_id: str
    tiers: list[TierPrice]


class LockSeat(CamelModel):
    seat_id: str
    tier_id: str


class LockRequest(CamelModel):
    hold_id: str
    event_id: str
    held_at: datetime
    seats: list[LockSeat]


class LockedPrice(CamelModel):
    seat_id: str
    tier_id: str
    price: Decimal


class LockResponse(CamelModel):
    hold_id: str
    prices: list[LockedPrice]


class PriceChangeOut(CamelModel):
    tier_id: str
    tier_name: str
    old_price: Decimal
    new_price: Decimal
    sell_through: float
    reason: str
    changed_at: datetime


def get_session(request: Request):
    yield from request.app.state.db.dependency()


router = APIRouter(prefix="/prices", tags=["prices"])


@router.get("/{event_id}", response_model=EventPrices, response_model_by_alias=True)
def current_prices(event_id: str, s: Session = Depends(get_session)):
    tiers = list(s.scalars(select(PricingTier).where(PricingTier.event_id == event_id.lower()).order_by(PricingTier.base_price.desc())))
    if not tiers:
        raise HTTPException(404, "Unknown event")
    return EventPrices(event_id=event_id.lower(), tiers=[
        TierPrice(tier_id=t.tier_id, name=t.name, price=t.current_price, base_price=t.base_price, min_price=t.min_price,
                  max_price=t.max_price, sold=t.sold, held=t.held, capacity=t.capacity) for t in tiers])


@router.post("/locks", response_model=LockResponse, response_model_by_alias=True)
def lock(req: LockRequest, s: Session = Depends(get_session)):
    try:
        locks = service.lock_prices(s, req.hold_id, req.event_id, service.utc(req.held_at), [(x.seat_id, x.tier_id) for x in req.seats])
        s.commit()
    except service.UnknownEventError as exc:
        # 503 so callers' retry policy kicks in while the event-published message is still in flight
        raise HTTPException(503, f"Pricing not initialised yet for {exc}") from exc
    return LockResponse(hold_id=req.hold_id.lower(), prices=[LockedPrice(seat_id=l.seat_id, tier_id=l.tier_id, price=l.price) for l in locks])


@router.get("/locks/{hold_id}", response_model=LockResponse, response_model_by_alias=True)
def get_locks(hold_id: str, s: Session = Depends(get_session)):
    locks = list(s.scalars(select(PriceLock).where(PriceLock.hold_id == hold_id.lower())))
    if not locks:
        raise HTTPException(404, "No locks for hold")
    return LockResponse(hold_id=hold_id.lower(), prices=[LockedPrice(seat_id=l.seat_id, tier_id=l.tier_id, price=l.price) for l in locks])


@router.get("/{event_id}/history", response_model=list[PriceChangeOut], response_model_by_alias=True)
def history(event_id: str, s: Session = Depends(get_session)):
    names = {t.tier_id: t.name for t in s.scalars(select(PricingTier).where(PricingTier.event_id == event_id.lower()))}
    changes = s.scalars(select(PriceChange).where(PriceChange.event_id == event_id.lower()).order_by(PriceChange.changed_at))
    return [PriceChangeOut(tier_id=c.tier_id, tier_name=names.get(c.tier_id, "?"), old_price=c.old_price, new_price=c.new_price,
                           sell_through=c.sell_through, reason=c.reason, changed_at=service.utc(c.changed_at).replace(tzinfo=UTC)) for c in changes]


@router.post("/{event_id}/recompute")
def recompute(event_id: str, request: Request):
    handlers = request.app.state.handlers
    with handlers.db.session() as s:
        if s.get(PricingEvent, event_id.lower()) is None:
            raise HTTPException(404, "Unknown event")
        updates = service.recompute(s, event_id, handlers.min_change_ratio)
        s.commit()
    for u in updates:
        handlers.publisher.publish("pricing.price-updated", u.event_id, u.payload())
    return {"changed": [u.payload() for u in updates]}
