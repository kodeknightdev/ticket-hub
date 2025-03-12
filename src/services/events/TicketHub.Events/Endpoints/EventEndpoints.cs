using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.BuildingBlocks.Security;
using TicketHub.Contracts;
using TicketHub.Events.Data;

namespace TicketHub.Events.Endpoints;

public sealed record CreateEventRequest(
    string Title, string Artist, string Category, string City, Guid? VenueId, string VenueName,
    DateTimeOffset StartsAt, string? CoverImageUrl, string? Description,
    IReadOnlyList<TierSpec> Tiers, IReadOnlyList<SectionSpec> Sections);

public sealed record UpdateEventRequest(string Title, string Artist, string Category, string City, DateTimeOffset StartsAt, string? CoverImageUrl, string? Description);
public sealed record CancelEventRequest(string? Reason);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed class CreateEventValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventValidator(TimeProvider clock)
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Artist).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).NotEmpty().Must(c => Categories.All.Contains(c)).WithMessage("Unknown category");
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.VenueName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StartsAt).GreaterThan(_ => clock.GetUtcNow());
        RuleFor(x => x.Tiers).NotEmpty();
        RuleForEach(x => x.Tiers).ChildRules(t =>
        {
            t.RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
            t.RuleFor(x => x.MinPrice).GreaterThan(0);
            t.RuleFor(x => x).Must(x => x.MinPrice <= x.BasePrice && x.BasePrice <= x.MaxPrice)
                .WithMessage("Base price must sit between min and max");
        });
        RuleFor(x => x.Sections).NotEmpty();
        RuleForEach(x => x.Sections).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            s.RuleFor(x => x.Rows).InclusiveBetween(1, 30);
            s.RuleFor(x => x.SeatsPerRow).InclusiveBetween(1, 40);
        });
        RuleFor(x => x).Must(x => x.Sections.All(s => x.Tiers.Any(t => string.Equals(t.Name, s.Tier, StringComparison.OrdinalIgnoreCase))))
            .WithMessage("Every section must reference a defined tier");
    }
}

