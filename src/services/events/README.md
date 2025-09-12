# events

The event catalogue: venues, events, sections, seats and price tiers, with the draft → published →
cancelled lifecycle. Seat maps are generated from `rows × seatsPerRow` per section.

## Endpoints

| Method | Path | |
|---|---|---|
| GET | `/events` | search published events (`q`, `city`, `category`, `from`, `to`, paging) |
| GET | `/events/{id}`, `/events/{id}/layout` | detail; full seat map |
| GET | `/events/mine` | Organizer |
| POST | `/events` | create draft with tiers and sections |
| PUT | `/events/{id}` | |
| POST | `/events/{id}/publish`, `/events/{id}/cancel` | |
| GET | `/events/ids`, `/events/summaries`, `/events/stats` | internal lookups for the BFFs |
| GET | `/venues`, `/categories` | |

## Events

- Publishes (outbox): `events.event-published`, `events.event-updated`, `events.event-cancelled`
- Consumes: `pricing.price-updated` (keeps `CurrentPrice` on tiers)

## Running alone

```bash
export Kafka__BootstrapServers=localhost:29092
export ConnectionStrings__Default="Server=localhost,1433;Database=TicketHub_Events;User Id=events_svc;Password=...;TrustServerCertificate=True"
dotnet run --project src/services/events/TicketHub.Events   # http://localhost:5202/scalar
```

Development seeds five venues and eight published events split between the two demo organizers.
