namespace Workspace.Host.Applications;

public interface IApplicationInventorySource
{
    Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken);
}

public static class ApplicationInventory
{
    public static IReadOnlyList<ApplicationDescriptor> Merge(
        IEnumerable<ApplicationDescriptor> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        return applications
            .GroupBy(application => (application.LaunchKind, NormalizeLocator(application.Locator)))
            .Select(group =>
        {
            var canonical = group
                .OrderBy(application => application.Id, StringComparer.Ordinal)
                .ThenBy(application => application.DisplayName, StringComparer.Ordinal)
                .ThenBy(application => application.Locator, StringComparer.Ordinal)
                .First();
            var aliases = group
                .SelectMany(application => application.Aliases)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .GroupBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                .Select(aliases => aliases.OrderBy(alias => alias, StringComparer.Ordinal).First())
                .OrderBy(alias => alias, StringComparer.Ordinal)
                .ToArray();

            return canonical with
            {
                Aliases = aliases,
            };
        })
        .OrderBy(application => application.Id, StringComparer.Ordinal)
        .ThenBy(application => application.DisplayName, StringComparer.Ordinal)
        .ThenBy(application => application.Locator, StringComparer.Ordinal)
        .ToArray();
    }

    private static string NormalizeLocator(string locator) =>
        locator.Replace('/', '\\').ToUpperInvariant();
}
