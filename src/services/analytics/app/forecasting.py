"""Sales forecasting.

Per event we fit Holt-Winters (damped additive trend, weekly seasonality once there are three
weeks of history) on log1p daily ticket counts. With under 7 days of data the fit is meaningless,
so we fall back to the average cumulative curve of similar completed events, indexed by days
before the event.
"""

import math
import warnings
from collections.abc import Sequence
from dataclasses import dataclass, field
from datetime import date, timedelta

import numpy as np
import pandas as pd

Z80 = 1.2816
MIN_HISTORY_DAYS = 7


@dataclass
class EventForecast:
    model: str
    sold_to_date: int
    capacity: int
    dates: list[date] = field(default_factory=list)
    predicted: list[float] = field(default_factory=list)
    lower: list[float] = field(default_factory=list)
    upper: list[float] = field(default_factory=list)
    cumulative: list[float] = field(default_factory=list)
    cumulative_lower: list[float] = field(default_factory=list)
    cumulative_upper: list[float] = field(default_factory=list)
    expected_to_date: float = 0.0

    @property
    def projected_final(self) -> float:
        return min(float(self.capacity), self.cumulative[-1] if self.cumulative else float(self.sold_to_date))

    @property
    def projected_sell_through(self) -> float:
        return self.projected_final / self.capacity if self.capacity else 0.0

    @property
    def sellout_date(self) -> date | None:
        if self.sold_to_date >= self.capacity:
            return None
        # matches what the UI rounds to 100%, so "100% projected" never sits next to "no sell-out"
        threshold = self.capacity - max(0.5, 0.005 * self.capacity)
        for d, c in zip(self.dates, self.cumulative):
            if c >= threshold:
                return d
        return None

    @property
    def pace_status(self) -> str:
        if self.capacity and self.sold_to_date >= self.capacity:
            return "Sold out"
        return "On track" if self.projected_sell_through >= 0.8 else "At risk"


@dataclass
class ReferenceCurve:
    """frac[d] = share of final sales already made when d days remain before the event."""

    frac: np.ndarray
    source: str

    def at(self, days_before: int) -> float:
        if days_before <= 0:
            return 1.0
        if days_before >= len(self.frac):
            return float(self.frac[-1])
        return float(self.frac[days_before])


def generic_curve(window_days: int) -> ReferenceCurve:
    window = max(window_days, 14)
    d = np.arange(0, window + 1)
    # Front-loaded: most tickets go in the first weeks after the on-sale date.
    frac = 1 - (d / window) ** 1.6
    return ReferenceCurve(np.clip(frac, 0, 1), "generic")


def build_reference_curve(completed: Sequence[pd.Series], horizon: int = 365) -> ReferenceCurve | None:
    """Each series is daily tickets for a finished event, ending the day before the event."""
    curves = []
    for series in completed:
        total = float(series.sum())
        if total <= 0 or len(series) < 14:
            continue
        cumulative = series.cumsum().to_numpy() / total
        # index by days before the event: last element is 1 day before
        days_before = np.arange(len(series), 0, -1)
        curve = np.ones(horizon + 1)
        for db, c in zip(days_before, cumulative):
            if db <= horizon:
                curve[db] = c
        # before the series started nothing had been sold yet
        curve[len(series) + 1:] = 0.0
        curve[0] = 1.0
        curves.append(curve)
    if not curves:
        return None
    return ReferenceCurve(np.mean(curves, axis=0), f"similar-events({len(curves)})")


def _holt_winters(history: np.ndarray, horizon: int) -> tuple[np.ndarray, float]:
    from statsmodels.tsa.holtwinters import ExponentialSmoothing

    y = np.log1p(np.clip(history, 0, None))
    seasonal = 7 if len(y) >= 21 else None
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        fit = ExponentialSmoothing(
            y, trend="add", damped_trend=True,
            seasonal="add" if seasonal else None, seasonal_periods=seasonal,
            initialization_method="estimated",
        ).fit(optimized=True)
    forecast = np.asarray(fit.forecast(horizon))
    sigma = float(np.std(fit.resid)) if len(fit.resid) > 2 else 0.5
    return forecast, sigma


