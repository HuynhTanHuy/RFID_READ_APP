namespace CareHR.RfidGateway.Services;

public sealed class TagDebouncer
{
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PresenceEntry> _presence = new(StringComparer.OrdinalIgnoreCase);
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

    public void UpdateLastSeen(string epc, int presenceTimeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(epc))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, presenceTimeoutSeconds));

        lock (_gate)
        {
            if (!_presence.TryGetValue(epc, out var entry))
            {
                entry = new PresenceEntry { LastSeen = now, SessionStart = now };
            }
            else
            {
                if (now - entry.LastSeen > timeout)
                {
                    entry.SessionStart = now;
                }

                entry.LastSeen = now;
            }

            _presence[epc] = entry;
            TrimPresenceIfNeeded(now, timeout);
        }
    }

    /// <summary>
    /// Returns true when EPC is newly present (caller should POST). Does not update LastSeen.
    /// </summary>
    public bool ShouldSendPresence(string epc, int presenceTimeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(epc))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, presenceTimeoutSeconds));

        lock (_gate)
        {
            if (!_presence.TryGetValue(epc, out var entry))
            {
                return true;
            }

            if (now - entry.LastSeen > timeout)
            {
                return true;
            }

            if (entry.ApiSentAt is not null && entry.ApiSentAt >= entry.SessionStart)
            {
                return false;
            }

            entry.ApiSentAt = entry.SessionStart;
            _presence[epc] = entry;
            return true;
        }
    }

    public void RemoveExpiredPresence(int presenceTimeoutSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, presenceTimeoutSeconds));

        lock (_gate)
        {
            foreach (var key in _presence
                         .Where(x => now - x.Value.LastSeen > timeout)
                         .Select(x => x.Key)
                         .ToList())
            {
                _presence.Remove(key);
            }
        }
    }

    public void ClearPresence()
    {
        lock (_gate)
        {
            _presence.Clear();
        }
    }

    private void TrimPresenceIfNeeded(DateTimeOffset now, TimeSpan timeout)
    {
        if (_presence.Count <= 5000)
        {
            return;
        }

        var staleBefore = now - timeout;
        foreach (var key in _presence.Where(x => x.Value.LastSeen < staleBefore).Select(x => x.Key).ToList())
        {
            _presence.Remove(key);
        }
    }

    private sealed class PresenceEntry
    {
        public DateTimeOffset LastSeen;
        public DateTimeOffset SessionStart;
        public DateTimeOffset? ApiSentAt;
    }
}
