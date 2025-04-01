using TicketHub.Contracts;

namespace TicketHub.Orders.Clients;

public sealed record LockSeat(Guid SeatId, Guid TierId);
public sealed record LockRequest(Guid HoldId, Guid EventId, DateTimeOffset HeldAt, IReadOnlyList<LockSeat> Seats);
public sealed record LockedPrice(Guid SeatId, Guid TierId, decimal Price);
public sealed record LockResponse(Guid HoldId, IReadOnlyList<LockedPrice> Prices);

public interface IPricingClient
{
    Task<IReadOnlyList<LockedPrice>> LockPricesAsync(LockRequest request, CancellationToken ct = default);
}

public sealed class PricingClient(HttpClient http) : IPricingClient
{
    public async Task<IReadOnlyList<LockedPrice>> LockPricesAsync(LockRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("/prices/locks", request, Json.Options, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LockResponse>(Json.Options, ct);
        return body?.Prices ?? [];
    }
}
