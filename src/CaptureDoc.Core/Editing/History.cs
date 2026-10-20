namespace CaptureDoc.Core.Editing;

/// <summary>
/// Memento-based undo/redo. State is captured as a string snapshot (JSON of a page).
/// See DevVault ADR-004.
/// </summary>
public sealed class History
{
    private readonly Func<string> _capture;
    private readonly Action<string> _restore;
    private readonly List<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();
    private string? _txBefore;
    private string? _txDescription;

    public int Limit { get; }

    public History(Func<string> capture, Action<string> restore, int limit = 200)
    {
        _capture = capture;
        _restore = restore;
        Limit = limit;
    }

    public sealed record Entry(string Description, string Before, string After)
    {
        public string? MergeKey { get; init; }
        public DateTime Time { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Like <see cref="Execute(string, Action)"/>, but consecutive edits with the same <paramref name="mergeKey"/>
    /// within <paramref name="window"/> collapse into one step (e.g. dragging a slider).
    /// </summary>
    public bool Execute(string description, Action action, string mergeKey, TimeSpan? window = null)
    {
        if (_txBefore is not null) { action(); return true; }
        var last = _undo.Count > 0 ? _undo[^1] : null;
        var before = _capture();
        action();
        var after = _capture();
        if (string.Equals(before, after, StringComparison.Ordinal)) return false;
        if (last is not null && last.MergeKey == mergeKey && _redo.Count == 0 &&
            DateTime.UtcNow - last.Time < (window ?? TimeSpan.FromSeconds(1.5)) && last.After == before)
        {
            _undo[^1] = last with { After = after, Time = DateTime.UtcNow };
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        _undo.Add(new Entry(description, before, after) { MergeKey = mergeKey });
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0 && _txBefore is null;
    public bool CanRedo => _redo.Count > 0 && _txBefore is null;
    public bool InTransaction => _txBefore is not null;
    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;
    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;
    public IReadOnlyList<string> UndoDescriptions => _undo.Select(e => e.Description).ToList();

    /// <summary>Runs <paramref name="action"/> and records it as one step (no step if nothing changed).</summary>
    public bool Execute(string description, Action action)
    {
        if (_txBefore is not null)
        {
            action();
            return true;
        }
        var before = _capture();
        action();
        return Push(description, before, _capture());
    }

    /// <summary>Starts a multi-event edit (e.g. mouse drag). Must be followed by Commit or Rollback.</summary>
    public void BeginTransaction(string description)
    {
        if (_txBefore is not null) Commit();
        _txBefore = _capture();
        _txDescription = description;
    }

    public bool Commit()
    {
        if (_txBefore is null) return false;
        var before = _txBefore; var desc = _txDescription ?? "Bearbeiten";
        _txBefore = null; _txDescription = null;
        return Push(desc, before, _capture());
    }

    public void Rollback()
    {
        if (_txBefore is null) return;
        var before = _txBefore;
        _txBefore = null; _txDescription = null;
        _restore(before);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _restore(e.Before);
        _redo.Push(e);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        var e = _redo.Pop();
        _restore(e.After);
        _undo.Add(e);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _undo.Clear(); _redo.Clear(); _txBefore = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool Push(string description, string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal)) return false;
        _undo.Add(new Entry(description, before, after));
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
