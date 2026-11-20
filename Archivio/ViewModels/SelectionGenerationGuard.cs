namespace Archivio.ViewModels;

/// <summary>選択変更を世代番号で追跡し、古い非同期処理のUI反映を防ぐ。</summary>
public sealed class SelectionGenerationGuard
{
    private long _generation;

    public long Begin() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long generation)
        => generation == Volatile.Read(ref _generation);
}
