# API reference

Browsers only ever call the two BFFs. The service APIs below are internal; they're listed because
the BFFs, the load simulator and other services use them, and because they're handy when debugging.

Interactive docs (with `docker compose up`, which publishes service ports via the override file):

| Service | OpenAPI UI |
|---|---|
| identity | http://localhost:5201/scalar |
| events | http://localhost:5202/scalar |
| seating | http://localhost:5203/scalar |
| orders | http://localhost:5204/scalar |
| notifier | http://localhost:5205/scalar |
| ticketing-bff | http://localhost:5000/scalar |
| dashboard-bff | http://localhost:5100/scalar |
| pricing | http://localhost:8001/docs |
| analytics | http://localhost:8002/docs |

All .NET services return ProblemDetails on errors. Every service has `GET /alive` and `GET /health`.

## ticketing-bff (`:5000`, cookie `__Host-tickethub-app`)

State-changing requests need `X-CSRF: 1` and `X-XSRF-TOKEN` (the value of the readable `XSRF-TOKEN-APP` cookie).

| Method | Path | Notes |
|---|---|---|
| POST | `/bff/login` | `{ email, password }` → user (no tokens) and session cookie |
| POST | `/bff/logout` | revokes the refresh token and clears the session |
| GET | `/bff/user` | `{ isAuthenticated, user }`, also refreshes the XSRF cookie |
| GET | `/bff/events/{id}/page` | events + seating (gRPC) + pricing, `partial` flag |
| GET | `/bff/checkout/{holdId}` | seating hold + pricing locks + orders quote |
| GET | `/bff/me/tickets` | orders tickets + events summaries, grouped upcoming/past |
| * | `/api/events/**`, `/api/venues`, `/api/categories` | proxied to events |
| POST | `/api/seating/holds` | proxied to seating, rate limited per user (10 / 30 s) |
| * | `/api/seating/**` | proxied to seating |
| * | `/api/orders/**`, `/api/tickets/**`, `/api/checkin` | proxied to orders, session required |
| GET | `/api/prices/**` | proxied to pricing |
| WS | `/hubs/seating` | proxied SignalR hub |

Example:

```http
POST /api/seating/holds
X-CSRF: 1
X-XSRF-TOKEN: CfDJ8…
Content-Type: application/json

{ "eventId": "0f4f2e7a-…", "seatIds": ["b1c2…", "b1c3…"] }
```

```json
{ "holdId": "8d2f…", "eventId": "0f4f2e7a-…", "seats": [{ "seatId": "b1c2…", "label": "Floor A A4", "tierId": "…", "sectionId": "…" }],
  "heldAt": "2025-06-12T19:00:02Z", "expiresAt": "2025-06-12T19:10:02Z", "secondsLeft": 600 }
```

## dashboard-bff (`:5100`, cookie `__Host-tickethub-dash`)

Only `Owner`, `Organizer` and `Admin` can log in. All endpoints accept `from`, `to` (yyyy-MM-dd),
`granularity` (day/week/month), `city`, `category`, `venue`, `organizerId` (owners only) and
`eventIds`/`eventId`. For organizers the BFF always injects their own event ids.
Responses carry `X-Cache: HIT|MISS`.

| Method | Path | Composes |
|---|---|---|
| GET | `/bff/overview` | analytics KPIs (current + previous period, trends) + events counts |
| GET | `/bff/sales/timeseries` | analytics |
| GET | `/bff/breakdowns?by=event\|artist\|category\|city\|venue` | analytics + events metadata |
| GET | `/bff/sections/heatmap` | analytics |
| GET | `/bff/events/performance` | analytics + seating live sold counts (gRPC) |
| GET | `/bff/events/{id}/insights` | analytics + pricing history + events |
| GET | `/bff/forecast/revenue?horizon=30\|60\|90` | analytics |
| GET | `/bff/forecast/accuracy` | analytics backtest |
| GET | `/bff/demand/heatmap`, `/bff/demand/lead-time` | analytics |
| GET | `/bff/funnel`, `/bff/pricing-impact`, `/bff/alerts`, `/bff/filters` | analytics |
| GET | `/bff/cancellations` | orders saga status, filtered to scope |
| GET | `/bff/export/{report}.csv` | `performance`, `sales`, `breakdown-{by}`, `pricing-impact`, `alerts`, `lead-time`, `funnel`; 5 per minute per user |
| GET | `/api/venues`, `/api/categories` | proxied to events |

## identity (`:5201`)

