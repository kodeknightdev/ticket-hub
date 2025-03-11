namespace TicketHub.Events.Data;

public sealed record TierSpec(string Name, decimal BasePrice, decimal MinPrice, decimal MaxPrice);
public sealed record SectionSpec(string Name, int Rows, int SeatsPerRow, string Tier);

public static class EventFactory
{
    public static void BuildLayout(Event evt, IEnumerable<TierSpec> tiers, IEnumerable<SectionSpec> sections, Func<string, Guid>? ids = null)
    {
        ids ??= _ => Guid.NewGuid();
        evt.Tiers = tiers.Select(t => new PriceTier
        {
            Id = ids($"tier:{t.Name}"),
            EventId = evt.Id,
            Name = t.Name,
            BasePrice = t.BasePrice,
            MinPrice = t.MinPrice,
            MaxPrice = t.MaxPrice,
            CurrentPrice = t.BasePrice
        }).ToList();

        var order = 0;
        evt.Sections = sections.Select(spec =>
        {
            var tier = evt.Tiers.First(t => string.Equals(t.Name, spec.Tier, StringComparison.OrdinalIgnoreCase));
            var section = new Section { Id = ids($"section:{spec.Name}"), EventId = evt.Id, Name = spec.Name, SortOrder = order++ };
            for (var r = 0; r < spec.Rows; r++)
            {
                var row = RowLabel(r);
                for (var n = 1; n <= spec.SeatsPerRow; n++)
                    section.Seats.Add(new Seat { Id = ids($"seat:{spec.Name}:{row}:{n}"), SectionId = section.Id, EventId = evt.Id, Row = row, Number = n, TierId = tier.Id });
            }
            return section;
        }).ToList();
    }

    public static string RowLabel(int index) => index < 26 ? ((char)('A' + index)).ToString() : $"A{(char)('A' + index - 26)}";
}
