namespace TicketHub.Seating.Layout;

public sealed record LayoutTier(Guid Id, string Name, decimal BasePrice, decimal MinPrice, decimal MaxPrice, decimal CurrentPrice);
public sealed record LayoutSeat(Guid Id, string Row, int Number, Guid TierId);
public sealed record LayoutSection(Guid Id, string Name, int SortOrder, IReadOnlyList<LayoutSeat> Seats);
public sealed record EventLayout(Guid EventId, int Capacity, IReadOnlyList<LayoutSection> Sections, IReadOnlyList<LayoutTier> Tiers)
{
    public IEnumerable<(LayoutSection Section, LayoutSeat Seat)> AllSeats() =>
        Sections.SelectMany(s => s.Seats.Select(seat => (s, seat)));
}
