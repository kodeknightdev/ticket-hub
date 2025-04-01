using TicketHub.Protos.Seating;

namespace TicketHub.Orders.Clients;

public sealed record HoldSnapshot(Guid HoldId, Guid EventId, Guid BuyerId, IReadOnlyList<HoldSeatSnapshot> Seats, DateTimeOffset HeldAt, DateTimeOffset ExpiresAt, bool Confirmed);
public sealed record HoldSeatSnapshot(Guid SeatId, Guid TierId, Guid SectionId, string Label);

public interface ISeatingClient
{
    Task<HoldSnapshot?> GetHoldAsync(Guid holdId, CancellationToken ct = default);
    Task<(bool Confirmed, string Reason)> ConfirmHoldAsync(Guid holdId, Guid orderId, CancellationToken ct = default);
    Task ReleaseHoldAsync(Guid holdId, string reason, CancellationToken ct = default);
}

public sealed class GrpcSeatingClient(SeatingRpc.SeatingRpcClient client) : ISeatingClient
{
    public async Task<HoldSnapshot?> GetHoldAsync(Guid holdId, CancellationToken ct = default)
    {
        var reply = await client.GetHoldAsync(new HoldRequest { HoldId = holdId.ToString() }, cancellationToken: ct);
        if (!reply.Found) return null;
        return new HoldSnapshot(
            Guid.Parse(reply.HoldId), Guid.Parse(reply.EventId), Guid.Parse(reply.BuyerId),
            reply.Seats.Select(s => new HoldSeatSnapshot(Guid.Parse(s.SeatId), Guid.Parse(s.TierId), Guid.Parse(s.SectionId), s.Label)).ToList(),
            DateTimeOffset.FromUnixTimeMilliseconds(reply.HeldAtUnixMs),
            DateTimeOffset.FromUnixTimeMilliseconds(reply.ExpiresAtUnixMs),
            reply.Confirmed);
    }

    public async Task<(bool Confirmed, string Reason)> ConfirmHoldAsync(Guid holdId, Guid orderId, CancellationToken ct = default)
    {
        var reply = await client.ConfirmHoldAsync(new ConfirmHoldRequest { HoldId = holdId.ToString(), OrderId = orderId.ToString() }, cancellationToken: ct);
        return (reply.Confirmed, reply.Reason);
    }

    public async Task ReleaseHoldAsync(Guid holdId, string reason, CancellationToken ct = default) =>
        await client.ReleaseHoldAsync(new ReleaseHoldRequest { HoldId = holdId.ToString(), Reason = reason }, cancellationToken: ct);
}
