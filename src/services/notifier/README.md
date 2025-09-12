# notifier

Sends email through MailKit (MailHog locally). Templates are Razor components rendered to HTML with
`HtmlRenderer`; ticket emails embed one QR code per ticket as inline images. Every email is logged in
`sent_emails` with an idempotency key, so a redelivered message doesn't send twice.

## Endpoints

| Method | Path | |
|---|---|---|
| GET | `/emails/recent` | last 50 sent |

## Events

- Consumes: `orders.tickets-issued` (confirmation), `seating.hold-expiring` (warning),
  `orders.order-refunded` (refund, or "event cancelled" when the reason is `event-cancelled`),
  `events.event-published/updated/cancelled` (keeps event titles and dates for templates)

## Running alone

```bash
export Kafka__BootstrapServers=localhost:29092 Smtp__Host=localhost Smtp__Port=1025
export ConnectionStrings__Default="Server=localhost,1433;Database=TicketHub_Notifier;User Id=notifier_svc;Password=...;TrustServerCertificate=True"
dotnet run --project src/services/notifier/TicketHub.Notifier   # mail shows up at http://localhost:8025
```
