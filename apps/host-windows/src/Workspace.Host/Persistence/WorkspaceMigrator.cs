using Workspace.Host.Domain;

namespace Workspace.Host.Persistence;

public static class WorkspaceMigrator
{
    public static WorkspaceDocument MigrateToCurrent(WorkspaceDocument document)
    {
        ValidateDocument(document);

        if (document.SchemaVersion == WorkspaceDocument.CurrentSchemaVersion)
        {
            return document;
        }

        var entities = new List<WorkspaceEntity>(document.Entities);
        foreach (var window in document.Entities.Where(entity =>
                     entity.Kind == EntityKinds.Window
                     && entity.Presentation.ParentPresentationId is null))
        {
            var deterministicSurfaceId = $"{EntityKinds.Surface}:{window.Id}";
            var deterministicEntity = entities.SingleOrDefault(entity =>
                string.Equals(entity.Id, deterministicSurfaceId, StringComparison.Ordinal));
            var displaySurfaces = entities.Where(entity =>
                entity.Kind == EntityKinds.Surface
                && Displays(entity, window.Id))
                .ToList();

            if (displaySurfaces.Count > 1)
            {
                throw InvalidDocument($"Window '{window.Id}' is displayed by multiple surfaces.");
            }

            if (deterministicEntity is not null
                && (deterministicEntity.Kind != EntityKinds.Surface
                    || !Displays(deterministicEntity, window.Id)))
            {
                throw InvalidDocument(
                    $"Deterministic surface id '{deterministicSurfaceId}' does not identify the migrated window.");
            }

            if (displaySurfaces.Count == 1)
            {
                continue;
            }

            entities.Add(WorkspaceEntity.CreateDisplaySurface(
                deterministicSurfaceId,
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

    public static void ValidateDocument(WorkspaceDocument? document)
    {
        if (document is null)
        {
            throw InvalidDocument("Workspace document was null.");
        }

        if (document.SchemaVersion <= 0)
        {
            throw InvalidDocument($"Schema version '{document.SchemaVersion}' is invalid.");
        }

        if (document.SchemaVersion > WorkspaceDocument.CurrentSchemaVersion)
        {
            throw InvalidDocument($"Schema version '{document.SchemaVersion}' is newer than this host supports.");
        }

        if (document.Entities is null)
        {
            throw InvalidDocument("Workspace document entities were null.");
        }

        var entityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in document.Entities)
        {
            if (entity is null)
            {
                throw InvalidDocument("Workspace document contained a null entity.");
            }

            if (string.IsNullOrWhiteSpace(entity.Id)
                || string.IsNullOrWhiteSpace(entity.Kind)
                || string.IsNullOrWhiteSpace(entity.Name))
            {
                throw InvalidDocument("Workspace entity identity fields were missing.");
            }

            if (!entityIds.Add(entity.Id))
            {
                throw InvalidDocument($"Workspace document contains duplicate entity id '{entity.Id}'.");
            }

            if (entity.Presentation is null
                || entity.Properties is null
                || entity.Relationships is null
                || entity.Capabilities is null)
            {
                throw InvalidDocument($"Workspace entity '{entity.Id}' had a null structural member.");
            }

            if (entity.HostBinding is { } hostBinding
                && (string.IsNullOrWhiteSpace(hostBinding.Type)
                    || string.IsNullOrWhiteSpace(hostBinding.Locator)))
            {
                throw InvalidDocument($"Workspace entity '{entity.Id}' had an invalid host binding.");
            }

            foreach (var relationship in entity.Relationships)
            {
                if (relationship is null
                    || string.IsNullOrWhiteSpace(relationship.Type)
                    || string.IsNullOrWhiteSpace(relationship.TargetId))
                {
                    throw InvalidDocument($"Workspace entity '{entity.Id}' had an invalid relationship.");
                }
            }

            if (entity.Capabilities.Any(string.IsNullOrWhiteSpace))
            {
                throw InvalidDocument($"Workspace entity '{entity.Id}' had an invalid capability.");
            }

            if (entity.Kind == EntityKinds.Surface
                && entity.Relationships.Count(relationship => relationship.Type == "displays") > 1)
            {
                throw InvalidDocument($"Surface '{entity.Id}' displayed more than one window.");
            }
        }
    }

    private static bool Displays(WorkspaceEntity surface, string windowId)
    {
        return surface.Relationships.Any(relationship =>
            relationship.Type == "displays" && relationship.TargetId == windowId);
    }

    private static InvalidDataException InvalidDocument(string message) => new(message);
}
