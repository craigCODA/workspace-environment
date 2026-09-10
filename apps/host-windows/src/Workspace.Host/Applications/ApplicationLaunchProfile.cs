using Workspace.Host.Domain;

namespace Workspace.Host.Applications;

public enum ApplicationLaunchPolicy
{
    ReuseOrLaunch,
    NewInstance,
}

public sealed record ApplicationLaunchProfile(
    string Id,
    string DisplayName,
    string ApplicationId,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    ApplicationLaunchPolicy LaunchPolicy,
    string? PreferredSurfaceId,
    PresentationState? PreferredPresentation)
{
    public bool Equals(ApplicationLaunchProfile? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && Id == other.Id
        && DisplayName == other.DisplayName
        && ApplicationId == other.ApplicationId
        && Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal)
        && WorkingDirectory == other.WorkingDirectory
        && LaunchPolicy == other.LaunchPolicy
        && PreferredSurfaceId == other.PreferredSurfaceId
        && PreferredPresentation == other.PreferredPresentation;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(DisplayName, StringComparer.Ordinal);
        hash.Add(ApplicationId, StringComparer.Ordinal);
        foreach (var argument in Arguments)
        {
            hash.Add(argument, StringComparer.Ordinal);
        }
        hash.Add(WorkingDirectory, StringComparer.Ordinal);
        hash.Add(LaunchPolicy);
        hash.Add(PreferredSurfaceId, StringComparer.Ordinal);
        hash.Add(PreferredPresentation);
        return hash.ToHashCode();
    }
}
