using Workspace.Host.Applications;

namespace Workspace.Host.Tests;

public sealed class ApplicationProfileStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"workspace-host-profile-tests-{Guid.NewGuid():N}");

    public ApplicationProfileStoreTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task Save_round_trips_tokenized_arguments()
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile(
            "profile:pythos-codex",
            "PythOS Codex",
            ["new-tab", "codex"],
            @"D:\PythOS-Workspace");

        await store.SaveAsync(profile, CancellationToken.None);

        Assert.Equal(profile, await store.FindAsync(profile.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Save_replaces_a_profile_and_lists_the_saved_version()
    {
        IApplicationProfileStore store = CreateStore();
        var original = Profile("profile:pythos-codex", "PythOS Codex", ["new-tab"], null);
        var updated = original with
        {
            DisplayName = "PythOS Codex Workspace",
            Arguments = ["new-tab", "codex"],
            LaunchPolicy = ApplicationLaunchPolicy.NewInstance,
        };

        await store.SaveAsync(original, CancellationToken.None);
        await store.SaveAsync(updated, CancellationToken.None);

        Assert.Equal([updated], await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Delete_removes_an_existing_profile_and_reports_missing_profiles()
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile("profile:pythos-codex", "PythOS Codex", [], null);
        await store.SaveAsync(profile, CancellationToken.None);

        Assert.True(await store.DeleteAsync(profile.Id, CancellationToken.None));
        Assert.Null(await store.FindAsync(profile.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(profile.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Save_rejects_a_relative_working_directory()
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile("profile:bad", "Bad", [], @"..\outside");

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("profile\0bad")]
    public async Task Save_rejects_blank_or_nul_profile_ids(string id)
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile(id, "PythOS Codex", [], null);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile, CancellationToken.None));
    }

    [Fact]
    public async Task Save_rejects_nul_characters_in_profile_text()
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile("profile:pythos-codex", "PythOS\0Codex", [], null);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile, CancellationToken.None));
    }

    [Fact]
    public async Task Save_rejects_more_than_sixty_four_arguments()
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile(
            "profile:pythos-codex",
            "PythOS Codex",
            Enumerable.Repeat("argument", 65).ToArray(),
            null);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile, CancellationToken.None));
    }

    [Fact]
    public async Task Save_rejects_arguments_longer_than_4096_characters()
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile(
            "profile:pythos-codex",
            "PythOS Codex",
            [new string('a', 4097)],
            null);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private IApplicationProfileStore CreateStore() => new AtomicApplicationProfileStore(
        Path.Combine(_temporaryDirectory, "application-profiles.json"));

    private static ApplicationLaunchProfile Profile(
        string id,
        string displayName,
        IReadOnlyList<string> arguments,
        string? workingDirectory) => new(
        id,
        displayName,
        "app:terminal",
        arguments,
        workingDirectory,
        ApplicationLaunchPolicy.ReuseOrLaunch,
        null,
        null);
}