| Method | Path | Notes |
|---|---|---|
| POST | `/auth/login` | `{ email, password }` → `{ accessToken, refreshToken, expiresAt, user }` |
| POST | `/auth/refresh` | rotates the refresh token; replaying an old one revokes the family |
| POST | `/auth/revoke` | |
| POST | `/auth/register` | new Buyer |
| GET | `/users/me` | bearer token |
| GET | `/users`, `/users/{id}` | Owner/Admin |

## events (`:5202`)

| Method | Path | Notes |
|---|---|---|
| GET | `/events?q&city&category&from&to&page&pageSize` | published, upcoming, paginated |
| GET | `/events/{id}` | drafts only visible to their organizer |
| GET | `/events/{id}/layout` | sections, seats, tiers (used by seating) |
| GET | `/events/mine` | Organizer |
| POST | `/events` | Organizer; tiers and sections (`rows` × `seatsPerRow`) |
| PUT | `/events/{id}` | publishes `event-updated` when already published |
| POST | `/events/{id}/publish`, `/events/{id}/cancel` | owner only; outbox events |
| GET | `/events/ids?organizerId`, `/events/summaries?ids`, `/events/stats?ids` | internal, used by the BFFs |
| GET | `/venues`, `/categories` | |

## seating (`:5203` HTTP, `:5213` gRPC)

| Method | Path | Notes |
|---|---|---|
| GET | `/seating/events/{eventId}/availability` | counts and per-seat state |
| POST | `/seating/holds` | `{ eventId, seatIds }` (max 8) → 201, 409 with `conflicts` |
| GET | `/seating/holds/{holdId}` | owner of the hold only |
| DELETE | `/seating/holds/{holdId}` | release |
| WS | `/hubs/seating` | `JoinEvent(eventId)`, `LeaveEvent(eventId)`; server sends `SeatsChanged`, `PriceChanged` |

gRPC service `tickethub.seating.v1.SeatingRpc` ([`shared/protos/seating.proto`](../shared/protos/seating.proto)):
`GetAvailability`, `GetHold`, `ConfirmHold`, `ReleaseHold`, `GetSoldCounts`.

## orders (`:5204`)

| Method | Path | Notes |
|---|---|---|
| GET | `/orders/quote?holdId` | locked prices, fees, any existing order for the hold |
| POST | `/orders` | `{ holdId }`, `Idempotency-Key` header required |
| GET | `/orders/mine`, `/orders/{id}` | |
| POST | `/orders/{id}/mock-pay` | `{ succeed }`; only with the mock provider |
| POST | `/orders/{id}/refund` | Owner/Admin |
| GET | `/tickets/mine`, `/tickets/{id}` | ticket detail includes a QR PNG (base64) |
| POST | `/checkin` | `{ code, eventId? }`, Staff/Organizer → `admitted` or 409 `already-checked-in`/`voided` |
| POST | `/webhooks/stripe` | Stripe-Signature verified; anonymous, not routed through any BFF |
| POST | `/webhooks/mock` | Mock-Signature verified |
| GET | `/cancellations?eventId` | cancellation saga progress |

## notifier (`:5205`)

| Method | Path | Notes |
|---|---|---|
| GET | `/emails/recent` | last 50 sent emails (template, recipient, subject) |

## pricing (`:8001`)

| Method | Path | Notes |
|---|---|---|
| GET | `/prices/{eventId}` | current price per tier with sold/held counts |
| POST | `/prices/locks` | `{ holdId, eventId, heldAt, seats[] }` → price per seat, as of `heldAt`; idempotent per hold; 503 if the event isn't known yet |
| GET | `/prices/locks/{holdId}` | |
| GET | `/prices/{eventId}/history` | every price change with reason |
| POST | `/prices/{eventId}/recompute` | force a pricing pass |

## analytics (`:8002`)

All read endpoints take `from`, `to`, `eventIds`, `city`, `category`, `venue`, `organizerId`.

| Method | Path |
|---|---|
| GET | `/analytics/kpis` |
| GET | `/analytics/sales/timeseries?granularity` |
| GET | `/analytics/breakdowns?by&limit` |
| GET | `/analytics/sections/heatmap` |
| GET | `/analytics/events/performance` |
| GET | `/analytics/events/ids?organizerId` |
| GET | `/analytics/events/{id}/insights` |
| GET | `/analytics/forecast/revenue?horizon`, `/analytics/forecast/accuracy` |
| GET | `/analytics/demand/heatmap`, `/analytics/demand/lead-time` |
| GET | `/analytics/funnel`, `/analytics/pricing-impact`, `/analytics/alerts`, `/analytics/filters` |
| POST | `/analytics/jobs/refresh`, `/analytics/jobs/retrain` |
