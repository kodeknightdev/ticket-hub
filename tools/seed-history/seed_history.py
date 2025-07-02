"""Generates ~12 months of synthetic ticket sales straight into the analytics database.

The shape is deliberately realistic so the dashboard and forecasts have something to chew on:
an on-sale spike, decay, weekend lift, a pre-event surge, summer/December seasonality, a few
events that are pacing badly, a refund spike and one show with a poor check-in rate.

    DATABASE_URL=... python seed_history.py [--events 30] [--seed 2025] [--no-jobs]
"""

import argparse
import math
import os
import random
import sys
import uuid
from datetime import date, datetime, timedelta
from decimal import Decimal

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.environ.get("ANALYTICS_PATH", os.path.join(HERE, "..", "..", "src", "services", "analytics")))

from sqlalchemy import delete, insert  # noqa: E402

from app import jobs  # noqa: E402
from app.config import get_settings  # noqa: E402
from app.db import Base, Database  # noqa: E402
from app.ingest import ensure_dates  # noqa: E402
from app.models import (  # noqa: E402
    DimEvent, DimVenueSection, FactCheckin, FactHold, FactOrder, FactPriceChange, FactRefund, FactTicketSale,
)
from app.timeutil import date_key, now  # noqa: E402

# Must match TicketHub.Contracts.StableId and DemoAccounts.
NAMESPACE = uuid.UUID("6f2c1a4e-8d3b-4c7a-9e10-5b7d2f4a8c61")
ORGANIZER = "11111111-1111-4111-8111-000000000002"
SECOND_ORGANIZER = "11111111-1111-4111-8111-000000000006"
OTHER_ORGANIZERS = ["5a0f3c52-7a7e-4d1f-9f43-1c2b3d4e5f60", "8e2d1b40-3c5a-4f6e-8a7b-9c0d1e2f3a4b"]

VENUES = [
    ("Riverside Arena", "Manchester", [("Floor A", 600), ("Floor B", 800), ("Lower Tier", 700), ("Upper Tier", 300)]),
    ("The Lantern Hall", "Leeds", [("Stalls", 450), ("Circle", 250), ("Balcony", 100)]),
    ("Harbour Bowl", "Bristol", [("Pit", 900), ("Terrace", 700), ("Hill", 400)]),
    ("Northern Stage", "Glasgow", [("Stalls", 380), ("Dress Circle", 160), ("Gallery", 60)]),
    ("Old Mill Theatre", "London", [("Stalls", 320), ("Royal Circle", 140), ("Grand Circle", 90)]),
    ("Quayside Club", "Newcastle", [("Floor", 260), ("Mezzanine", 90)]),
]

CATEGORY_ARTISTS = {
    "Concert": ["Velvet Static", "Neon Harbour", "Paper Lanterns", "The Brightsides", "Mira Vale", "Echo Valley", "Static Bloom"],
    "Festival": ["Summer Tides", "Northern Lights Weekender", "Harbourfest"],
    "Comedy": ["Mira Patel", "Dan O'Rourke", "The Improv Collective"],
    "Theatre": ["Arden Quartet", "The Glass Menagerie", "Twelfth Night"],
    "Sport": ["City Cup Final", "Boxing: Title Night", "Derby Day"],
    "Family": ["The Great Puppet Parade", "Dino Live", "Circus of Wonders"],
}

TIERS = [("Premium", 1.6), ("Standard", 1.0), ("Value", 0.65)]
BASE_PRICE = {"Concert": 55, "Festival": 85, "Comedy": 32, "Theatre": 42, "Sport": 48, "Family": 24}


def sid(name: str) -> str:
    return str(uuid.uuid5(NAMESPACE, name))


def month_factor(d: date) -> float:
    return {6: 1.25, 7: 1.35, 8: 1.3, 12: 1.4, 1: 0.8, 2: 0.85}.get(d.month, 1.0)


