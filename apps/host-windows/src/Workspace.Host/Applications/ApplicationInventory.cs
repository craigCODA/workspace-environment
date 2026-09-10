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

        var merged = new Dictionary<(ApplicationLaunchKind LaunchKind, string Locator), ApplicationDescriptor>();
        var aliases = new Dictionary<(ApplicationLaunchKind LaunchKind, string Locator), HashSet<string>>();

        foreach (var application in applications)
        {
            var key = (application.LaunchKind, application.Locator.ToUpperInvariant());
            if (!merged.TryAdd(key, application))
            {
                aliases[key].UnionWith(application.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)));
                continue;
            }

            aliases[key] = new HashSet<string>(
                application.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)),
                StringComparer.OrdinalIgnoreCase);
        }

        return merged.Select(pair =>
        {
            var application = pair.Value;
            return application with
            {
                Aliases = aliases[pair.Key]
                    .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
        }).ToArray();
    }
}
