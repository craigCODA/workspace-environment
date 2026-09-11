using Workspace.Desktop.Core.Persistence;

namespace Workspace.Desktop.Core.Preferences;

public sealed class VoiceProfileStore
{
    private readonly AtomicJsonStore<VoiceProfile> _store;

    public VoiceProfileStore(string path) => _store = new AtomicJsonStore<VoiceProfile>(path);

    public async Task<VoiceProfile> LoadAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _store.LoadOrDefaultAsync(() => VoiceProfile.Default, cancellationToken);
        return VoiceProfile.Migrate(profile);
    }

    public Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != VoiceProfile.CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                profile.SchemaVersion,
                "The voice profile schema version is not supported.");
        }

        if (string.IsNullOrWhiteSpace(profile.WakePhrase))
        {
            throw new ArgumentException("The wake phrase cannot be empty.", nameof(profile));
        }

        return _store.SaveAsync(profile, cancellationToken);
    }
}