def daily_weights(start: date, event_day: date, rng: random.Random) -> list[tuple[date, float]]:
    days = (event_day - start).days
    out = []
    for i in range(days):
        d = start + timedelta(days=i)
        w = math.exp(-i / max(days / 3.2, 1))
        if i < 2:
            w += 4.5 - 2.0 * i
        left = days - i
        if left <= 10:
            w += 0.9 * (11 - left) / 10
        if d.weekday() >= 4:
            w *= 1.35
        w *= month_factor(d) * rng.uniform(0.8, 1.2)
        out.append((d, w))
    return out


def sale_hour(rng: random.Random, on_sale_day: bool) -> int:
    if on_sale_day and rng.random() < 0.5:
        return 10
    return rng.choices(range(24), weights=[1, 1, 1, 1, 1, 1, 2, 4, 6, 7, 8, 9, 12, 11, 8, 7, 8, 10, 13, 15, 14, 11, 7, 3])[0]


def build_events(n: int, rng: random.Random, today: date) -> list[dict]:
    events = []
    for i in range(n):
        category = rng.choices(list(CATEGORY_ARTISTS), weights=[10, 2, 4, 3, 3, 2])[0]
        artist = rng.choice(CATEGORY_ARTISTS[category])
        venue_name, city, sections = VENUES[rng.randrange(len(VENUES))]
        if category == "Festival":
            venue_name, city, sections = VENUES[2]
        offset = int(-300 + (390 * i / max(n - 1, 1))) + rng.randint(-6, 6)
        starts = datetime.combine(today + timedelta(days=offset), datetime.min.time()) + timedelta(hours=rng.choice([13, 19, 19, 20]))
        window = rng.randint(45, 120)
        published = starts - timedelta(days=window)
        if published.date() > today - timedelta(days=3):
            published = datetime.combine(today - timedelta(days=3), datetime.min.time()) + timedelta(hours=10)
        organizer = ORGANIZER if i % 3 == 0 else SECOND_ORGANIZER if i % 3 == 1 else rng.choice(OTHER_ORGANIZERS)
        target = rng.uniform(0.72, 1.0)
        # a few upcoming events deliberately pace badly so the at-risk alert has something to say
        if offset > 0 and i % 4 == 1:
            target = rng.uniform(0.35, 0.55)
        title = f"{artist}" + ("" if category in ("Sport", "Festival") else rng.choice([" Live", ": On Tour", " In Concert", ""]))
        events.append({
            "key": f"history:{i}", "event_id": sid(f"history:{i}"), "organizer_id": organizer, "title": title, "artist": artist,
            "category": category, "city": city, "venue_id": sid(f"venue:{city}:{venue_name}".lower()), "venue_name": venue_name,
            "sections": [(sid(f"history:{i}:section:{name}"), name, cap) for name, cap in sections],
            "starts_at": starts, "published_at": published, "target": target,
            "base": BASE_PRICE[category] * rng.uniform(0.85, 1.25),
        })
    return events


