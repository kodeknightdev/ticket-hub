from datetime import timedelta

import numpy as np
import pandas as pd

from app import forecasting

from .synthetic import sales_curve


def test_backtest_mape_on_synthetic_events_is_acceptable():
    completed = [sales_curve(90, 1500, seed=i) for i in range(6)]
    mapes = []
    for i, series in enumerate(completed):
        reference = forecasting.build_reference_curve([c for j, c in enumerate(completed) if j != i])
        event_date = series.index[-1] + timedelta(days=1)
        mapes.append(forecasting.backtest_event(series, 2000, event_date, reference))
    assert all(m is not None for m in mapes)
    assert np.mean(mapes) < 25, mapes


def test_holt_winters_used_with_enough_history():
    series = sales_curve(60, 800, seed=3)
    history = series.iloc[:40]
    today = series.index[40]
    fc = forecasting.forecast_event(history, int(history.sum()), 1000, series.index[-1] + timedelta(days=1), today)
    assert fc.model == "holt-winters"
    assert len(fc.dates) == 20
    assert all(lo <= p <= up for lo, p, up in zip(fc.lower, fc.predicted, fc.upper))
    assert fc.cumulative[-1] >= history.sum()


def test_fallback_to_similar_events_with_little_history():
    reference = forecasting.build_reference_curve([sales_curve(60, 900, seed=i) for i in range(4)])
    history = pd.Series([40.0, 25.0, 18.0], index=pd.date_range("2025-01-01", periods=3).date)
    today = history.index[-1] + timedelta(days=1)
    fc = forecasting.forecast_event(history, 83, 1000, today + timedelta(days=57), today, reference)
    assert fc.model == "similar-events"
    assert 0 < fc.projected_sell_through <= 1


def test_hot_event_gets_sellout_date_and_capped_cumulative():
    series = sales_curve(30, 2000, seed=7)
    history = series.iloc[:20]
    fc = forecasting.forecast_event(history, int(history.sum()), int(history.sum()) + 50, series.index[-1] + timedelta(days=1), series.index[20])
    assert fc.sellout_date is not None
    assert max(fc.cumulative) <= fc.capacity
    assert fc.pace_status == "On track"


def test_slow_event_is_at_risk():
    history = pd.Series([3.0, 2.0, 2.0, 1.0, 2.0, 1.0, 1.0, 1.0, 0.0, 1.0], index=pd.date_range("2025-01-01", periods=10).date)
    today = history.index[-1] + timedelta(days=1)
    fc = forecasting.forecast_event(history, 14, 1000, today + timedelta(days=30), today)
    assert fc.pace_status == "At risk"
    assert fc.projected_sell_through < 0.5


def test_revenue_forecast_keeps_weekly_shape():
    days = pd.date_range("2025-01-06", periods=120).date
    revenue = pd.Series([1000 + (600 if d.weekday() >= 5 else 0) + 3 * i for i, d in enumerate(days)], index=days, dtype=float)
    fc = forecasting.forecast_revenue(revenue, days[-1] + timedelta(days=1), 90)
    assert len(fc.dates) == 90
    weekend = [p for d, p in zip(fc.dates, fc.predicted) if d.weekday() >= 5]
    weekday = [p for d, p in zip(fc.dates, fc.predicted) if d.weekday() < 5]
    assert np.mean(weekend) > np.mean(weekday)
    assert fc.total(30)["lower"] <= fc.total(30)["predicted"] <= fc.total(30)["upper"]