def forecast_event(
    history: pd.Series,
    sold_to_date: int,
    capacity: int,
    event_date: date,
    today: date,
    reference: ReferenceCurve | None = None,
) -> EventForecast:
    """history: daily ticket counts from on-sale up to (not including) today."""
    horizon = max(0, (event_date - today).days)
    result = EventForecast(model="none", sold_to_date=sold_to_date, capacity=capacity)
    remaining = max(0, capacity - sold_to_date)
    if horizon == 0 or remaining == 0:
        result.model = "complete"
        result.expected_to_date = float(sold_to_date)
        return result

    dates = [today + timedelta(days=i) for i in range(horizon)]
    window = len(history) + horizon
    ref = reference or generic_curve(window)
    days_before_now = horizon

    if len(history) >= MIN_HISTORY_DAYS and history.sum() > 0:
        log_fc, sigma = _holt_winters(history.to_numpy(dtype=float), horizon)
        daily = np.expm1(log_fc).clip(min=0)
        lower = np.expm1(log_fc - Z80 * sigma).clip(min=0)
        upper = np.expm1(log_fc + Z80 * sigma)

        # Holt-Winters can't see the pre-event surge; lift the tail by the reference curve's shape.
        frac_now = ref.at(days_before_now)
        if reference is not None and 0.05 < frac_now < 0.98:
            curve_final = sold_to_date / frac_now
            curve_remaining = max(0.0, curve_final - sold_to_date)
            hw_remaining = float(daily.sum())
            if hw_remaining > 0 and curve_remaining > hw_remaining:
                scale = 0.5 + 0.5 * curve_remaining / hw_remaining
                daily, lower, upper = daily * scale, lower * scale, upper * scale
        result.model = "holt-winters"
    else:
        frac_now = max(ref.at(days_before_now), 0.03)
        final = min(float(capacity), sold_to_date / frac_now) if sold_to_date > 0 else capacity * 0.6
        increments = []
        for i in range(horizon):
            d_left = horizon - i
            increments.append(max(0.0, final * (ref.at(d_left - 1) - ref.at(d_left))))
        daily = np.array(increments)
        lower, upper = daily * 0.7, daily * 1.3
        result.model = "similar-events" if reference is not None else "generic-curve"

    cumulative = sold_to_date + np.cumsum(daily)
    cum_lower = sold_to_date + np.cumsum(lower)
    cum_upper = sold_to_date + np.cumsum(upper)

    result.dates = dates
    result.predicted = [round(float(x), 2) for x in daily]
    result.lower = [round(float(x), 2) for x in lower]
    result.upper = [round(float(x), 2) for x in upper]
    result.cumulative = [round(float(min(capacity, x)), 2) for x in cumulative]
    result.cumulative_lower = [round(float(min(capacity, x)), 2) for x in cum_lower]
    result.cumulative_upper = [round(float(min(capacity, x)), 2) for x in cum_upper]
    result.expected_to_date = round(result.projected_final * ref.at(days_before_now), 2)
    return result


def backtest_event(series: pd.Series, capacity: int, event_date: date, reference: ReferenceCurve | None, cut_ratio: float = 0.6) -> float | None:
    """MAPE (%) of the cumulative curve over the held-out tail of a finished event."""
    if len(series) < 14 or series.sum() <= 0:
        return None
    cut = max(MIN_HISTORY_DAYS, int(len(series) * cut_ratio))
    train, test = series.iloc[:cut], series.iloc[cut:]
    if test.empty:
        return None
    cut_date = series.index[cut]
    fc = forecast_event(train, int(train.sum()), capacity, event_date, cut_date, reference)
    actual_cum = train.sum() + test.cumsum().to_numpy()
    predicted = np.array(fc.cumulative[: len(actual_cum)])
    if len(predicted) < len(actual_cum):
        predicted = np.pad(predicted, (0, len(actual_cum) - len(predicted)), mode="edge")
    mask = actual_cum > 0
    return float(np.mean(np.abs(actual_cum[mask] - predicted[mask]) / actual_cum[mask]) * 100)


@dataclass
class RevenueForecast:
    dates: list[date]
    predicted: list[float]
    lower: list[float]
    upper: list[float]

    def total(self, days: int) -> dict[str, float]:
        n = min(days, len(self.dates))
        return {
            "predicted": round(sum(self.predicted[:n]), 2),
            "lower": round(sum(self.lower[:n]), 2),
            "upper": round(sum(self.upper[:n]), 2),
        }


def forecast_revenue(history: pd.Series, today: date, horizon: int = 90) -> RevenueForecast:
    """Platform revenue with weekly seasonality; history is daily revenue up to yesterday."""
    dates = [today + timedelta(days=i) for i in range(horizon)]
    y = history.to_numpy(dtype=float)
    if len(y) < 14 or y.sum() <= 0:
        mean = float(y.mean()) if len(y) else 0.0
        return RevenueForecast(dates, [mean] * horizon, [mean * 0.7] * horizon, [mean * 1.3] * horizon)

    from statsmodels.tsa.holtwinters import ExponentialSmoothing

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        fit = ExponentialSmoothing(y, trend="add", damped_trend=True, seasonal="add", seasonal_periods=7,
                                   initialization_method="estimated").fit(optimized=True)
    fc = np.asarray(fit.forecast(horizon)).clip(min=0)
    sigma = float(np.std(fit.resid))
    # widen slowly with the horizon; an approximation, not the model's analytic interval
    spread = np.array([Z80 * sigma * math.sqrt(1 + h / 14) for h in range(horizon)])
    return RevenueForecast(
        dates,
        [round(float(x), 2) for x in fc],
        [round(float(x), 2) for x in (fc - spread).clip(min=0)],
        [round(float(x), 2) for x in fc + spread],
    )
