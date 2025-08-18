using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TicketHub.Contracts;

// Simulates an on-sale rush: N buyers register, then all try to grab overlapping seats for the same event.
// Talks to identity, seating and orders directly so the BFF's per-user rate limit doesn't hide contention.

var opts = Options.Parse(args);
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
Console.WriteLine($"load-sim: {opts.Buyers} buyers, {opts.SeatsPerBuyer} seats each, pay ratio {opts.PayRatio:P0}");

var eventId = opts.EventId ?? await FirstEventAsync(http, opts);
var layout = await http.GetFromJsonAsync<JsonElement>($"{opts.Events}/events/{eventId}/layout", Json.Options);
var seatIds = layout.GetProperty("sections").EnumerateArray()
    .SelectMany(s => s.GetProperty("seats").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()))
    .ToList();
var hotSeats = seatIds.Take(Math.Min(seatIds.Count, Math.Max(opts.SeatsPerBuyer * 4, opts.Buyers))).ToList();
Console.WriteLine($"event {eventId}: {seatIds.Count} seats, buyers compete for the first {hotSeats.Count}");

var tokens = await Task.WhenAll(Enumerable.Range(0, opts.Buyers).Select(i => RegisterAsync(http, opts, i)));

var stats = new ConcurrentDictionary<string, int>();
var won = new ConcurrentBag<(Guid HoldId, Guid[] Seats)>();
var paidSeats = new ConcurrentBag<Guid>();
var latencies = new ConcurrentBag<double>();
var random = new Random(opts.Seed);
var picks = tokens.Select(_ => hotSeats.OrderBy(_ => random.Next()).Take(opts.SeatsPerBuyer).ToArray()).ToList();
var payDecisions = tokens.Select(_ => random.NextDouble() < opts.PayRatio).ToList();

var clock = Stopwatch.StartNew();
await Parallel.ForEachAsync(Enumerable.Range(0, tokens.Length), new ParallelOptions { MaxDegreeOfParallelism = opts.Concurrency }, async (i, ct) =>
{
    var started = Stopwatch.GetTimestamp();
    using var hold = new HttpRequestMessage(HttpMethod.Post, $"{opts.Seating}/seating/holds")
    {
        Content = JsonContent.Create(new { eventId, seatIds = picks[i] }, options: Json.Options)
    };
    hold.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[i]);
    using var response = await http.SendAsync(hold, ct);
    latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    if (response.StatusCode == HttpStatusCode.Conflict) { stats.AddOrUpdate("hold-conflict", 1, (_, n) => n + 1); return; }
    if (!response.IsSuccessStatusCode) { stats.AddOrUpdate($"hold-{(int)response.StatusCode}", 1, (_, n) => n + 1); return; }
    stats.AddOrUpdate("hold-ok", 1, (_, n) => n + 1);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json.Options, ct);
    var holdId = body.GetProperty("holdId").GetGuid();
    won.Add((holdId, picks[i]));
    if (!payDecisions[i]) return;

    var order = await PostAsync(http, $"{opts.Orders}/orders", tokens[i], new { holdId }, Guid.NewGuid().ToString(), ct);
    if (order is null) { stats.AddOrUpdate("order-failed", 1, (_, n) => n + 1); return; }
    var orderId = order.Value.GetProperty("id").GetGuid();
    var paid = await PostAsync(http, $"{opts.Orders}/orders/{orderId}/mock-pay", tokens[i], new { succeed = true }, null, ct);
    if (paid?.GetProperty("status").GetString() == "Paid")
    {
        stats.AddOrUpdate("paid", 1, (_, n) => n + 1);
        foreach (var seat in picks[i]) paidSeats.Add(seat);
    }
    else stats.AddOrUpdate("pay-failed", 1, (_, n) => n + 1);
});
clock.Stop();

var heldSeats = won.SelectMany(w => w.Seats).ToList();
Console.WriteLine();
Console.WriteLine($"finished in {clock.Elapsed.TotalSeconds:0.0}s");
foreach (var (key, value) in stats.OrderBy(k => k.Key)) Console.WriteLine($"  {key,-14} {value}");
var sorted = latencies.OrderBy(x => x).ToList();
if (sorted.Count > 0)
    Console.WriteLine($"  hold latency p50 {sorted[sorted.Count / 2]:0}ms, p95 {sorted[(int)(sorted.Count * 0.95)]:0}ms");

