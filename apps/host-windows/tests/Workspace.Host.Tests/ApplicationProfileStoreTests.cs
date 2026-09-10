using System.Text.Json;
using Workspace.Host.Applications;
using Workspace.Host.Domain;

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
            Id = "PROFILE:PYTHOS-CODEX",
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

        Assert.True(await store.DeleteAsync(profile.Id.ToUpperInvariant(), CancellationToken.None));
        Assert.Null(await store.FindAsync(profile.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(profile.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Find_matches_profile_ids_case_insensitively()
    {
        IApplicationProfileStore store = CreateStore();
        var profile = Profile("profile:pythos-codex", "PythOS Codex", [], null);
        await store.SaveAsync(profile, CancellationToken.None);

        Assert.Equal(profile, await store.FindAsync(profile.Id.ToUpperInvariant(), CancellationToken.None));
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

    [Fact]
    public async Task Save_rejects_nul_characters_in_nested_preferred_presentation_fields()
    {
        IApplicationProfileStore store = CreateStore();
        var parentId = Profile("profile:parent-nul", "PythOS Codex", [], null) with
        {
            PreferredPresentation = PresentationState.Default with { ParentPresentationId = "surface\0child" },
        };
        var representation = Profile("profile:representation-nul", "PythOS Codex", [], null) with
        {
            PreferredPresentation = PresentationState.Default with { Representation = "screen\0stream" },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(parentId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(representation, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_profile_file_returns_an_empty_store()
    {
        IApplicationProfileStore store = CreateStore();

        Assert.Empty(await store.ListAsync(CancellationToken.None));
        Assert.Null(await store.FindAsync("profile:missing", CancellationToken.None));
        Assert.False(await store.DeleteAsync("profile:missing", CancellationToken.None));
    }

    [Fact]
    public async Task List_rejects_a_null_profile_entry_as_invalid_data()
    {
        await WriteDocumentAsync("""
            { "version": 1, "profiles": [null] }
            """);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateStore().ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task List_rejects_duplicate_case_colliding_profile_ids_as_invalid_data()
    {
        await WriteDocumentAsync("""
            {
              "version": 1,
              "profiles": [
                { "id": "profile:pythos", "displayName": "PythOS", "applicationId": "app:terminal", "arguments": [], "workingDirectory": null, "launchPolicy": 0, "preferredSurfaceId": null, "preferredPresentation": null },
                { "id": "PROFILE:PYTHOS", "displayName": "PythOS Duplicate", "applicationId": "app:terminal", "arguments": [], "workingDirectory": null, "launchPolicy": 0, "preferredSurfaceId": null, "preferredPresentation": null }
              ]
            }
            """);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateStore().FindAsync("profile:pythos", CancellationToken.None));
    }

    [Fact]
    public async Task List_rejects_malformed_json_as_invalid_data()
    {
        await WriteDocumentAsync("{ \"version\":");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateStore().ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task List_rejects_an_unsupported_schema_as_invalid_data()
    {
        await WriteDocumentAsync("""
            { "version": 2, "profiles": [] }
            """);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateStore().ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Save_persists_profiles_in_deterministic_id_order()
    {
        IApplicationProfileStore store = CreateStore();
        await store.SaveAsync(Profile("profile:zulu", "Zulu", [], null), CancellationToken.None);
        await store.SaveAsync(Profile("profile:alpha", "Alpha", [], null), CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(ProfileStorePath));
        var profileIds = document.RootElement
            .GetProperty("profiles")
            .EnumerateArray()
            .Select(profile => profile.GetProperty("id").GetString()!)
            .ToArray();

        Assert.Equal(["profile:alpha", "profile:zulu"], profileIds);
    }

    [Fact]
    public async Task Save_cancellation_preserves_the_old_file_without_temporary_files()
    {
        IApplicationProfileStore store = CreateStore();
        var original = Profile("profile:pythos-codex", "PythOS Codex", [], null);
        await store.SaveAsync(original, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(
            original with { DisplayName = "Changed" },
            cancellation.Token));

        Assert.Equal(original, await CreateStore().FindAsync(original.Id, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_temporaryDirectory, "application-profiles.json.*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private IApplicationProfileStore CreateStore() => new AtomicApplicationProfileStore(
        ProfileStorePath);

    private string ProfileStorePath => Path.Combine(_temporaryDirectory, "application-profiles.json");

    private Task WriteDocumentAsync(string content) => File.WriteAllTextAsync(ProfileStorePath, content);

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
