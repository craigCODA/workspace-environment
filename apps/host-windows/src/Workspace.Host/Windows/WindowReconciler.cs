namespace Workspace.Host.Windows;

public sealed class WindowReconciler
{
    public string ResolveEntityId(WindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.ApplicationId);

        return $"pc.window:{snapshot.ApplicationId}";
    }
}
