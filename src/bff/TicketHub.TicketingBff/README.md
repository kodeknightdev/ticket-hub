# ticketing-bff

Backend-for-Frontend for `ticketing-frontend`. Owns the `__Host-tickethub-app` cookie session,
keeps tokens in Redis, proxies CRUD and the SignalR hub with YARP, and composes page-shaped responses.
See [docs/bff.md](../../../docs/bff.md).

## Endpoints

- `/bff/login`, `/bff/logout`, `/bff/user`
- `/bff/events/{id}/page`, `/bff/checkout/{holdId}`, `/bff/me/tickets`
- proxied: `/api/events/**`, `/api/seating/**` (holds rate limited), `/api/orders/**`, `/api/tickets/**`, `/api/checkin`, `/api/prices/**`, `/hubs/seating`

Publishes and consumes no Kafka messages.

## Running alone

```bash
export ConnectionStrings__Redis=localhost:6379
dotnet run --project src/bff/TicketHub.TicketingBff   # http://localhost:5000
```

Downstream addresses come from `Services:*`, `Bff:IdentityUrl` and the `ReverseProxy:Clusters` section.
