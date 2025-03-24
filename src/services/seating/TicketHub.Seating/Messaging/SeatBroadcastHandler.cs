using Microsoft.AspNetCore.SignalR;
using TicketHub.BuildingBlocks.Messaging;
using TicketHub.Contracts;
using TicketHub.Seating.Holds;
using TicketHub.Seating.Hubs;

namespace TicketHub.Seating.Messaging;

/// <summary>
/// Runs in its own consumer group. With the Redis backplane any one replica can push to every
/// connected client, so a plain consumer group is enough for fan-out.
/// </summary>
public sealed class SeatBroadcastHandler(IHubContext<SeatingHub, ISeatingClient> hub) : IIntegrationEventHandler
{
    public Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        switch (envelope.Type)
        {
            case Topics.SeatHeld:
            {
                var e = envelope.As<SeatHeld>();
                return Send(e.EventId, e.Seats.Select(s => s.SeatId).ToList(), SeatStates.Held);
            }
            case Topics.SeatReleased:
            {
                var e = envelope.As<SeatReleased>();
                return Send(e.EventId, e.SeatIds, SeatStates.Available);
            }
            case Topics.HoldExpired:
            {
                var e = envelope.As<HoldExpired>();
                return Send(e.EventId, e.SeatIds, SeatStates.Available);
            }
            case Topics.SeatsSold:
            {
                var e = envelope.As<SeatsSold>();
                return Send(e.EventId, e.SeatIds, SeatStates.Sold);
            }
            case Topics.PriceUpdated:
            {
                var e = envelope.As<PriceUpdated>();
                return hub.Clients.Group(SeatingHub.GroupFor(e.EventId)).PriceChanged(new PriceChanged(e.EventId, e.TierId, e.NewPrice));
            }
            default:
                return Task.CompletedTask;
        }
    }

    private Task Send(Guid eventId, IReadOnlyList<Guid> seatIds, string state) =>
        hub.Clients.Group(SeatingHub.GroupFor(eventId)).SeatsChanged(new SeatsChanged(eventId, seatIds, state));
}
