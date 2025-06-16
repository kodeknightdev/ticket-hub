"""Star schema for the read side. Fact tables are append-mostly and get clustered columnstore
indexes on SQL Server (see the initial migration)."""

from datetime import date, datetime
from decimal import Decimal

from sqlalchemy import Boolean, Date, DateTime, Float, Index, Integer, Numeric, String, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column

from .db import Base

Money = Numeric(12, 2)


class DimEvent(Base):
    __tablename__ = "dim_event"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    organizer_id: Mapped[str] = mapped_column(String(36), index=True)
    title: Mapped[str] = mapped_column(String(200))
    artist: Mapped[str] = mapped_column(String(200))
    category: Mapped[str] = mapped_column(String(50))
    city: Mapped[str] = mapped_column(String(100))
    venue_id: Mapped[str] = mapped_column(String(36))
    venue_name: Mapped[str] = mapped_column(String(200))
    capacity: Mapped[int] = mapped_column(Integer)
    starts_at: Mapped[datetime] = mapped_column(DateTime())
    published_at: Mapped[datetime] = mapped_column(DateTime())
    status: Mapped[str] = mapped_column(String(20), default="published")


class DimDate(Base):
    __tablename__ = "dim_date"

    date_key: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=False)
    date: Mapped[date] = mapped_column(Date)
    year: Mapped[int] = mapped_column(Integer)
    month: Mapped[int] = mapped_column(Integer)
    day: Mapped[int] = mapped_column(Integer)
    weekday: Mapped[int] = mapped_column(Integer)
    is_weekend: Mapped[bool] = mapped_column(Boolean)


class DimVenueSection(Base):
    __tablename__ = "dim_venue_section"

    section_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    venue_id: Mapped[str] = mapped_column(String(36))
    name: Mapped[str] = mapped_column(String(100))
    capacity: Mapped[int] = mapped_column(Integer)


