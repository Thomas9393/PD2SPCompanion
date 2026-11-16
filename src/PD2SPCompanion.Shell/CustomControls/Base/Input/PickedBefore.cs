using System.Collections.Generic;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// What a rectangle's Ctrl + drag leaves as it was: each item's state the first time the drag sees it. Asked once at
/// the drag's start, a vault row built later by the scroll at the edge counted as not picked, and its picks outside
/// the rectangle were let go of, though Ctrl + drag only adds: the rows that are
/// not built hold picks too (VP_ViewModel_Vault_L.ReadingOrder).
/// </summary>
public sealed class PickedBefore
{
    private readonly Dictionary<object, bool> _seen = [];

    /// <summary>Whether the item was picked before the drag touched it: <paramref name="pickedNow"/>, the first time it is asked of.</summary>
    public bool Seen(object item, bool pickedNow)
    {
        if (_seen.TryGetValue(item, out bool was))
            return was;

        _seen[item] = pickedNow;
        return pickedNow;
    }

    public void Clear() => _seen.Clear();
}
