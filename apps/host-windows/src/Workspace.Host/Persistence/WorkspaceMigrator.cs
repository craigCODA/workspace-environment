using Workspace.Host.Domain;

namespace Workspace.Host.Persistence;

public static class WorkspaceMigrator
{
    public static WorkspaceDocument MigrateToCurrent(WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion >= WorkspaceDocument.CurrentSchemaVersion)
        {
            return document;
        }

        var entities = new List<WorkspaceEntity>(document.Entities);
        foreach (var window in document.Entities.Where(entity =>
                     entity.Kind == EntityKinds.Window
                     && entity.Presentation.ParentPresentationId is null))
        {
            var isAlreadyDisplayed = entities.Any(entity =>
                entity.Kind == EntityKinds.Surface
                && entity.Relationships.Any(relationship =>
                    relationship.Type == "displays" && relationship.TargetId == window.Id));
            if (isAlreadyDisplayed)
            {
                continue;
            }

            entities.Add(WorkspaceEntity.CreateDisplaySurface(
                $"{EntityKinds.Surface}:{window.Id}",
                window.Name,
                window.Presentation,
                window.Id));
        }

        return new WorkspaceDocument(WorkspaceDocument.CurrentSchemaVersion, entities);
    }

    public static async Task EnsureCurrentAsync(IWorkspaceStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        var document = await store.LoadAsync(cancellationToken);
        var migrated = MigrateToCurrent(document);
        if (!ReferenceEquals(document, migrated))
        {
            await store.SaveAsync(migrated, cancellationToken);
        }
    }
}
