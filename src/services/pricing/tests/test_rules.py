from decimal import Decimal

from app import rules


def demand(**overrides):
    base = dict(base_price=Decimal("50"), min_price=Decimal("35"), max_price=Decimal("90"),
                capacity=100, sold=0, held=0, days_on_sale=10, days_to_event=30)
    base.update(overrides)
    return rules.TierDemand(**base)


def test_on_track_keeps_base_price():
    expected = rules.expected_sell_through(10, 30)
    sold = round(expected * 100)
    decision = rules.decide(demand(sold=sold))
    assert abs(decision.price - Decimal("50")) <= Decimal("1.00")
    assert decision.reason == "on-track"


def test_hot_sales_raise_price_but_respect_max():
    decision = rules.decide(demand(sold=95, days_on_sale=1, days_to_event=60))
    assert decision.price > Decimal("50")
    assert decision.price <= Decimal("90")
    assert decision.reason in ("demand-high", "last-minute")


def test_slow_sales_lower_price_but_respect_min():
    decision = rules.decide(demand(sold=0, days_on_sale=50, days_to_event=5))
    assert decision.price < Decimal("50")
    assert decision.price >= Decimal("35")


def test_held_seats_count_half():
    assert rules.sell_through(sold=10, held=10, capacity=100) == 0.15


def test_prices_round_to_fifty_pence():
    decision = rules.decide(demand(sold=40))
    assert (decision.price * 2) % 1 == 0


def test_material_change_threshold():
    assert not rules.is_material_change(Decimal("50"), Decimal("50.50"), 0.02)
    assert rules.is_material_change(Decimal("50"), Decimal("52"), 0.02)
