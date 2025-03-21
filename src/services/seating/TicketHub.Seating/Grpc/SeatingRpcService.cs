using Grpc.Core;
using TicketHub.Protos.Seating;
using TicketHub.Seating.Holds;
using HeldSeatProto = TicketHub.Protos.Seating.HeldSeat;

namespace TicketHub.Seating.Grpc;

public sealed class SeatingRpcService(HoldService holds, HoldStore store, ILogger<SeatingRpcService> logger) : SeatingRpc.SeatingRpcBase
{
    public override async Task<AvailabilityReply> GetAvailability(AvailabilityRequest request, ServerCallContext context)
    {
        var snapshot = await holds.AvailabilityAsync(ParseId(request.EventId), context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Unknown event"));

        var reply = new AvailabilityReply
        {
            EventId = request.EventId,
            Capacity = snapshot.Capacity,
            Available = snapshot.Available,
            Held = snapshot.Held,
            Sold = snapshot.Sold
        };
        if (request.IncludeSeats)
            reply.Seats.AddRange(snapshot.Seats.Select(s => new SeatState { SeatId = s.SeatId.ToString(), State = s.State }));
        return reply;
    }

    public override async Task<HoldReply> GetHold(HoldRequest request, ServerCallContext context)
    {
        var hold = await store.GetAsync(ParseId(request.HoldId));
        if (hold is null) return new HoldReply { Found = false, HoldId = request.HoldId };

        var reply = new HoldReply
        {
            Found = true,
            HoldId = hold.HoldId.ToString(),
            EventId = hold.EventId.ToString(),
            BuyerId = hold.BuyerId.ToString(),
            HeldAtUnixMs = hold.HeldAt.ToUnixTimeMilliseconds(),
            ExpiresAtUnixMs = hold.ExpiresAt.ToUnixTimeMilliseconds(),
            Confirmed = hold.ConfirmedOrderId is not null
        };
        reply.Seats.AddRange(hold.Seats.Select(s => new HeldSeatProto
        {
            SeatId = s.SeatId.ToString(),
            TierId = s.TierId.ToString(),
            SectionId = s.SectionId.ToString(),
            Label = s.Label
        }));
        return reply;
    }

    public override async Task<ConfirmHoldReply> ConfirmHold(ConfirmHoldRequest request, ServerCallContext context)
    {
        var hold = await store.GetAsync(ParseId(request.HoldId));
        if (hold is null) return new ConfirmHoldReply { Confirmed = false, Reason = "hold-expired" };

        var orderId = ParseId(request.OrderId);
        if (hold.ConfirmedOrderId == orderId) return new ConfirmHoldReply { Confirmed = true };
        if (hold.ConfirmedOrderId is not null) return new ConfirmHoldReply { Confirmed = false, Reason = "confirmed-by-another-order" };

        var ok = await store.ConfirmAsync(hold, orderId);
        logger.LogInformation("Confirm hold {HoldId} for order {OrderId}: {Result}", hold.HoldId, orderId, ok);
        return new ConfirmHoldReply { Confirmed = ok, Reason = ok ? "" : "hold-expired" };
    }

    public override async Task<ReleaseHoldReply> ReleaseHold(ReleaseHoldRequest request, ServerCallContext context)
    {
        var hold = await store.GetAsync(ParseId(request.HoldId));
        if (hold is null) return new ReleaseHoldReply { Released = false };
        var released = await holds.ReleaseAsync(hold, string.IsNullOrWhiteSpace(request.Reason) ? "released" : request.Reason, context.CancellationToken);
        return new ReleaseHoldReply { Released = released };
    }

    public override async Task<SoldCountsReply> GetSoldCounts(SoldCountsRequest request, ServerCallContext context)
    {
        var reply = new SoldCountsReply();
        foreach (var raw in request.EventIds.Take(200))
        {
            if (!Guid.TryParse(raw, out var eventId)) continue;
            await holds.EnsureSoldLoadedAsync(eventId, context.CancellationToken);
            var sold = await store.SoldCountAsync(eventId);
            var held = (await store.HoldsForEventAsync(eventId)).Count;
            reply.Counts.Add(new SoldCount { EventId = raw, Sold = (int)sold, Held = held });
        }
        return reply;
    }

    private static Guid ParseId(string value) =>
        Guid.TryParse(value, out var id) ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid id '{value}'"));
}
