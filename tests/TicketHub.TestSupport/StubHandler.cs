using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using TicketHub.Contracts;

namespace TicketHub.TestSupport;

/// <summary>Routes outgoing HTTP calls to canned responses and records what was asked for.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public StubHandler OnGet(string path, object body) =>
        On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase), _ => Json(body));

    public StubHandler OnPost(string path, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        On(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase), respond);

    public StubHandler Fail(string pathPrefix, HttpStatusCode status = HttpStatusCode.ServiceUnavailable) =>
        On(r => r.RequestUri!.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase), _ => new HttpResponseMessage(status));

    public StubHandler On(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Insert(0, (match, respond));
        return this;
    }

    public int Count(string pathPrefix) => Requests.Count(r => r.RequestUri!.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));

    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body is string s ? s : JsonSerializer.Serialize(body, TicketHub.Contracts.Json.Options), Encoding.UTF8, "application/json")
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        foreach (var (match, respond) in _routes)
            if (match(request)) return Task.FromResult(respond(request));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