def simulate(evt: dict, rng: random.Random, today: date, rows: dict[str, list]) -> None:
    capacity = sum(c for _, _, c in evt["sections"])
    evt["capacity"] = capacity
    start, event_day = evt["published_at"].date(), evt["starts_at"].date()
    weights = daily_weights(start, event_day, rng)
    total_w = sum(w for _, w in weights)
    final_tickets = int(capacity * evt["target"])

    tiers = [(sid(f"{evt['key']}:tier:{name}"), name, round(evt["base"] * f, 2)) for name, f in TIERS]
    prices = {tid: Decimal(str(round(p * 2) / 2)) for tid, _, p in tiers}
    seat_counter = 0
    sold = 0
    next_review = start + timedelta(days=rng.randint(5, 9))

    for d, w in weights:
        if d >= today:
            break
        tickets_today = int(round(final_tickets * w / total_w * rng.uniform(0.85, 1.15)))
        tickets_today = max(0, min(tickets_today, capacity - sold))

        if d >= next_review:
            elapsed = (d - start).days / max((event_day - start).days, 1)
            pace = sold / capacity - elapsed ** 0.6
            for tid, _, base in tiers:
                old = prices[tid]
                factor = 1 + max(-0.25, min(0.4, 0.8 * pace))
                new = Decimal(str(round(max(base * 0.7, min(base * 1.6, base * factor)) * 2) / 2))
                if abs(new - old) / old >= Decimal("0.02"):
                    at = datetime.combine(d, datetime.min.time()) + timedelta(hours=3)
                    rows["price"].append(dict(event_id=evt["event_id"], tier_id=tid, old_price=old, new_price=new, sell_through=round(sold / capacity, 4),
                                              reason="demand-high" if new > old else "demand-low", changed_at=at, date_key=date_key(at)))
                    prices[tid] = new
            next_review = d + timedelta(days=rng.randint(6, 12))

        remaining = tickets_today
        while remaining > 0:
            qty = min(remaining, rng.choices([1, 2, 3, 4], weights=[25, 45, 18, 12])[0])
            remaining -= qty
            at = datetime.combine(d, datetime.min.time()) + timedelta(hours=sale_hour(rng, d == start), minutes=rng.randint(0, 59))
            hold_id, order_id, buyer = str(uuid.uuid4()), str(uuid.uuid4()), str(uuid.uuid4())
            tier_id = rng.choices([t[0] for t in tiers], weights=[2, 5, 3])[0]
            total = Decimal(0)
            for _ in range(qty):
                section_id = evt["sections"][seat_counter % len(evt["sections"])][0]
                seat_counter += 1
                price = prices[tier_id]
                total += price
                rows["sale"].append(dict(order_id=order_id, event_id=evt["event_id"], seat_id=str(uuid.uuid4()), section_id=section_id, tier_id=tier_id,
                                         buyer_id=buyer, price=price, sold_at=at, date_key=date_key(at), weekday=at.weekday(), hour=at.hour,
                                         lead_days=(event_day - d).days))
            rows["hold"].append(dict(hold_id=hold_id, event_id=evt["event_id"], buyer_id=buyer, seats=qty, held_at=at - timedelta(minutes=4),
                                     date_key=date_key(at), outcome="converted", ended_at=at))
            rows["order"].append(dict(order_id=order_id, hold_id=hold_id, event_id=evt["event_id"], total=total, status="paid",
                                      created_at=at - timedelta(minutes=2), paid_at=at, tickets_issued_at=at + timedelta(seconds=3), date_key=date_key(at)))
            if rng.random() < 0.018:
                ref_at = at + timedelta(days=rng.randint(1, 20))
                if ref_at.date() < today and ref_at.date() <= event_day:
                    rows["refund"].append(dict(order_id=order_id, event_id=evt["event_id"], amount=total, tickets=qty, reason="requested",
                                               refunded_at=ref_at, date_key=date_key(ref_at)))
                    rows["order"][-1]["status"] = "refunded"
            sold += qty

        # holds that never turned into orders: abandoned or timed out
        for _ in range(int(tickets_today * rng.uniform(0.15, 0.35))):
            at = datetime.combine(d, datetime.min.time()) + timedelta(hours=sale_hour(rng, d == start), minutes=rng.randint(0, 59))
            outcome = rng.choices(["expired", "released"], weights=[4, 1])[0]
            hold_id = str(uuid.uuid4())
            rows["hold"].append(dict(hold_id=hold_id, event_id=evt["event_id"], buyer_id=str(uuid.uuid4()), seats=rng.randint(1, 4),
                                     held_at=at, date_key=date_key(at), outcome=outcome, ended_at=at + timedelta(minutes=10)))
            if outcome == "expired" and rng.random() < 0.3:
                rows["order"].append(dict(order_id=str(uuid.uuid4()), hold_id=hold_id, event_id=evt["event_id"], total=Decimal("0"), status="expired",
                                          created_at=at + timedelta(minutes=3), paid_at=None, tickets_issued_at=None, date_key=date_key(at)))

    evt["sold"] = sold


def add_checkins(evt: dict, rng: random.Random, today: date, rows: dict[str, list], rate: float) -> None:
    if evt["starts_at"].date() >= today:
        return
    for sale in (s for s in rows["sale"] if s["event_id"] == evt["event_id"]):
        if any(r["order_id"] == sale["order_id"] for r in rows["refund"]) or rng.random() > rate:
            continue
        at = evt["starts_at"] - timedelta(minutes=rng.randint(0, 90))
        rows["checkin"].append(dict(ticket_id=str(uuid.uuid4()), event_id=evt["event_id"], seat_id=sale["seat_id"], checked_in_at=at, date_key=date_key(at)))