class FactTicketSale(Base):
    __tablename__ = "fact_ticket_sale"
    __table_args__ = (UniqueConstraint("order_id", "seat_id", name="uq_fact_ticket_sale_order_seat"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    order_id: Mapped[str] = mapped_column(String(36))
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    seat_id: Mapped[str] = mapped_column(String(36))
    section_id: Mapped[str] = mapped_column(String(36))
    tier_id: Mapped[str] = mapped_column(String(36))
    buyer_id: Mapped[str] = mapped_column(String(36))
    price: Mapped[Decimal] = mapped_column(Money)
    sold_at: Mapped[datetime] = mapped_column(DateTime())
    date_key: Mapped[int] = mapped_column(Integer, index=True)
    weekday: Mapped[int] = mapped_column(Integer)
    hour: Mapped[int] = mapped_column(Integer)
    lead_days: Mapped[int] = mapped_column(Integer)


class FactRefund(Base):
    __tablename__ = "fact_refund"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    order_id: Mapped[str] = mapped_column(String(36), unique=True)
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    amount: Mapped[Decimal] = mapped_column(Money)
    tickets: Mapped[int] = mapped_column(Integer)
    reason: Mapped[str] = mapped_column(String(50))
    refunded_at: Mapped[datetime] = mapped_column(DateTime())
    date_key: Mapped[int] = mapped_column(Integer)


class FactHold(Base):
    __tablename__ = "fact_hold"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    hold_id: Mapped[str] = mapped_column(String(36), unique=True)
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    buyer_id: Mapped[str] = mapped_column(String(36))
    seats: Mapped[int] = mapped_column(Integer)
    held_at: Mapped[datetime] = mapped_column(DateTime())
    date_key: Mapped[int] = mapped_column(Integer)
    outcome: Mapped[str] = mapped_column(String(20), default="held")
    ended_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)


class FactOrder(Base):
    __tablename__ = "fact_order"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    order_id: Mapped[str] = mapped_column(String(36), unique=True)
    hold_id: Mapped[str] = mapped_column(String(36))
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    total: Mapped[Decimal] = mapped_column(Money)
    status: Mapped[str] = mapped_column(String(20))
    created_at: Mapped[datetime] = mapped_column(DateTime())
    paid_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    tickets_issued_at: Mapped[datetime | None] = mapped_column(DateTime(), nullable=True)
    date_key: Mapped[int] = mapped_column(Integer)


class FactCheckin(Base):
    __tablename__ = "fact_checkin"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    ticket_id: Mapped[str] = mapped_column(String(36), unique=True)
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    seat_id: Mapped[str] = mapped_column(String(36))
    checked_in_at: Mapped[datetime] = mapped_column(DateTime())
    date_key: Mapped[int] = mapped_column(Integer)


class FactPriceChange(Base):
    __tablename__ = "fact_price_change"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    event_id: Mapped[str] = mapped_column(String(36), index=True)
    tier_id: Mapped[str] = mapped_column(String(36))
    old_price: Mapped[Decimal] = mapped_column(Money)
    new_price: Mapped[Decimal] = mapped_column(Money)
    sell_through: Mapped[float] = mapped_column(Float)
    reason: Mapped[str] = mapped_column(String(50))
    changed_at: Mapped[datetime] = mapped_column(DateTime())
    date_key: Mapped[int] = mapped_column(Integer)


class DailyEventSales(Base):
    __tablename__ = "daily_event_sales"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    date: Mapped[date] = mapped_column(Date, primary_key=True)
    tickets: Mapped[int] = mapped_column(Integer)
    revenue: Mapped[Decimal] = mapped_column(Money)
    refund_amount: Mapped[Decimal] = mapped_column(Money)
    refunded_tickets: Mapped[int] = mapped_column(Integer)
    cumulative_tickets: Mapped[int] = mapped_column(Integer)


class DailyPlatformSales(Base):
    __tablename__ = "daily_platform_sales"

    date: Mapped[date] = mapped_column(Date, primary_key=True)
    tickets: Mapped[int] = mapped_column(Integer)
    revenue: Mapped[Decimal] = mapped_column(Money)
    refund_amount: Mapped[Decimal] = mapped_column(Money)
    orders: Mapped[int] = mapped_column(Integer)


class ForecastDaily(Base):
    __tablename__ = "forecasts"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    date: Mapped[date] = mapped_column(Date, primary_key=True)
    predicted: Mapped[float] = mapped_column(Float)
    lower: Mapped[float] = mapped_column(Float)
    upper: Mapped[float] = mapped_column(Float)
    cumulative: Mapped[float] = mapped_column(Float)
    cumulative_lower: Mapped[float] = mapped_column(Float)
    cumulative_upper: Mapped[float] = mapped_column(Float)


class ForecastSummary(Base):
    __tablename__ = "forecast_summary"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    model: Mapped[str] = mapped_column(String(30))
    sold_to_date: Mapped[int] = mapped_column(Integer)
    expected_to_date: Mapped[float] = mapped_column(Float)
    projected_final: Mapped[float] = mapped_column(Float)
    projected_sell_through: Mapped[float] = mapped_column(Float)
    predicted_sellout_date: Mapped[date | None] = mapped_column(Date, nullable=True)
    pace_status: Mapped[str] = mapped_column(String(20))
    generated_at: Mapped[datetime] = mapped_column(DateTime())


class ForecastPlatformRevenue(Base):
    __tablename__ = "forecast_platform_revenue"

    date: Mapped[date] = mapped_column(Date, primary_key=True)
    predicted: Mapped[float] = mapped_column(Float)
    lower: Mapped[float] = mapped_column(Float)
    upper: Mapped[float] = mapped_column(Float)
    generated_at: Mapped[datetime] = mapped_column(DateTime())


class ForecastBacktest(Base):
    __tablename__ = "forecast_backtest"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    model: Mapped[str] = mapped_column(String(30))
    mape: Mapped[float] = mapped_column(Float)
    events: Mapped[int] = mapped_column(Integer)
    generated_at: Mapped[datetime] = mapped_column(DateTime())


class Alert(Base):
    __tablename__ = "alerts"
    __table_args__ = (Index("ix_alerts_active", "active", "created_at"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    rule: Mapped[str] = mapped_column(String(50))
    severity: Mapped[str] = mapped_column(String(10))
    event_id: Mapped[str | None] = mapped_column(String(36), nullable=True)
    message: Mapped[str] = mapped_column(String(400))
    value: Mapped[float] = mapped_column(Float)
    created_at: Mapped[datetime] = mapped_column(DateTime())
    active: Mapped[bool] = mapped_column(Boolean, default=True)


class ProcessedEvent(Base):
    __tablename__ = "processed_events"

    event_id: Mapped[str] = mapped_column(String(36), primary_key=True)
    consumer: Mapped[str] = mapped_column(String(100), primary_key=True)
    processed_at: Mapped[datetime] = mapped_column(DateTime())
