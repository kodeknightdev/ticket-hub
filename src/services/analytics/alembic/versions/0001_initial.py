"""analytics star schema

Revision ID: 0001
Revises:
Create Date: 2025-06-17
"""
from alembic import op
import sqlalchemy as sa

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None

FACT_TABLES = ("fact_ticket_sale", "fact_refund", "fact_hold", "fact_order", "fact_checkin", "fact_price_change")


def _pk(*cols):
    # Fact tables keep a nonclustered PK so the clustered index can be a columnstore on SQL Server.
    return sa.PrimaryKeyConstraint(*cols, mssql_clustered=False)


def upgrade() -> None:
    op.create_table(
        "dim_event",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("organizer_id", sa.String(36), nullable=False, index=True),
        sa.Column("title", sa.String(200), nullable=False),
        sa.Column("artist", sa.String(200), nullable=False),
        sa.Column("category", sa.String(50), nullable=False),
        sa.Column("city", sa.String(100), nullable=False),
        sa.Column("venue_id", sa.String(36), nullable=False),
        sa.Column("venue_name", sa.String(200), nullable=False),
        sa.Column("capacity", sa.Integer, nullable=False),
        sa.Column("starts_at", sa.DateTime(), nullable=False),
        sa.Column("published_at", sa.DateTime(), nullable=False),
        sa.Column("status", sa.String(20), nullable=False),
    )
    op.create_table(
        "dim_date",
        sa.Column("date_key", sa.Integer, primary_key=True, autoincrement=False),
        sa.Column("date", sa.Date, nullable=False),
        sa.Column("year", sa.Integer, nullable=False),
        sa.Column("month", sa.Integer, nullable=False),
        sa.Column("day", sa.Integer, nullable=False),
        sa.Column("weekday", sa.Integer, nullable=False),
        sa.Column("is_weekend", sa.Boolean, nullable=False),
    )
    op.create_table(
        "dim_venue_section",
        sa.Column("section_id", sa.String(36), primary_key=True),
        sa.Column("event_id", sa.String(36), nullable=False, index=True),
        sa.Column("venue_id", sa.String(36), nullable=False),
        sa.Column("name", sa.String(100), nullable=False),
        sa.Column("capacity", sa.Integer, nullable=False),
    )

    op.create_table(
        "fact_ticket_sale",
        sa.Column("id", sa.Integer, autoincrement=True),
        sa.Column("order_id", sa.String(36), nullable=False),
        sa.Column("event_id", sa.String(36), nullable=False, index=True),
        sa.Column("seat_id", sa.String(36), nullable=False),
        sa.Column("section_id", sa.String(36), nullable=False),
        sa.Column("tier_id", sa.String(36), nullable=False),
        sa.Column("buyer_id", sa.String(36), nullable=False),
        sa.Column("price", sa.Numeric(12, 2), nullable=False),
        sa.Column("sold_at", sa.DateTime(), nullable=False),
        sa.Column("date_key", sa.Integer, nullable=False, index=True),
        sa.Column("weekday", sa.Integer, nullable=False),
        sa.Column("hour", sa.Integer, nullable=False),
        sa.Column("lead_days", sa.Integer, nullable=False),
        _pk("id"),
        sa.UniqueConstraint("order_id", "seat_id", name="uq_fact_ticket_sale_order_seat"),
    )
    op.create_table(
        "fact_refund",
        sa.Column("id", sa.Integer, autoincrement=True),
        sa.Column("order_id", sa.String(36), nullable=False, unique=True),
        sa.Column("event_id", sa.String(36), nullable=False, index=True),
        sa.Column("amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("tickets", sa.Integer, nullable=False),
        sa.Column("reason", sa.String(50), nullable=False),
        sa.Column("refunded_at", sa.DateTime(), nullable=False),
        sa.Column("date_key", sa.Integer, nullable=False),
        _pk("id"),
    )
    op.create_table(
        "fact_hold",
        sa.Column("id", sa.Integer, autoincrement=True),
        sa.Column("hold_id", sa.String(36), nullable=False, unique=True),
        sa.Column("event_id", sa.String(36), nullable=False, index=True),
        sa.Column("buyer_id", sa.String(36), nullable=False),
        sa.Column("seats", sa.Integer, nullable=False),
        sa.Column("held_at", sa.DateTime(), nullable=False),
        sa.Column("date_key", sa.Integer, nullable=False),
        sa.Column("outcome", sa.String(20), nullable=False),
        sa.Column("ended_at", sa.DateTime(), nullable=True),
        _pk("id"),
    )
    op.create_table(
        "fact_order",
        sa.Column("id", sa.Integer, autoincrement=True),
        sa.Column("order_id", sa.String(36), nullable=False, unique=True),
        sa.Column("hold_id", sa.String(36), nullable=False),
        sa.Column("event_id", sa.String(36), nullable=False, index=True),
        sa.Column("total", sa.Numeric(12, 2), nullable=False),
        sa.Column("status", sa.String(20), nullable=False),
        sa.Column("created_at", sa.DateTime(), nullable=False),
        sa.Column("paid_at", sa.DateTime(), nullable=True),
        sa.Column("tickets_issued_at", sa.DateTime(), nullable=True),
        sa.Column("date_key", sa.Integer, nullable=False),
        _pk("id"),
    )
    op.create_table(
        "fact_checkin",
        sa.Column("id", sa.Integer, autoincrement=True),
        sa.Column("ticket_id", sa.String(36), nullable=False, unique=True),
        sa.Column("event_id", sa.String(36), nullable=False, index=True),
        sa.Column("seat_id", sa.String(36), nullable=False),
        sa.Column("checked_in_at", sa.DateTime(), nullable=False),
        sa.Column("date_key", sa.Integer, nullable=False),
        _pk("id"),
    )
    op.create_table(
        "fact_price_change",
        sa.Column("id", sa.Integer, autoincrement=True),
        sa.Column("event_id", sa.String(36), nullable=False, index=True),
        sa.Column("tier_id", sa.String(36), nullable=False),
        sa.Column("old_price", sa.Numeric(12, 2), nullable=False),
        sa.Column("new_price", sa.Numeric(12, 2), nullable=False),
        sa.Column("sell_through", sa.Float, nullable=False),
        sa.Column("reason", sa.String(50), nullable=False),
        sa.Column("changed_at", sa.DateTime(), nullable=False),
        sa.Column("date_key", sa.Integer, nullable=False),
        _pk("id"),
    )

    op.create_table(
        "daily_event_sales",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("date", sa.Date, primary_key=True),
        sa.Column("tickets", sa.Integer, nullable=False),
        sa.Column("revenue", sa.Numeric(12, 2), nullable=False),
        sa.Column("refund_amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("refunded_tickets", sa.Integer, nullable=False),
        sa.Column("cumulative_tickets", sa.Integer, nullable=False),
    )
    op.create_table(
        "daily_platform_sales",
        sa.Column("date", sa.Date, primary_key=True),
        sa.Column("tickets", sa.Integer, nullable=False),
        sa.Column("revenue", sa.Numeric(12, 2), nullable=False),
        sa.Column("refund_amount", sa.Numeric(12, 2), nullable=False),
        sa.Column("orders", sa.Integer, nullable=False),
    )
    op.create_table(
        "forecasts",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("date", sa.Date, primary_key=True),
        sa.Column("predicted", sa.Float, nullable=False),
        sa.Column("lower", sa.Float, nullable=False),
        sa.Column("upper", sa.Float, nullable=False),
        sa.Column("cumulative", sa.Float, nullable=False),
        sa.Column("cumulative_lower", sa.Float, nullable=False),
        sa.Column("cumulative_upper", sa.Float, nullable=False),
    )
    op.create_table(
        "forecast_summary",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("model", sa.String(30), nullable=False),
        sa.Column("sold_to_date", sa.Integer, nullable=False),
        sa.Column("expected_to_date", sa.Float, nullable=False),
        sa.Column("projected_final", sa.Float, nullable=False),
        sa.Column("projected_sell_through", sa.Float, nullable=False),
        sa.Column("predicted_sellout_date", sa.Date, nullable=True),
        sa.Column("pace_status", sa.String(20), nullable=False),
        sa.Column("generated_at", sa.DateTime(), nullable=False),
    )
    op.create_table(
        "forecast_platform_revenue",
        sa.Column("date", sa.Date, primary_key=True),
        sa.Column("predicted", sa.Float, nullable=False),
        sa.Column("lower", sa.Float, nullable=False),
        sa.Column("upper", sa.Float, nullable=False),
        sa.Column("generated_at", sa.DateTime(), nullable=False),
    )
    op.create_table(
        "forecast_backtest",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("model", sa.String(30), nullable=False),
        sa.Column("mape", sa.Float, nullable=False),
        sa.Column("events", sa.Integer, nullable=False),
        sa.Column("generated_at", sa.DateTime(), nullable=False),
    )
    op.create_table(
        "alerts",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("rule", sa.String(50), nullable=False),
        sa.Column("severity", sa.String(10), nullable=False),
        sa.Column("event_id", sa.String(36), nullable=True),
        sa.Column("message", sa.String(400), nullable=False),
        sa.Column("value", sa.Float, nullable=False),
        sa.Column("created_at", sa.DateTime(), nullable=False),
        sa.Column("active", sa.Boolean, nullable=False),
    )
    op.create_index("ix_alerts_active", "alerts", ["active", "created_at"])
    op.create_table(
        "processed_events",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("consumer", sa.String(100), primary_key=True),
        sa.Column("processed_at", sa.DateTime(), nullable=False),
    )

    if op.get_bind().dialect.name == "mssql":
        for table in FACT_TABLES:
            op.execute(f"CREATE CLUSTERED COLUMNSTORE INDEX cci_{table} ON {table}")


def downgrade() -> None:
    for table in ("processed_events", "alerts", "forecast_backtest", "forecast_platform_revenue", "forecast_summary", "forecasts",
                  "daily_platform_sales", "daily_event_sales", *FACT_TABLES, "dim_venue_section", "dim_date", "dim_event"):
        op.drop_table(table)
