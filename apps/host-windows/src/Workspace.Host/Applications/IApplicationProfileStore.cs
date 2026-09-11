namespace Workspace.Host.Applications;

public interface IApplicationProfileStore
{
    Task<IReadOnlyList<ApplicationLaunchProfile>> ListAsync(CancellationToken cancellationToken);

    Task<ApplicationLaunchProfile?> FindAsync(string profileId, CancellationToken cancellationToken);

    Task SaveAsync(ApplicationLaunchProfile profile, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string profileId, CancellationToken cancellationToken);
}
