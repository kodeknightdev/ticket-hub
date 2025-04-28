from datetime import datetime
from decimal import Decimal

from sqlalchemy import Boolean, DateTime, ForeignKey, Index, Integer, Numeric, String, Float
from sqlalchemy.orm import Mapped, mapped_column

from .db import Base

Money = Numeric(10, 2)


class PricingEvent(Base):
    __tablename__ = "pricing_events"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    title: Mapped[str] = mapped_column(String(200))
    starts_at: Mapped[datetime] = mapped_column(DateTime())
    published_at: Mapped[datetime] = mapped_column(DateTime())
    capacity: Mapped[int] = mapped_column(Integer)
    cancelled: Mapped[bool] = mapped_column(Boolean, default=False)


class PricingTier(Base):
    __tablename__ = "pricing_tiers"

    tier_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    event_id: Mapped[str] = mapped_column(String(36), ForeignKey("pricing_events.event_id"), index=True)
    name: Mapped[str] = mapped_column(String(50))
    base_price: Mapped[Decimal] = mapped_column(Money)
    min_price: Mapped[Decimal] = mapped_column(Money)
    max_price: Mapped[Decimal] = mapped_column(Money)
    current_price: Mapped[Decimal] = mapped_column(Money)
    capacity: Mapped[int] = mapped_column(Integer)
    sold: Mapped[int] = mapped_column(Integer, default=0)
    held: Mapped[int] = mapped_column(Integer, default=0)


class PriceChange(Base):
    __tablename__ = "price_changes"
    __table_args__ = (Index("ix_price_changes_tier_time", "tier_id", "changed_at"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    tier_id: Mapped[str] = mapped_column(String(36))
    old_price: Mapped[Decimal] = mapped_column(Money)
    new_price: Mapped[Decimal] = mapped_column(Money)
    sell_through: Mapped[float] = mapped_column(Float)
    reason: Mapped[str] = mapped_column(String(50))
    changed_at: Mapped[datetime] = mapped_column(DateTime())


class PriceLock(Base):
    __tablename__ = "price_locks"

    hold_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    seat_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    tier_id: Mapped[str] = mapped_column(String(36))
    price: Mapped[Decimal] = mapped_column(Money)
    locked_at: Mapped[datetime] = mapped_column(DateTime())
    state: Mapped[str] = mapped_column(String(10), default="held")


class ProcessedEvent(Base):
    __tablename__ = "processed_events"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    consumer: Mapped[str] = mapped_column(String(100), primary_key=True)
    processed_at: Mapped[datetime] = mapped_column(DateTime())
