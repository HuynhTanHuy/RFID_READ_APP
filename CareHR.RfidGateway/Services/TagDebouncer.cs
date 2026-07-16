namespace CareHR.RfidGateway.Services;

public sealed class TagDebouncer
{
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool ShouldAccept(string epc, int debounceSeconds)
    {
        if (string.IsNullOrWhiteSpace(epc))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromSeconds(Math.Max(0, debounceSeconds));

        lock (_gate)
        {
            if (_lastSeen.TryGetValue(epc, out var previous) && now - previous < window)
            {
                return false;
            }

            _lastSeen[epc] = now;

            if (_lastSeen.Count > 5000)
            {
                var staleBefore = now - window;
                foreach (var key in _lastSeen.Where(x => x.Value < staleBefore).Select(x => x.Key).ToList())
                {
                    _lastSeen.Remove(key);
                }
            }

            return true;
        }
    }
}
