"""initial pricing schema

Revision ID: 0001
Revises:
Create Date: 2025-04-28
"""
from alembic import op
import sqlalchemy as sa

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "pricing_events",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("title", sa.String(200), nullable=False),
        sa.Column("starts_at", sa.DateTime(), nullable=False),
        sa.Column("published_at", sa.DateTime(), nullable=False),
        sa.Column("capacity", sa.Integer, nullable=False),
        sa.Column("cancelled", sa.Boolean, nullable=False, server_default=sa.false()),
    )
    op.create_table(
        "pricing_tiers",
        sa.Column("tier_id", sa.String(36), primary_key=True),
        sa.Column("event_id", sa.String(36), sa.ForeignKey("pricing_events.event_id"), nullable=False),
        sa.Column("name", sa.String(50), nullable=False),
        sa.Column("base_price", sa.Numeric(10, 2), nullable=False),
        sa.Column("min_price", sa.Numeric(10, 2), nullable=False),
        sa.Column("max_price", sa.Numeric(10, 2), nullable=False),
        sa.Column("current_price", sa.Numeric(10, 2), nullable=False),
        sa.Column("capacity", sa.Integer, nullable=False),
        sa.Column("sold", sa.Integer, nullable=False, server_default="0"),
        sa.Column("held", sa.Integer, nullable=False, server_default="0"),
    )
    op.create_index("ix_pricing_tiers_event_id", "pricing_tiers", ["event_id"])
    op.create_table(
        "price_changes",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("event_id", sa.String(36), nullable=False),
        sa.Column("tier_id", sa.String(36), nullable=False),
        sa.Column("old_price", sa.Numeric(10, 2), nullable=False),
        sa.Column("new_price", sa.Numeric(10, 2), nullable=False),
        sa.Column("sell_through", sa.Float, nullable=False),
        sa.Column("reason", sa.String(50), nullable=False),
        sa.Column("changed_at", sa.DateTime(), nullable=False),
    )
    op.create_index("ix_price_changes_event_id", "price_changes", ["event_id"])
    op.create_index("ix_price_changes_tier_time", "price_changes", ["tier_id", "changed_at"])
    op.create_table(
        "price_locks",
        sa.Column("hold_id", sa.String(36), primary_key=True),
        sa.Column("seat_id", sa.String(36), primary_key=True),
        sa.Column("event_id", sa.String(36), nullable=False),
        sa.Column("tier_id", sa.String(36), nullable=False),
        sa.Column("price", sa.Numeric(10, 2), nullable=False),
        sa.Column("locked_at", sa.DateTime(), nullable=False),
        sa.Column("state", sa.String(10), nullable=False, server_default="held"),
    )
    op.create_index("ix_price_locks_event_id", "price_locks", ["event_id"])
    op.create_table(
        "processed_events",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("consumer", sa.String(100), primary_key=True),
        sa.Column("processed_at", sa.DateTime(), nullable=False),
    )


def downgrade() -> None:
    for table in ("processed_events", "price_locks", "price_changes", "pricing_tiers", "pricing_events"):
        op.drop_table(table)
