namespace TicketHub.BffKit;

/// <summary>
/// Runs a downstream call for an aggregation endpoint. Non-critical failures are recorded so the
/// response can be returned with partial = true instead of failing outright.
/// </summary>
public sealed class Fanout
{
    private readonly List<string> _failures = [];

    public IReadOnlyList<string> Failures => _failures;
    public bool Partial => _failures.Count > 0;

    public async Task<T?> Optional<T>(string source, Func<Task<T?>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_failures) _failures.Add($"{source}: {ex.GetType().Name}");
            return default;
        }
    }
}