public static class Categories
{
    public static readonly string[] All = ["Concert", "Festival", "Comedy", "Theatre", "Sport", "Family"];
}

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/events").WithTags("Events");

        events.MapGet("/", async (string? q, string? city, string? category, DateTimeOffset? from, DateTimeOffset? to,
            int? page, int? pageSize, EventsDbContext db, TimeProvider clock) =>
        {
            var size = Math.Clamp(pageSize ?? 12, 1, 50);
            var number = Math.Max(page ?? 1, 1);
            var query = db.Events.AsNoTracking().Where(e => e.Status == EventStatus.Published && e.StartsAt >= (from ?? clock.GetUtcNow()));

            if (!string.IsNullOrWhiteSpace(city)) query = query.Where(e => e.City == city);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(e => e.Category == category);
            if (to is not null) query = query.Where(e => e.StartsAt <= to);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(e => e.Title.Contains(q) || e.Artist.Contains(q));

            var total = await query.CountAsync();
            var items = await query.OrderBy(e => e.StartsAt)
                .Skip((number - 1) * size).Take(size)
                .Include(e => e.Tiers)
                .Select(e => new { Event = e, Capacity = db.Seats.Count(s => s.EventId == e.Id) })
                .ToListAsync();

            return Results.Ok(new PagedResult<EventSummaryDto>(items.Select(x => x.Event.ToSummary(x.Capacity)).ToList(), number, size, total));
        });

        events.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, EventsDbContext db) =>
        {
            var evt = await Load(db, id);
            if (evt is null || !CanView(evt, ctx)) return Results.NotFound();
            return Results.Ok(evt.ToDetail());
        });

        events.MapGet("/{id:guid}/layout", async (Guid id, EventsDbContext db) =>
        {
            var evt = await Load(db, id);
            return evt is null ? Results.NotFound() : Results.Ok(evt.ToLayout());
        });

        events.MapGet("/mine", async (HttpContext ctx, EventsDbContext db) =>
        {
            var organizerId = ctx.User.UserId();
            var list = await db.Events.AsNoTracking().Include(e => e.Tiers)
                .Where(e => e.OrganizerId == organizerId)
                .OrderByDescending(e => e.StartsAt)
                .Select(e => new { Event = e, Capacity = db.Seats.Count(s => s.EventId == e.Id) })
                .ToListAsync();
            return Results.Ok(list.Select(x => x.Event.ToSummary(x.Capacity)));
        }).RequireAuthorization("organizer");

        events.MapGet("/ids", async (Guid organizerId, EventsDbContext db) =>
            Results.Ok(await db.Events.AsNoTracking().Where(e => e.OrganizerId == organizerId).Select(e => e.Id).ToListAsync()));

        events.MapGet("/summaries", async (string? ids, Guid? organizerId, EventsDbContext db) =>
        {
            var query = db.Events.AsNoTracking().Include(e => e.Tiers).AsQueryable();
            var idList = ParseIds(ids);
            if (idList.Count > 0) query = query.Where(e => idList.Contains(e.Id));
            if (organizerId is not null) query = query.Where(e => e.OrganizerId == organizerId);
            var list = await query.Take(500).Select(e => new { Event = e, Capacity = db.Seats.Count(s => s.EventId == e.Id) }).ToListAsync();
            return Results.Ok(list.Select(x => x.Event.ToSummary(x.Capacity)));
        });

        events.MapGet("/stats", async (string? ids, EventsDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            var query = db.Events.AsNoTracking();
            var idList = ParseIds(ids);
            if (idList.Count > 0) query = query.Where(e => idList.Contains(e.Id));
            return Results.Ok(new
            {
                upcoming = await query.CountAsync(e => e.Status == EventStatus.Published && e.StartsAt > now),
                next30Days = await query.CountAsync(e => e.Status == EventStatus.Published && e.StartsAt > now && e.StartsAt <= now.AddDays(30)),
                drafts = await query.CountAsync(e => e.Status == EventStatus.Draft),
                cancelled = await query.CountAsync(e => e.Status == EventStatus.Cancelled)
            });
        });

        events.MapPost("/", async (CreateEventRequest req, IValidator<CreateEventRequest> validator, HttpContext ctx, EventsDbContext db, TimeProvider clock) =>
        {
            var validation = await validator.ValidateAsync(req);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var venueId = req.VenueId ?? StableId.From($"venue:{req.City}:{req.VenueName}".ToLowerInvariant());
            if (!await db.Venues.AnyAsync(v => v.Id == venueId))
                db.Venues.Add(new Venue { Id = venueId, Name = req.VenueName, City = req.City });

            var evt = new Event
            {
                Id = Guid.NewGuid(),
                OrganizerId = ctx.User.UserId(),
                Title = req.Title.Trim(),
                Artist = req.Artist.Trim(),
                Category = req.Category,
                City = req.City.Trim(),
                VenueId = venueId,
                VenueName = req.VenueName.Trim(),
                StartsAt = req.StartsAt,
                CoverImageUrl = req.CoverImageUrl,
                Description = req.Description,
                Status = EventStatus.Draft,
                CreatedAt = clock.GetUtcNow()
            };
            EventFactory.BuildLayout(evt, req.Tiers, req.Sections);
            db.Events.Add(evt);
            await db.SaveChangesAsync();

            return Results.Created($"/events/{evt.Id}", evt.ToDetail());
        }).RequireAuthorization("organizer");

        events.MapPut("/{id:guid}", async (Guid id, UpdateEventRequest req, HttpContext ctx, EventsDbContext db) =>
        {
            var evt = await Load(db, id, tracking: true);
            if (evt is null) return Results.NotFound();
            if (!IsOwner(evt, ctx)) return Results.Forbid();
            if (evt.Status == EventStatus.Cancelled) return Results.Problem(statusCode: 409, title: "Event is cancelled");

            evt.Title = req.Title;
            evt.Artist = req.Artist;
            evt.Category = req.Category;
            evt.City = req.City;
            evt.StartsAt = req.StartsAt;
            evt.CoverImageUrl = req.CoverImageUrl;
            evt.Description = req.Description;

            if (evt.Status == EventStatus.Published)
                db.Enqueue(Topics.EventUpdated, evt.Id.ToString(),
                    new EventUpdated(evt.Id, evt.Title, evt.Artist, evt.Category, evt.City, evt.StartsAt, evt.CoverImageUrl));

            await db.SaveChangesAsync();
            return Results.Ok(evt.ToDetail());
        }).RequireAuthorization("organizer");

        events.MapPost("/{id:guid}/publish", async (Guid id, HttpContext ctx, EventsDbContext db, TimeProvider clock) =>
        {
            var evt = await Load(db, id, tracking: true);
            if (evt is null) return Results.NotFound();
            if (!IsOwner(evt, ctx)) return Results.Forbid();
            if (evt.Status != EventStatus.Draft) return Results.Problem(statusCode: 409, title: $"Event is {evt.Status}");

            evt.Status = EventStatus.Published;
            evt.PublishedAt = clock.GetUtcNow();
            db.Enqueue(Topics.EventPublished, evt.Id.ToString(), evt.ToPublished());
            await db.SaveChangesAsync();
            return Results.Ok(evt.ToDetail());
        }).RequireAuthorization("organizer");

        events.MapPost("/{id:guid}/cancel", async (Guid id, CancelEventRequest req, HttpContext ctx, EventsDbContext db, TimeProvider clock) =>
        {
            var evt = await Load(db, id, tracking: true);
            if (evt is null) return Results.NotFound();
            if (!IsOwner(evt, ctx)) return Results.Forbid();
            if (evt.Status == EventStatus.Cancelled) return Results.Ok(evt.ToDetail());

            var wasPublished = evt.Status == EventStatus.Published;
            evt.Status = EventStatus.Cancelled;
            evt.CancelledAt = clock.GetUtcNow();
            if (wasPublished)
                db.Enqueue(Topics.EventCancelled, evt.Id.ToString(),
                    new EventCancelled(evt.Id, evt.OrganizerId, evt.Title, req.Reason ?? "Cancelled by organizer", evt.CancelledAt.Value));
            await db.SaveChangesAsync();
            return Results.Ok(evt.ToDetail());
        }).RequireAuthorization("organizer");

        app.MapGet("/venues", async (EventsDbContext db) =>
            Results.Ok(await db.Venues.AsNoTracking().OrderBy(v => v.City).ThenBy(v => v.Name).ToListAsync())).WithTags("Venues");

        app.MapGet("/categories", () => Results.Ok(Categories.All)).WithTags("Venues");

        return app;
    }

    private static Task<Event?> Load(EventsDbContext db, Guid id, bool tracking = false)
    {
        var query = db.Events.Include(e => e.Tiers).Include(e => e.Sections).ThenInclude(s => s.Seats).AsSplitQuery();
        if (!tracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(e => e.Id == id);
    }

    private static bool IsOwner(Event evt, HttpContext ctx) => evt.OrganizerId == ctx.User.UserId() || ctx.User.IsInRole(Roles.Admin);

    private static bool CanView(Event evt, HttpContext ctx) => evt.Status != EventStatus.Draft || IsOwner(evt, ctx);

    internal static List<Guid> ParseIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids) ? [] : ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
}
