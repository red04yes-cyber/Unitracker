using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>
/// Serializes complete read-modify-write operations over the persisted study
/// aggregate. Callers receive a freshly loaded snapshot for every operation;
/// no page can save a stale snapshot over changes committed by another page.
/// </summary>
public sealed class StudyDataSession(IStudyDataStore store)
{
    private readonly IStudyDataStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _revision;

    public event EventHandler<StudyDataChangedEventArgs>? DataChanged;

    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>
    /// Runs a read projection against the latest persisted snapshot. The
    /// projection should return an immutable/read-only result, not the
    /// mutable StudyData aggregate or one of its collections.
    /// </summary>
    public async Task<TResult> QueryAsync<TResult>(
        Func<StudyData, TResult> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            return projection(snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies one mutation to the latest snapshot and persists it before
    /// releasing the session. A failed mutation or save does not publish a
    /// change notification.
    /// </summary>
    public async Task<TResult> UpdateAsync<TResult>(
        Func<StudyData, TResult> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        TResult result;
        long revision;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            result = mutation(snapshot);
            await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            revision = Interlocked.Increment(ref _revision);
        }
        finally
        {
            _gate.Release();
        }

        DataChanged?.Invoke(this, new StudyDataChangedEventArgs(revision));
        return result;
    }
}

public sealed class StudyDataChangedEventArgs(long revision) : EventArgs
{
    public long Revision { get; } = revision;
}
