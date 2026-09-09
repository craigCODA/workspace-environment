using Workspace.Desktop.Core.Persistence;

namespace Workspace.Desktop.Core.Launcher;

public sealed class ActivationStore
{
    private readonly AtomicJsonStore<ActivationState> _store;

    public ActivationStore(string path) => _store = new AtomicJsonStore<ActivationState>(path);

    public async Task<ActivationState> LoadAsync(CancellationToken cancellationToken = default)
    {
        var state = await _store.LoadOrDefaultAsync(() => ActivationState.Empty, cancellationToken);
        return state.SchemaVersion == ActivationState.CurrentSchemaVersion
            ? state
            : ActivationState.Empty;
    }

    public Task SaveAsync(ActivationState state, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(state, cancellationToken);

    public async Task<ActivationState> UpdateAsync(
        Func<ActivationState, ActivationState> update,
        CancellationToken cancellationToken = default)
    {
        var next = update(await LoadAsync(cancellationToken));
        await SaveAsync(next, cancellationToken);
        return next;
    }
}
