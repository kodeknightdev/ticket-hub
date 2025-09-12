# seating

Owns live seat state. Holds are Redis keys with a TTL set atomically by Lua scripts, so a seat can't
be held twice; sold seats are also written to SQL Server with an `(EventId, SeatId)` primary key as
the final guard. Hosts the SignalR hub that pushes seat changes to every viewer, and the gRPC API
used by orders and the BFFs.

## Endpoints

| Method | Path | |
|---|---|---|
| GET | `/seating/events/{eventId}/availability` | |
| POST | `/seating/holds` | up to 8 seats, 10 minutes |
| GET/DELETE | `/seating/holds/{holdId}` | |
| WS | `/hubs/seating` | `JoinEvent`, `LeaveEvent` → `SeatsChanged`, `PriceChanged` |
| gRPC | `SeatingRpc` on `:8081` | `GetAvailability`, `GetHold`, `ConfirmHold`, `ReleaseHold`, `GetSoldCounts` |

## Events

- Publishes: `seating.seat-held`, `seating.seat-released`, `seating.hold-expired`, `seating.hold-expiring`, `seating.seats-sold`
- Consumes (`seating`): `orders.order-paid`, `events.event-published/updated/cancelled`
- Consumes (`seating-broadcast`): seat and price events, fanned out through SignalR (Redis backplane)

## Running alone

```bash
export Kafka__BootstrapServers=localhost:29092 ConnectionStrings__Redis=localhost:6379
export ConnectionStrings__Default="Server=localhost,1433;Database=TicketHub_Seating;User Id=seating_svc;Password=...;TrustServerCertificate=True"
dotnet run --project src/services/seating/TicketHub.Seating   # HTTP :5203, gRPC :5213
```

Seat-map layouts are read from events (`Services:Events`) and cached in Redis for ten minutes.
