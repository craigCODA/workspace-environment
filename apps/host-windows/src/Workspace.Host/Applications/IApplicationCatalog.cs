namespace Workspace.Host.Applications;

public interface IApplicationCatalog
{
    Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken);

    Task<ApplicationDescriptor?> FindByNameAsync(string displayName, CancellationToken cancellationToken);
}

public sealed class InMemoryApplicationCatalog(IEnumerable<ApplicationDescriptor> applications) : IApplicationCatalog
{
    private readonly IReadOnlyList<ApplicationDescriptor> _applications = applications.ToArray();

    public Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_applications);
    }

    public Task<ApplicationDescriptor?> FindByNameAsync(string displayName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var match = _applications.FirstOrDefault(app =>
            string.Equals(app.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }
}
