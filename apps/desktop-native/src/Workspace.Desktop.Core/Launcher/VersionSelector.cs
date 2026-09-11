namespace Workspace.Desktop.Core.Launcher;

public sealed record ActivationState(
    int SchemaVersion,
    string? ActiveVersion,
    string? PendingVersion,
    string? KnownGoodVersion)
{
    public const int CurrentSchemaVersion = 1;
    public static ActivationState Empty { get; } = new(CurrentSchemaVersion, null, null, null);
}

public static class VersionSelector
{
    public static string? Select(ActivationState state) =>
        !string.IsNullOrWhiteSpace(state.PendingVersion)
            ? state.PendingVersion
            : state.ActiveVersion;

    public static ActivationState Stage(ActivationState state, string versionId) =>
        state with { PendingVersion = RequireVersion(versionId) };

    public static ActivationState MarkHealthy(ActivationState state, string versionId)
    {
        var version = RequireVersion(versionId);
        return state with
        {
            ActiveVersion = version,
            PendingVersion = null,
            KnownGoodVersion = version,
        };
    }

    public static ActivationState MarkFailed(ActivationState state, string versionId)
    {
        var version = RequireVersion(versionId);
        if (!string.Equals(state.PendingVersion, version, StringComparison.OrdinalIgnoreCase))
            return state;
        return state with
        {
            ActiveVersion = state.KnownGoodVersion,
            PendingVersion = null,
        };
    }

    private static string RequireVersion(string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        if (versionId is "." or ".." || versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The version id is not safe.", nameof(versionId));
        return versionId.Trim();
    }
}
