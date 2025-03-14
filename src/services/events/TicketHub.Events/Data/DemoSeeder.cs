using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;

namespace TicketHub.Events.Data;

public static class DemoSeeder
{
    private sealed record DemoEvent(string Key, Guid Organizer, string Title, string Artist, string Category, int VenueIndex, int DaysAhead, int Hour, string Cover);

    private static readonly (string Name, string City)[] Venues =
    [
        ("Riverside Arena", "Manchester"),
        ("The Lantern Hall", "Leeds"),
        ("Harbour Bowl", "Bristol"),
        ("Northern Stage", "Glasgow"),
        ("Old Mill Theatre", "London")
    ];

    private static readonly DemoEvent[] DemoEvents =
    [
        new("neon-harbour", DemoAccounts.Organizer, "Neon Harbour Live", "Neon Harbour", "Concert", 0, 12, 20, "https://picsum.photos/seed/neon/800/400"),
        new("velvet-static", DemoAccounts.Organizer, "Velvet Static: Late Shift Tour", "Velvet Static", "Concert", 1, 26, 19, "https://picsum.photos/seed/velvet/800/400"),
        new("laugh-lines", DemoAccounts.Organizer, "Laugh Lines Comedy Night", "Mira Patel", "Comedy", 4, 9, 20, "https://picsum.photos/seed/laugh/800/400"),
        new("summer-tides", DemoAccounts.Organizer, "Summer Tides Festival Day", "Various Artists", "Festival", 2, 58, 13, "https://picsum.photos/seed/tides/800/400"),
        new("glasgow-strings", DemoAccounts.SecondOrganizer, "Glasgow Strings Quartet", "Arden Quartet", "Theatre", 3, 33, 19, "https://picsum.photos/seed/strings/800/400"),
        new("cup-final", DemoAccounts.SecondOrganizer, "City Cup Final", "Riverside FC vs Northside", "Sport", 0, 44, 15, "https://picsum.photos/seed/cup/800/400"),
        new("puppet-parade", DemoAccounts.SecondOrganizer, "The Great Puppet Parade", "Paper Moon Company", "Family", 4, 19, 11, "https://picsum.photos/seed/puppet/800/400"),
        new("echo-valley", DemoAccounts.Organizer, "Echo Valley Unplugged", "Echo Valley", "Concert", 3, 75, 20, "https://picsum.photos/seed/echo/800/400")
    ];

    public static async Task SeedAsync(EventsDbContext db, IServiceProvider services)
    {
        if (await db.Events.AnyAsync()) return;

        foreach (var (name, city) in Venues)
            db.Venues.Add(new Venue { Id = StableId.From($"venue:{city}:{name}".ToLowerInvariant()), Name = name, City = city });

        var today = DateTimeOffset.UtcNow.Date;
        foreach (var demo in DemoEvents)
        {
            var (venueName, city) = Venues[demo.VenueIndex];
            var evt = new Event
            {
                Id = StableId.From($"event:{demo.Key}"),
                OrganizerId = demo.Organizer,
                Title = demo.Title,
                Artist = demo.Artist,
                Category = demo.Category,
                City = city,
                VenueId = StableId.From($"venue:{city}:{venueName}".ToLowerInvariant()),
                VenueName = venueName,
                StartsAt = new DateTimeOffset(today.AddDays(demo.DaysAhead).AddHours(demo.Hour), TimeSpan.Zero),
                CoverImageUrl = demo.Cover,
                Description = $"{demo.Artist} live at {venueName}, {city}.",
                Status = EventStatus.Published,
                CreatedAt = DateTimeOffset.UtcNow,
                PublishedAt = DateTimeOffset.UtcNow
            };

            EventFactory.BuildLayout(evt,
                [
                    new TierSpec("Front", 89m, 69m, 149m),
                    new TierSpec("Standard", 55m, 39m, 95m),
                    new TierSpec("Balcony", 35m, 25m, 60m)
                ],
                [
                    new SectionSpec("Floor A", 6, 14, "Front"),
                    new SectionSpec("Floor B", 8, 16, "Standard"),
                    new SectionSpec("Balcony", 5, 18, "Balcony")
                ],
                key => StableId.From($"event:{demo.Key}:{key}"));

            db.Events.Add(evt);
            db.Enqueue(Topics.EventPublished, evt.Id.ToString(), evt.ToPublished());
        }

        await db.SaveChangesAsync();
    }
}
