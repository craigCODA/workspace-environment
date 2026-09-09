namespace Workspace.Host.Windows;

public interface IWindowCatalog
{
    Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken);
}
