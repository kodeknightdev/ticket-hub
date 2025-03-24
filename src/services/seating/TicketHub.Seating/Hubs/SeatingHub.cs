using Microsoft.AspNetCore.SignalR;

namespace TicketHub.Seating.Hubs;

public sealed record SeatsChanged(Guid EventId, IReadOnlyList<Guid> SeatIds, string State);
public sealed record PriceChanged(Guid EventId, Guid TierId, decimal Price);

public interface ISeatingClient
{
    Task SeatsChanged(SeatsChanged change);
    Task PriceChanged(PriceChanged change);
}

/// <summary>Viewers join a group per event; anonymous browsing is allowed.</summary>
public sealed class SeatingHub : Hub<ISeatingClient>
{
    public Task JoinEvent(Guid eventId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(eventId));

    public Task LeaveEvent(Guid eventId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(eventId));

    public static string GroupFor(Guid eventId) => $"event:{eventId}";
}
