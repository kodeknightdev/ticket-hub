using System.Net;
using System.Net.Http.Json;
using TicketHub.Contracts;
using TicketHub.Protos.Seating;

namespace TicketHub.TicketingBff.Clients;

public sealed record TierDto(Guid Id, string Name, decimal BasePrice, decimal MinPrice, decimal MaxPrice, decimal CurrentPrice);
public sealed record SeatDto(Guid Id, string Row, int Number, Guid TierId);
public sealed record SectionDto(Guid Id, string Name, int SortOrder, IReadOnlyList<SeatDto> Seats);
public sealed record LayoutDto(Guid EventId, int Capacity, IReadOnlyList<SectionDto> Sections, IReadOnlyList<TierDto> Tiers);
public sealed record EventDetailDto(
    Guid Id, Guid OrganizerId, string Title, string Artist, string Category, string City, Guid VenueId, string VenueName,
    DateTimeOffset StartsAt, string? CoverImageUrl, string? Description, string Status, int Capacity,
    DateTimeOffset? PublishedAt, IReadOnlyList<TierDto> Tiers);
public sealed record EventSummaryDto(Guid Id, string Title, string Artist, string City, string VenueName, DateTimeOffset StartsAt, string? CoverImageUrl, string Status);

public sealed record TierPriceDto(string TierId, string Name, decimal Price, decimal BasePrice);
public sealed record EventPricesDto(string EventId, IReadOnlyList<TierPriceDto> Tiers);
public sealed record LockedPriceDto(string SeatId, string TierId, decimal Price);
public sealed record LockResponseDto(string HoldId, IReadOnlyList<LockedPriceDto> Prices);

public sealed record QuoteLineDto(Guid SeatId, Guid TierId, string Label, decimal Price);
public sealed record PaymentDto(string Provider, string? PaymentRef, string? ClientSecret, string? PublishableKey);
public sealed record OrderDto(Guid Id, Guid EventId, Guid HoldId, string Status, string? StatusReason, decimal Subtotal, decimal Fees, decimal Total,
    string Currency, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? PaidAt, PaymentDto? Payment);
public sealed record QuoteDto(Guid HoldId, Guid EventId, IReadOnlyList<QuoteLineDto> Lines, decimal Subtotal, decimal Fees, decimal Total, string Currency, OrderDto? ExistingOrder);
public sealed record TicketDto(Guid Id, Guid OrderId, Guid EventId, Guid SeatId, string Label, string Code, string Status, DateTimeOffset IssuedAt, DateTimeOffset? CheckedInAt);

public sealed class EventsApi(HttpClient http)
{
    public const string Name = "events";

    public Task<EventDetailDto?> GetEventAsync(Guid id, CancellationToken ct) => GetOrNull<EventDetailDto>($"/events/{id}", ct);
    public Task<LayoutDto?> GetLayoutAsync(Guid id, CancellationToken ct) => GetOrNull<LayoutDto>($"/events/{id}/layout", ct);

    public async Task<IReadOnlyList<EventSummaryDto>> GetSummariesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = string.Join(',', ids.Distinct());
        if (list.Length == 0) return [];
        return await http.GetFromJsonAsync<List<EventSummaryDto>>($"/events/summaries?ids={list}", Json.Options, ct) ?? [];
    }

    private async Task<T?> GetOrNull<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json.Options, ct);
    }
}

public sealed class PricingApi(HttpClient http)
{
    public const string Name = "pricing";

    public Task<EventPricesDto?> GetPricesAsync(Guid eventId, CancellationToken ct) =>
        http.GetFromJsonAsync<EventPricesDto>($"/prices/{eventId}", Json.Options, ct);

    public async Task<LockResponseDto?> GetLocksAsync(Guid holdId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/prices/locks/{holdId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LockResponseDto>(Json.Options, ct);
    }
}

public sealed class OrdersApi(HttpClient http)
{
    public const string Name = "orders";

    public async Task<QuoteDto?> GetQuoteAsync(Guid holdId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/orders/quote?holdId={holdId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<QuoteDto>(Json.Options, ct);
    }

    public async Task<IReadOnlyList<TicketDto>> GetMyTicketsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<TicketDto>>("/tickets/mine", Json.Options, ct) ?? [];
}

public sealed record SeatAvailability(Guid SeatId, string State);
public sealed record Availability(int Capacity, int Available, int Held, int Sold, IReadOnlyDictionary<Guid, string> Seats);
public sealed record HoldView(Guid HoldId, Guid EventId, Guid BuyerId, IReadOnlyList<HeldSeatView> Seats, DateTimeOffset ExpiresAt, bool Confirmed);
public sealed record HeldSeatView(Guid SeatId, Guid TierId, string Label);

public interface ISeatingReader
{
    Task<Availability?> GetAvailabilityAsync(Guid eventId, CancellationToken ct);
    Task<HoldView?> GetHoldAsync(Guid holdId, CancellationToken ct);
}

public sealed class GrpcSeatingReader(SeatingRpc.SeatingRpcClient client) : ISeatingReader
{
    public async Task<Availability?> GetAvailabilityAsync(Guid eventId, CancellationToken ct)
    {
        var reply = await client.GetAvailabilityAsync(new AvailabilityRequest { EventId = eventId.ToString(), IncludeSeats = true }, cancellationToken: ct);
        return new Availability(reply.Capacity, reply.Available, reply.Held, reply.Sold,
            reply.Seats.ToDictionary(s => Guid.Parse(s.SeatId), s => s.State));
    }

    public async Task<HoldView?> GetHoldAsync(Guid holdId, CancellationToken ct)
    {
        var reply = await client.GetHoldAsync(new HoldRequest { HoldId = holdId.ToString() }, cancellationToken: ct);
        if (!reply.Found) return null;
        return new HoldView(Guid.Parse(reply.HoldId), Guid.Parse(reply.EventId), Guid.Parse(reply.BuyerId),
            reply.Seats.Select(s => new HeldSeatView(Guid.Parse(s.SeatId), Guid.Parse(s.TierId), s.Label)).ToList(),
            DateTimeOffset.FromUnixTimeMilliseconds(reply.ExpiresAtUnixMs), reply.Confirmed);
    }
}