def inject_refund_spike(evt: dict, rng: random.Random, today: date, rows: dict[str, list]) -> None:
    orders = [o for o in rows["order"] if o["event_id"] == evt["event_id"] and o["status"] == "paid"]
    refunded = {r["order_id"] for r in rows["refund"]}
    for order in rng.sample(orders, k=min(len(orders), max(12, len(orders) // 8))):
        if order["order_id"] in refunded:
            continue
        tickets = sum(1 for s in rows["sale"] if s["order_id"] == order["order_id"])
        at = datetime.combine(today - timedelta(days=rng.randint(0, 5)), datetime.min.time()) + timedelta(hours=rng.randint(9, 21))
        rows["refund"].append(dict(order_id=order["order_id"], event_id=evt["event_id"], amount=order["total"], tickets=tickets,
                                   reason="requested", refunded_at=at, date_key=date_key(at)))
        order["status"] = "refunded"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--events", type=int, default=30)
    parser.add_argument("--seed", type=int, default=2025)
    parser.add_argument("--no-jobs", action="store_true", help="skip the rollup/forecast refresh at the end")
    args = parser.parse_args()

    rng = random.Random(args.seed)
    today = now().date()
    settings = get_settings()
    db = Database(settings.database_url)
    if settings.database_url.startswith("sqlite"):
        Base.metadata.create_all(db.engine)

    events = build_events(args.events, rng, today)
    rows: dict[str, list] = {k: [] for k in ("sale", "hold", "order", "refund", "checkin", "price")}
    for evt in events:
        simulate(evt, rng, today, rows)

    past = [e for e in events if e["starts_at"].date() < today]
    upcoming = [e for e in events if e["starts_at"].date() >= today]
    low_checkin = past[-1] if past else None
    for evt in past:
        add_checkins(evt, rng, today, rows, 0.64 if evt is low_checkin else rng.uniform(0.82, 0.95))
    if upcoming:
        inject_refund_spike(max(upcoming[:4], key=lambda e: e.get("sold", 0)), rng, today, rows)

    ids = [e["event_id"] for e in events]
    with db.session() as s:
        for model in (FactTicketSale, FactHold, FactOrder, FactRefund, FactCheckin, FactPriceChange, DimVenueSection, DimEvent):
            s.execute(delete(model).where(model.event_id.in_(ids)))
        s.execute(insert(DimEvent), [dict(
            event_id=e["event_id"], organizer_id=e["organizer_id"], title=e["title"], artist=e["artist"], category=e["category"], city=e["city"],
            venue_id=e["venue_id"], venue_name=e["venue_name"], capacity=e["capacity"], starts_at=e["starts_at"], published_at=e["published_at"],
            status="published") for e in events])
        s.execute(insert(DimVenueSection), [dict(section_id=sec_id, event_id=e["event_id"], venue_id=e["venue_id"], name=name, capacity=cap)
                                            for e in events for sec_id, name, cap in e["sections"]])
        for model, key in ((FactTicketSale, "sale"), (FactHold, "hold"), (FactOrder, "order"), (FactRefund, "refund"),
                           (FactCheckin, "checkin"), (FactPriceChange, "price")):
            batch = rows[key]
            for i in range(0, len(batch), 5000):
                s.execute(insert(model), batch[i:i + 5000])
        ensure_dates(s, min(e["published_at"] for e in events).date(), max(e["starts_at"] for e in events).date())
        s.commit()

    print(f"seeded {len(events)} events ({len(past)} past, {len(upcoming)} upcoming): "
          f"{len(rows['sale'])} tickets, {len(rows['order'])} orders, {len(rows['hold'])} holds, "
          f"{len(rows['refund'])} refunds, {len(rows['checkin'])} check-ins, {len(rows['price'])} price changes")

    if not args.no_jobs:
        print("refresh:", jobs.refresh(db))
        print("retrain:", jobs.retrain_all(db))


if __name__ == "__main__":
    main()
