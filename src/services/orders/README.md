# orders

Orders, payments, tickets and check-in. Payments go through an `IPaymentProvider` anti-corruption
layer: Stripe in **test mode** when `Stripe:SecretKey` is an `sk_test_` key, otherwise a built-in mock
provider that signs its webhooks the same way. Also runs the event-cancellation saga.

## Endpoints

| Method | Path | |
|---|---|---|
| GET | `/orders/quote?holdId` | locked prices + fees |
| POST | `/orders` | needs `Idempotency-Key` |
| GET | `/orders/mine`, `/orders/{id}` | |
| POST | `/orders/{id}/mock-pay` | mock provider only |
| POST | `/orders/{id}/refund` | Owner/Admin |
| GET | `/tickets/mine`, `/tickets/{id}` | ticket detail includes the QR PNG |
| POST | `/checkin` | Staff/Organizer |
| POST | `/webhooks/stripe`, `/webhooks/mock` | signature-verified, deduplicated by event id |
| GET | `/cancellations` | saga progress |

## Events

- Publishes (outbox): `orders.order-created`, `orders.order-paid`, `orders.order-expired`,
  `orders.order-refunded`, `orders.tickets-issued`, `orders.ticket-checked-in`
- Consumes: `seating.seats-sold` (issue tickets), `seating.hold-expired`, `events.event-cancelled` (start the saga)
- Calls: seating over gRPC (`GetHold`, `ConfirmHold`, `ReleaseHold`), pricing over REST (`/prices/locks`)

## Running alone

```bash
export Kafka__BootstrapServers=localhost:29092 Services__SeatingGrpc=http://localhost:5213 Services__Pricing=http://localhost:8001
export ConnectionStrings__Default="Server=localhost,1433;Database=TicketHub_Orders;User Id=orders_svc;Password=...;TrustServerCertificate=True"
dotnet run --project src/services/orders/TicketHub.Orders   # http://localhost:5204/scalar
```

Never configure a live Stripe key; only `sk_test_…` keys are accepted.
