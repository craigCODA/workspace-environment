using Workspace.Desktop.Core.Persistence;

namespace Workspace.Desktop.Core.Capabilities;

public sealed class CapabilityBroker
{
    private static readonly HashSet<string> FreshConfirmationCapabilities = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "credential.read",
        "remote.publish",
        "system.configure",
        "filesystem.destructive-outside-workspace",
    };

    private readonly AtomicJsonStore<CapabilityGrantDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private List<CapabilityGrant> _grants;

    private CapabilityBroker(
        AtomicJsonStore<CapabilityGrantDocument> store,
        IEnumerable<CapabilityGrant> grants,
        TimeProvider timeProvider)
    {
        _store = store;
        _grants = grants.ToList();
        _timeProvider = timeProvider;
    }

    public static async Task<CapabilityBroker> OpenAsync(
        string path,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var store = new AtomicJsonStore<CapabilityGrantDocument>(path);
        var document = await store.LoadOrDefaultAsync(
            () => CapabilityGrantDocument.Empty,
            cancellationToken);
        var grants = document.SchemaVersion == CapabilityGrantDocument.CurrentSchemaVersion
            ? document.Grants
            : Array.Empty<CapabilityGrant>();
        return new CapabilityBroker(store, grants, timeProvider ?? TimeProvider.System);
    }

    public IReadOnlyList<CapabilityGrant> Grants => _grants.AsReadOnly();

    public bool IsGranted(string capability, string scope)
    {
        if (FreshConfirmationCapabilities.Contains(capability)) return false;
        var normalizedScope = NormalizeScope(scope, rejectRoot: false);
        var now = _timeProvider.GetUtcNow();
        return _grants.Any(grant =>
            string.Equals(grant.Capability, capability, StringComparison.OrdinalIgnoreCase)
            && string.Equals(grant.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase)
            && (grant.ExpiresAt is null || grant.ExpiresAt > now));
    }

    public async Task RememberAsync(
        CapabilityGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.Capability);
        if (FreshConfirmationCapabilities.Contains(grant.Capability))
        {
            throw new InvalidOperationException(
                $"{grant.Capability} always requires fresh confirmation.");
        }

        var normalized = grant with
        {
            Capability = grant.Capability.Trim(),
            Scope = NormalizeScope(grant.Scope, rejectRoot: true),
            ApprovedAt = grant.ApprovedAt == default
                ? _timeProvider.GetUtcNow()
                : grant.ApprovedAt,
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _grants.RemoveAll(existing =>
                string.Equals(existing.Capability, normalized.Capability, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Scope, normalized.Scope, StringComparison.OrdinalIgnoreCase));
            _grants.Add(normalized);
            await PersistAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RevokeAsync(
        string capability,
        string scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedScope = NormalizeScope(scope, rejectRoot: false);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var removed = _grants.RemoveAll(grant =>
                string.Equals(grant.Capability, capability, StringComparison.OrdinalIgnoreCase)
                && string.Equals(grant.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) await PersistAsync(cancellationToken);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task PersistAsync(CancellationToken cancellationToken) => _store.SaveAsync(
        new CapabilityGrantDocument(
            CapabilityGrantDocument.CurrentSchemaVersion,
            _grants.ToArray()),
        cancellationToken);

    private static string NormalizeScope(string scope, bool rejectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var trimmed = scope.Trim();
        if (!Path.IsPathRooted(trimmed)) return trimmed;

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalized)!);
        if (rejectRoot && string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A filesystem root cannot be remembered as a capability scope.", nameof(scope));
        }

        return normalized;
    }
}
