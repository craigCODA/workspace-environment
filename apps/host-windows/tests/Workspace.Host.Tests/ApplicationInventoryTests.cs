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
}
