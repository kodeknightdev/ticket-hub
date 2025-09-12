# pricing

Demand-based pricing in Python/FastAPI. Each tier's price moves with how sell-through compares to an
expected curve for the time elapsed since on-sale, with a last-minute bump for hot shows and a
clearance cut for slow ones, always within the organizer's min/max and rounded to 50p. Changes under
2% are ignored. Prices are locked per hold at the price in effect when the hold was placed.

Rules: [`app/rules.py`](app/rules.py). Service logic: [`app/service.py`](app/service.py).

## Endpoints

| Method | Path | |
|---|---|---|
| GET | `/prices/{eventId}` | current tier prices |
| POST | `/prices/locks` | lock prices for a hold (idempotent) |
| GET | `/prices/locks/{holdId}` | |
| GET | `/prices/{eventId}/history` | |
| POST | `/prices/{eventId}/recompute` | |
| GET | `/health`, `/alive`, `/docs` | |

## Events

- Consumes (`pricing`): `events.event-published`, `events.event-cancelled`, `seating.seat-held`,
  `seating.seat-released`, `seating.hold-expired`, `seating.seats-sold`
- Publishes: `pricing.price-updated`

## Running alone

```bash
cd src/services/pricing
pip install -r requirements.txt
KAFKA_ENABLED=false uvicorn app.main:app --reload --port 8001   # SQLite by default
pytest
```

`requirements.txt` is compiled from `requirements.in`.
