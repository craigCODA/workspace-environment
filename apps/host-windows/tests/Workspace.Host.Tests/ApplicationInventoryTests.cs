using Workspace.Host.Applications;

namespace Workspace.Host.Tests;

public sealed class ApplicationInventoryTests
{
    [Fact]
    public void Inventory_merges_duplicate_locators_and_keeps_aliases()
    {
        var merged = ApplicationInventory.Merge(
        [
            new("app:edge", "Microsoft Edge", ApplicationLaunchKind.Executable, @"C:\Edge\msedge.exe", ["Edge"]),
            new("app:copy", "Edge", ApplicationLaunchKind.Executable, @"C:\EDGE\msedge.exe", ["Microsoft Edge"]),
        ]);

        var application = Assert.Single(merged);

        Assert.Contains("Edge", application.Aliases);
    }

    [Fact]
    public void Inventory_merge_chooses_a_canonical_descriptor_independent_of_source_order()
    {
        var canonical = new ApplicationDescriptor(
            "app:edge",
            "Microsoft Edge",
            ApplicationLaunchKind.Executable,
            @"C:\EDGE\msedge.exe",
            ["Edge"]);
        var duplicate = new ApplicationDescriptor(
            "app:edge-copy",
            "Edge",
            ApplicationLaunchKind.Executable,
            @"C:\edge\msedge.exe",
            ["Browser", "Microsoft Edge"]);

        var forward = Assert.Single(ApplicationInventory.Merge([duplicate, canonical]));
        var reverse = Assert.Single(ApplicationInventory.Merge([canonical, duplicate]));

        Assert.Equal("app:edge", forward.Id);
        Assert.Equal("Microsoft Edge", forward.DisplayName);
        Assert.Equal(@"C:\EDGE\msedge.exe", forward.Locator);
        Assert.Equal(forward.Id, reverse.Id);
        Assert.Equal(forward.DisplayName, reverse.DisplayName);
        Assert.Equal(forward.Locator, reverse.Locator);
        Assert.Equal(["Browser", "Edge", "Microsoft Edge"], forward.Aliases);
        Assert.Equal(forward.Aliases, reverse.Aliases);
    }

    [Fact]
    public async Task Catalog_lookup_uses_resolver_identifiers_aliases_prefixes_and_normalization()
    {
        var catalog = new WindowsApplicationCatalog(
        [
            new FixedInventorySource(
            [
                new ApplicationDescriptor("app:edge", "Microsoft Edge", ApplicationLaunchKind.Executable,
                    @"C:\Edge\msedge.exe", ["Edge"]),
            ]),
        ]);

        foreach (var query in new[] { "app:edge", "edge", "Micro", "  Ｍｉｃｒｏｓｏｆｔ\u00a0Edge  " })
        {
            var result = await catalog.FindByNameAsync(query, CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal("app:edge", result.Id);
        }
    }

    [Fact]
    public async Task Catalog_lookup_returns_null_for_an_ambiguous_resolver_result()
    {
        var catalog = new WindowsApplicationCatalog(
        [
            new FixedInventorySource(
            [
                new ApplicationDescriptor("app:visual-studio", "Visual Studio", ApplicationLaunchKind.Executable,
                    @"C:\VS\devenv.exe", []),
                new ApplicationDescriptor("app:visual-studio-code", "Visual Studio Code", ApplicationLaunchKind.Executable,
                    @"C:\Code\code.exe", []),
            ]),
        ]);

        var result = await catalog.FindByNameAsync("Visual", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Catalog_continues_after_an_inaccessible_inventory_source()
    {
        var catalog = new WindowsApplicationCatalog(
        [
            new InaccessibleInventorySource(),
            new FixedInventorySource(
            [
                new ApplicationDescriptor("app:notepad", "Notepad", ApplicationLaunchKind.Executable,
                    @"C:\Windows\notepad.exe", []),
            ]),
        ]);

        var applications = await catalog.ListAsync(CancellationToken.None);

        Assert.Equal("app:notepad", Assert.Single(applications).Id);
    }

    [Fact]
    public void Apps_folder_locator_uses_the_shell_activation_boundary()
    {
        Assert.Equal(
            "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            WindowsApplicationCatalog.CreateAppsFolderLocator("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
    }

    private sealed class FixedInventorySource(IReadOnlyList<ApplicationDescriptor> applications) : IApplicationInventorySource
    {
        public Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(applications);
    }

    private sealed class InaccessibleInventorySource : IApplicationInventorySource
    {
        public Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("Inventory source denied access.");
    }
}
