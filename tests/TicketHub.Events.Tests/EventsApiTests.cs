using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TicketHub.BuildingBlocks.Persistence;
using TicketHub.Contracts;
using TicketHub.Events.Data;
using TicketHub.TestSupport;

namespace TicketHub.Events.Tests;

public sealed class EventsApiTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly WebApplicationFactory<Program> _factory;

    public EventsApiTests()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Jwt:SigningKey", JwtFactory.Key);
            b.UseSetting("Kafka:Enabled", "false");
            b.ConfigureServices(s => s.UseSqliteFor<EventsDbContext>(_connection));
        });
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<EventsDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    private HttpClient As(Guid userId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", JwtFactory.Create(userId, role));
        return client;
    }

    private static object NewEvent(DateTimeOffset startsAt) => new
    {
        title = "Test Night", artist = "The Testers", category = "Concert", city = "Leeds", venueName = "Test Hall",
        startsAt, coverImageUrl = (string?)null, description = "x",
        tiers = new[] { new { name = "Standard", basePrice = 40m, minPrice = 30m, maxPrice = 70m } },
        sections = new[] { new { name = "Floor", rows = 3, seatsPerRow = 5, tier = "Standard" } }
    };

    [Fact]
    public async Task Organizer_creates_draft_then_publishes_through_the_outbox()
    {
        var organizer = Guid.NewGuid();
        var client = As(organizer, Roles.Organizer);

        var created = await client.PostAsJsonAsync("/events", NewEvent(DateTimeOffset.UtcNow.AddDays(20)));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await _factory.CreateClient().GetAsync($"/events/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync($"/events/{id}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var layout = await _factory.CreateClient().GetFromJsonAsync<JsonElement>($"/events/{id}/layout");
        layout.GetProperty("capacity").GetInt32().ShouldBe(15);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
        var message = db.Set<OutboxMessage>().Single(m => m.Topic == Topics.EventPublished);
        var published = JsonSerializer.Deserialize<EventEnvelope>(message.Payload, Json.Options)!.As<EventPublished>();
        published.Capacity.ShouldBe(15);
        published.OrganizerId.ShouldBe(organizer);
        published.Tiers.ShouldHaveSingleItem().Capacity.ShouldBe(15);
    }

    [Fact]
    public async Task Buyers_cannot_create_events_and_validation_rejects_bad_input()
    {
        (await As(Guid.NewGuid(), Roles.Buyer).PostAsJsonAsync("/events", NewEvent(DateTimeOffset.UtcNow.AddDays(5))))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await As(Guid.NewGuid(), Roles.Organizer).PostAsJsonAsync("/events", NewEvent(DateTimeOffset.UtcNow.AddDays(-1))))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Another_organizer_cannot_publish_or_cancel()
    {
        var client = As(Guid.NewGuid(), Roles.Organizer);
        var created = await client.PostAsJsonAsync("/events", NewEvent(DateTimeOffset.UtcNow.AddDays(20)));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var intruder = As(Guid.NewGuid(), Roles.Organizer);
        (await intruder.PostAsync($"/events/{id}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_filters_by_city_and_paginates()
    {
        var client = As(Guid.NewGuid(), Roles.Organizer);
        for (var i = 0; i < 3; i++)
        {
            var created = await client.PostAsJsonAsync("/events", NewEvent(DateTimeOffset.UtcNow.AddDays(10 + i)));
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await client.PostAsync($"/events/{id}/publish", null);
        }

        var page = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/events?city=Leeds&pageSize=2&page=1");
        page.GetProperty("total").GetInt32().ShouldBe(3);
        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        (await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/events?city=Bristol")).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public void Layout_builder_labels_rows_and_assigns_tiers()
    {
        var evt = new Event { Id = Guid.NewGuid(), Title = "t", Artist = "a", Category = "Concert", City = "c", VenueName = "v" };
        EventFactory.BuildLayout(evt, [new TierSpec("Front", 80, 60, 120), new TierSpec("Back", 40, 30, 60)],
            [new SectionSpec("Front stalls", 2, 3, "Front"), new SectionSpec("Rear", 28, 2, "back")]);

        evt.Capacity.ShouldBe(6 + 56);
        evt.Sections[1].Seats.Select(s => s.Row).Distinct().Last().ShouldBe("AB");
        evt.Sections[1].Seats.ShouldAllBe(s => s.TierId == evt.Tiers[1].Id);
    }
}
