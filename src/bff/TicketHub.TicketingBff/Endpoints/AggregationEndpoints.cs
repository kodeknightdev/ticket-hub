using TicketHub.BffKit;
using TicketHub.TicketingBff.Clients;

namespace TicketHub.TicketingBff.Endpoints;

public sealed record PageSeat(Guid Id, int Number, Guid TierId, string State);
public sealed record PageRow(string Row, IReadOnlyList<PageSeat> Seats);
public sealed record PageSection(Guid Id, string Name, IReadOnlyList<PageRow> Rows);
public sealed record PageTier(Guid Id, string Name, decimal Price, decimal BasePrice);
public sealed record PageAvailability(int Capacity, int Available, int Held, int Sold);

public sealed record EventPage(
    EventDetailDto Event, IReadOnlyList<PageTier> Tiers, IReadOnlyList<PageSection> Sections, PageAvailability? Availability,
    bool Partial, IReadOnlyList<string> Failures);

public sealed record CheckoutLine(Guid SeatId, Guid TierId, string Label, decimal Price);
public sealed record CheckoutPage(
    Guid HoldId, Guid EventId, string? EventTitle, DateTimeOffset? StartsAt, DateTimeOffset ExpiresAt, int SecondsLeft,
    IReadOnlyList<CheckoutLine> Lines, decimal? Subtotal, decimal? Fees, decimal? Total, string Currency,
    OrderDto? ExistingOrder, bool Partial, IReadOnlyList<string> Failures);

public sealed record TicketGroup(EventSummaryDto? Event, IReadOnlyList<TicketDto> Tickets);
public sealed record MyTicketsPage(IReadOnlyList<TicketGroup> Upcoming, IReadOnlyList<TicketGroup> Past, bool Partial, IReadOnlyList<string> Failures);

public static class AggregationEndpoints
{
    public static IEndpointRouteBuilder MapAggregationEndpoints(this IEndpointRouteBuilder app)
    {
        var bff = app.MapGroup("/bff").WithTags("Aggregation");

        bff.MapGet("/events/{id:guid}/page", async (Guid id, EventsApi events, ISeatingReader seating, PricingApi pricing, CancellationToken ct) =>
        {
            var fanout = new Fanout();
            var eventTask = events.GetEventAsync(id, ct);
            var layoutTask = events.GetLayoutAsync(id, ct);
            var availabilityTask = fanout.Optional("seating", () => seating.GetAvailabilityAsync(id, ct));
            var pricesTask = fanout.Optional("pricing", () => pricing.GetPricesAsync(id, ct));

            EventDetailDto? evt;
            LayoutDto? layout;
            try
            {
                await Task.WhenAll(eventTask, layoutTask);
                evt = eventTask.Result;
                layout = layoutTask.Result;
            }
            catch (HttpRequestException)
            {
                return Results.Problem(statusCode: 502, title: "Event catalogue is unavailable");
            }
            if (evt is null || layout is null) return Results.NotFound();

            var availability = await availabilityTask;
            var prices = await pricesTask;
            var priceByTier = prices?.Tiers.ToDictionary(t => Guid.Parse(t.TierId), t => t.Price) ?? [];

            var tiers = layout.Tiers.Select(t => new PageTier(t.Id, t.Name, priceByTier.GetValueOrDefault(t.Id, t.CurrentPrice), t.BasePrice)).ToList();
            var sections = layout.Sections.OrderBy(s => s.SortOrder).Select(s => new PageSection(s.Id, s.Name,
                s.Seats.GroupBy(x => x.Row).OrderBy(g => g.Key.Length).ThenBy(g => g.Key)
                    .Select(g => new PageRow(g.Key, g.OrderBy(x => x.Number)
                        .Select(x => new PageSeat(x.Id, x.Number, x.TierId, availability?.Seats.GetValueOrDefault(x.Id, "unknown") ?? "unknown")).ToList()))
                    .ToList())).ToList();

            return Results.Ok(new EventPage(evt, tiers, sections,
                availability is null ? null : new PageAvailability(availability.Capacity, availability.Available, availability.Held, availability.Sold),
                fanout.Partial, fanout.Failures));
        });

        bff.MapGet("/checkout/{holdId:guid}", async (Guid holdId, HttpContext ctx, ISeatingReader seating, PricingApi pricing, OrdersApi orders,
            EventsApi events, TimeProvider clock, CancellationToken ct) =>
        {
            HoldView? hold;
            try
            {
                hold = await seating.GetHoldAsync(holdId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Problem(statusCode: 502, title: "Seating is unavailable");
            }
            var user = BffSession.ToUser(ctx.User);
            if (hold is null || hold.BuyerId != user.Id) return Results.Problem(statusCode: 410, title: "This hold has expired");

            var fanout = new Fanout();
            var locksTask = fanout.Optional("pricing", () => pricing.GetLocksAsync(holdId, ct));
            var quoteTask = fanout.Optional("orders", () => orders.GetQuoteAsync(holdId, ct));
            var eventTask = fanout.Optional("events", () => events.GetEventAsync(hold.EventId, ct));
            await Task.WhenAll(locksTask, quoteTask, eventTask);

            var quote = quoteTask.Result;
            var locked = locksTask.Result?.Prices.ToDictionary(p => Guid.Parse(p.SeatId), p => p.Price) ?? [];
            var lines = hold.Seats.Select(s => new CheckoutLine(s.SeatId, s.TierId, s.Label,
                quote?.Lines.FirstOrDefault(l => l.SeatId == s.SeatId)?.Price ?? locked.GetValueOrDefault(s.SeatId))).ToList();

            return Results.Ok(new CheckoutPage(
                hold.HoldId, hold.EventId, eventTask.Result?.Title, eventTask.Result?.StartsAt, hold.ExpiresAt,
                Math.Max(0, (int)(hold.ExpiresAt - clock.GetUtcNow()).TotalSeconds),
                lines, quote?.Subtotal, quote?.Fees, quote?.Total, quote?.Currency ?? "GBP", quote?.ExistingOrder,
                fanout.Partial, fanout.Failures));
        }).RequireAuthorization(BffSession.UserPolicy);

        bff.MapGet("/me/tickets", async (OrdersApi orders, EventsApi events, TimeProvider clock, CancellationToken ct) =>
        {
            IReadOnlyList<TicketDto> tickets;
            try
            {
                tickets = await orders.GetMyTicketsAsync(ct);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(statusCode: 502, title: "Orders service is unavailable");
            }

            var fanout = new Fanout();
            var summaries = await fanout.Optional("events", async () => await events.GetSummariesAsync(tickets.Select(t => t.EventId), ct)) ?? [];
            var byId = summaries.ToDictionary(e => e.Id);
            var now = clock.GetUtcNow();

            var groups = tickets.GroupBy(t => t.EventId)
                .Select(g => new TicketGroup(byId.GetValueOrDefault(g.Key), g.OrderBy(t => t.Label).ToList()))
                .ToList();
            var upcoming = groups.Where(g => g.Event is null || g.Event.StartsAt >= now).OrderBy(g => g.Event?.StartsAt).ToList();
            var past = groups.Where(g => g.Event is not null && g.Event.StartsAt < now).OrderByDescending(g => g.Event!.StartsAt).ToList();

            return Results.Ok(new MyTicketsPage(upcoming, past, fanout.Partial, fanout.Failures));
        }).RequireAuthorization(BffSession.UserPolicy);

        return app;
    }
}
