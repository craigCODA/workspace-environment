namespace Workspace.Host.Persistence;

public interface IWorkspaceStore
{
    Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken);
}
