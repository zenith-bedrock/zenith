namespace Zenith.Player;

/// <summary>Personal grid and indexed craft-result custody, included in live inventory saves (ADR §158).</summary>
sealed class PlayerCraftUi : ICraftGrid
{
    public const int GridSize = 4;
    int ICraftGrid.GridSize => GridSize;

    private readonly InventorySlot[] _grid = new InventorySlot[GridSize];
    private InventorySlot _result = InventorySlot.Empty;
    public const int MaxCreatedOutputs = 256; // Create selects an unsigned byte index.
    private InventorySlot[] _outputs = [];
    private int _selectedOutput;
    public bool HasPendingOutputs => !_result.IsEmpty || _outputs.Any(s => !s.IsEmpty);

    public InventorySlot GetGrid(int index) =>
        index is >= 0 and < GridSize ? _grid[index] : InventorySlot.Empty;

    public InventorySlot Result => _result;

    public bool TrySetGrid(int index, InventorySlot slot)
    {
        if (index is < 0 or >= GridSize) return false;
        _grid[index] = slot;
        return true;
    }

    public bool TrySetResult(InventorySlot slot)
    {
        _result = slot;
        if (_outputs.Length > 0) _outputs[_selectedOutput] = slot;
        return true;
    }

    public bool TryStageOutputs(InventorySlot[] outputs)
    {
        if (HasPendingOutputs || outputs.Length is < 1 or > MaxCreatedOutputs) return false;
        _outputs = (InventorySlot[])outputs.Clone();
        _selectedOutput = 0;
        _result = _outputs[0];
        return true;
    }

    public bool TrySelectOutput(byte index)
    {
        if (_outputs.Length == 0) return index == 0 && !_result.IsEmpty;
        if (index >= _outputs.Length || _outputs[index].IsEmpty) return false;
        _selectedOutput = index;
        _result = _outputs[index];
        return true;
    }

    public InventorySlot[] SnapshotOutputs() => _outputs.Length == 0 ? [_result] : (InventorySlot[])_outputs.Clone();

    public readonly record struct FullSnapshot(InventorySlot[] Grid, InventorySlot[] Outputs, int Selected);
    public FullSnapshot CaptureFullSnapshot() => new(CaptureSnapshot(), (InventorySlot[])_outputs.Clone(), _selectedOutput);
    public void RestoreFullSnapshot(FullSnapshot snapshot)
    {
        RestoreSnapshot(snapshot.Grid);
        _outputs = (InventorySlot[])snapshot.Outputs.Clone();
        _selectedOutput = snapshot.Selected;
    }

    public void RestoreOutputs(InventorySlot[] outputs)
    {
        _outputs = (InventorySlot[])outputs.Clone();
        _selectedOutput = 0;
        _result = _outputs.Length == 0 ? InventorySlot.Empty : _outputs[0];
    }

    public bool RecoverOutputs(PlayerInventory inventory)
    {
        var changed = false;
        var outputs = SnapshotOutputs();
        for (var i = 0; i < outputs.Length; i++)
        {
            if (outputs[i].IsEmpty) continue;
            var added = inventory.TryAddUpTo(outputs[i].Id, outputs[i].Count);
            changed |= added > 0;
            outputs[i] = added == outputs[i].Count ? InventorySlot.Empty : outputs[i] with { Count = outputs[i].Count - added };
        }
        RestoreOutputs(outputs);
        return changed;
    }

    public InventorySlot[] CaptureSnapshot()
    {
        var snap = new InventorySlot[GridSize + 1];
        Array.Copy(_grid, snap, GridSize);
        snap[GridSize] = _result;
        return snap;
    }

    public void RestoreSnapshot(InventorySlot[] snap)
    {
        if (snap.Length != GridSize + 1) return;
        Array.Copy(snap, _grid, GridSize);
        _result = snap[GridSize];
    }

    public void Clear()
    {
        for (var i = 0; i < GridSize; i++)
            _grid[i] = InventorySlot.Empty;
        _result = InventorySlot.Empty;
        _outputs = [];
        _selectedOutput = 0;
    }
}