var duplicateHolds = heldSeats.Count - heldSeats.Distinct().Count();
Console.WriteLine($"  seats won by more than one hold: {duplicateHolds}");

// Give Kafka a moment to carry order-paid to seating before checking the seat map.
await Task.Delay(TimeSpan.FromSeconds(opts.SettleSeconds));
var availability = await http.GetFromJsonAsync<JsonElement>($"{opts.Seating}/seating/events/{eventId}/availability", Json.Options);
var soldNow = availability.GetProperty("seats").EnumerateArray().Where(s => s.GetProperty("state").GetString() == "sold").Select(s => s.GetProperty("seatId").GetGuid()).ToHashSet();
var missing = paidSeats.Count(s => !soldNow.Contains(s));
Console.WriteLine($"  paid seats: {paidSeats.Count} (distinct {paidSeats.Distinct().Count()}), marked sold: {paidSeats.Count(soldNow.Contains)}, not yet sold: {missing}");

var ok = duplicateHolds == 0 && paidSeats.Count == paidSeats.Distinct().Count();
Console.WriteLine(ok ? "RESULT: no seat was sold twice" : "RESULT: DOUBLE SALE DETECTED");
return ok ? 0 : 1;

static async Task<Guid> FirstEventAsync(HttpClient http, Options opts)
{
    var page = await http.GetFromJsonAsync<JsonElement>($"{opts.Events}/events?pageSize=1", Json.Options);
    return page.GetProperty("items")[0].GetProperty("id").GetGuid();
}

static async Task<string> RegisterAsync(HttpClient http, Options opts, int i)
{
    var email = $"loadsim-{opts.RunId}-{i}@tickethub.local";
    var response = await http.PostAsJsonAsync($"{opts.Identity}/auth/register", new { email, password = "LoadSim!2025", displayName = $"Load Sim {i}" }, Json.Options);
    if (response.StatusCode == HttpStatusCode.Conflict)
        response = await http.PostAsJsonAsync($"{opts.Identity}/auth/login", new { email, password = "LoadSim!2025" }, Json.Options);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<JsonElement>(Json.Options)).GetProperty("accessToken").GetString()!;
}

static async Task<JsonElement?> PostAsync(HttpClient http, string url, string token, object body, string? idempotencyKey, CancellationToken ct)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json.Options) };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    if (idempotencyKey is not null) request.Headers.Add(WellKnownHeaders.IdempotencyKey, idempotencyKey);
    using var response = await http.SendAsync(request, ct);
    return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<JsonElement>(Json.Options, ct) : null;
}

sealed record Options(
    string Identity, string Events, string Seating, string Orders, Guid? EventId,
    int Buyers, int SeatsPerBuyer, int Concurrency, double PayRatio, int Seed, int SettleSeconds, string RunId)
{
    public static Options Parse(string[] args)
    {
        var map = args.Select(a => a.Split('=', 2)).Where(p => p.Length == 2 && p[0].StartsWith("--"))
            .ToDictionary(p => p[0][2..], p => p[1], StringComparer.OrdinalIgnoreCase);
        string Get(string key, string env, string fallback) => map.GetValueOrDefault(key) ?? Environment.GetEnvironmentVariable(env) ?? fallback;
        return new Options(
            Get("identity", "LOADSIM_IDENTITY", "http://localhost:5201"),
            Get("events", "LOADSIM_EVENTS", "http://localhost:5202"),
            Get("seating", "LOADSIM_SEATING", "http://localhost:5203"),
            Get("orders", "LOADSIM_ORDERS", "http://localhost:5204"),
            Guid.TryParse(Get("event", "LOADSIM_EVENT", ""), out var e) ? e : null,
            int.Parse(Get("buyers", "LOADSIM_BUYERS", "100")),
            int.Parse(Get("seats", "LOADSIM_SEATS", "2")),
            int.Parse(Get("concurrency", "LOADSIM_CONCURRENCY", "50")),
            double.Parse(Get("pay", "LOADSIM_PAY_RATIO", "0.8"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("seed", "LOADSIM_SEED", "7")),
            int.Parse(Get("settle", "LOADSIM_SETTLE_SECONDS", "5")),
            Get("run", "LOADSIM_RUN", DateTime.UtcNow.ToString("HHmmss")));
    }
}
