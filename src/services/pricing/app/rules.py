"""Demand-based pricing rule. Pure functions so they are easy to test and reason about."""

from dataclasses import dataclass
from decimal import ROUND_HALF_UP, Decimal


@dataclass(frozen=True)
class TierDemand:
    base_price: Decimal
    min_price: Decimal
    max_price: Decimal
    capacity: int
    sold: int
    held: int
    days_on_sale: float
    days_to_event: float


@dataclass(frozen=True)
class PriceDecision:
    price: Decimal
    sell_through: float
    expected_sell_through: float
    reason: str


def sell_through(sold: int, held: int, capacity: int) -> float:
    if capacity <= 0:
        return 0.0
    # Held seats count half: they signal demand but a lot of them never convert.
    return min(1.0, (sold + 0.5 * held) / capacity)


def expected_sell_through(days_on_sale: float, days_to_event: float) -> float:
    window = days_on_sale + days_to_event
    if window <= 0:
        return 1.0
    elapsed = max(0.0, min(1.0, days_on_sale / window))
    # Sales are front-loaded around the on-sale date, so the target curve is concave.
    return elapsed ** 0.6


def round_to_half(value: Decimal) -> Decimal:
    return (value * 2).quantize(Decimal("1"), rounding=ROUND_HALF_UP) / 2


def decide(d: TierDemand) -> PriceDecision:
    actual = sell_through(d.sold, d.held, d.capacity)
    expected = expected_sell_through(d.days_on_sale, d.days_to_event)
    pressure = actual - expected

    multiplier = 1.0 + 0.8 * pressure
    reason = "demand-high" if pressure > 0.05 else "demand-low" if pressure < -0.05 else "on-track"

    if d.days_to_event <= 7 and actual >= 0.75:
        multiplier += 0.10
        reason = "last-minute"
    elif d.days_to_event <= 3 and actual < 0.5:
        multiplier -= 0.10
        reason = "clearance"

    multiplier = max(0.6, min(1.8, multiplier))
    raw = round_to_half(d.base_price * Decimal(str(round(multiplier, 4))))
    price = max(d.min_price, min(d.max_price, raw))
    return PriceDecision(price=price.quantize(Decimal("0.01")), sell_through=actual, expected_sell_through=expected, reason=reason)


def is_material_change(old: Decimal, new: Decimal, min_ratio: float) -> bool:
    if old <= 0:
        return new > 0
    return abs(float(new - old)) / float(old) >= min_ratio
