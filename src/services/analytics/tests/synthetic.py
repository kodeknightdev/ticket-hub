"""Synthetic ticket sales with an on-sale spike, decay, weekend lift and a pre-event surge."""

from datetime import date, timedelta

import numpy as np
import pandas as pd


def sales_curve(days: int, total: int, seed: int = 0, spike: float = 0.18, surge: float = 0.15) -> pd.Series:
    rng = np.random.default_rng(seed)
    t = np.arange(days)
    base = np.exp(-t / (days / 3.5))
    base[: max(1, days // 30)] += spike * days / 10
    tail = np.zeros(days)
    tail[-10:] = np.linspace(0.2, 1.0, 10)
    shape = base + surge * tail * base.sum() / 10
    start = date(2024, 6, 1)
    weekday = np.array([(start + timedelta(days=int(i))).weekday() for i in t])
    shape *= np.where(weekday >= 5, 1.3, 1.0)
    expected = shape / shape.sum() * total
    counts = rng.poisson(expected)
    return pd.Series(counts.astype(float), index=[start + timedelta(days=int(i)) for i in t])
