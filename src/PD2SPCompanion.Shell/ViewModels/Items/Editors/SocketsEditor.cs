using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item panel's sockets: the rows, a socket added or closed or typed, a gem marked to come out, the
/// die. Staged like everything else on the panel - boring a socket is harmless, but closing
/// one turns out anything inside it, so what leaves is shown before KEEP rather than reported after.
/// </summary>
public sealed partial class SocketsEditor : ViewModelBase, IDragPicker
{
    private readonly IItemPanel _panel;

    public SocketsEditor(IItemPanel panel)
    {
        _panel = panel;
        _deleteByKey = DeleteByKey;
    }

    /// <summary>What the play mode allows, for the rows' own buttons.</summary>
    public Services.Settings.PlayRules Rules => _panel.Rules;

    /// <summary>
    /// The socket rows, as they will read once the item is kept. Long enough to show what is
    /// leaving: a socket being closed stays on the list, struck through, until KEEP.
    /// </summary>
    public ObservableCollection<SocketViewModel> Rows { get; } = [];

    /// <summary>How many sockets the item will have once kept.</summary>
    public int Staged { get; private set; }

    /// <summary>The gems marked to leave for the inventory at KEEP.</summary>
    private readonly HashSet<Item> _ejecting = [];

    private int Stored => _panel.Selected?.Item.TotalNumberOfSockets ?? 0;

    [ObservableProperty] private bool _isOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(SocketsEditor) + nameof(IsOpen));

    partial void OnIsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(SocketsEditor) + nameof(IsOpen), value);

    [RelayCommand]
    private void Toggle()
    {
        _panel.Press();
        IsOpen = !IsOpen;
    }

    /// <summary>Hidden entirely on an item that has no sockets, rather than shown empty.</summary>
    public bool HasSockets => Rows.Count > 0;

    /// <summary>The most this base takes at this item level, or 0 for one that takes none.</summary>
    public int Maximum => _panel.Selected is { } tile ? _panel.MaximumSockets(tile) : 0;

    /// <summary>
    /// What the box holds its number to: <see cref="Maximum"/>, and never under what the item holds or has staged.
    /// A value is never corrected by being read (BasicEditor.DurabilityCeiling): an item over its limit - a level
    /// lowered, a corruption's count, a Legacy item, a base the tables do not know - came into a box held to the
    /// limit alone, which wrote the limit back as if it had been typed, and the sockets past it were kept off with
    /// what was in them.
    /// </summary>
    public int BoxMaximum => Math.Max(Maximum, Math.Max(Stored, Staged));

    /// <summary>
    /// Shown on any base that could be socketed, not only one that already is - a plain
    /// sword with no holes is exactly where boring one is worth doing.
    /// </summary>
    public bool CanSocket => !_panel.IsTabTile && Maximum > 0;

    public string Limits => L($"max {Maximum}");

    /// <summary>
    /// The fewest the count can be set to: none - or, where gems cannot come out (the vault's Edit page), as many
    /// as are filled, where - one at a time stops too.
    /// </summary>
    public int Minimum => _panel.Selected is { } tile && !_panel.CanEmptySockets ? tile.Item.SocketedItems.Count : 0;

    /// <summary>Whether the count can be typed: an item open, and no runeword staged on it - as + and - ask.</summary>
    public bool CanType => _panel.Selected is not null && !_panel.IsTabTile && _panel.Draft.Runeword is null;

    /// <summary>
    /// The count as the stepper's box shows it, and a count typed into it: staged like a press of + or -, held
    /// between <see cref="Minimum"/> and <see cref="BoxMaximum"/>. A box emptied on the way to another number stages
    /// nothing.
    /// </summary>
    public string Count
    {
        get => Staged.ToString();
        set
        {
            if (!CanType || !int.TryParse(value, out int typed))
                return;

            typed = Math.Clamp(typed, Minimum, Math.Max(Minimum, BoxMaximum));
            if (typed == Staged)
                return;

            Staged = typed;
            Build();
        }
    }

    /// <summary>
    /// The - button's tooltip, as the page it is on has it: where gems cannot come out (the vault's Edit page) a socket
    /// goes only empty, and Shift stops at the filled ones (<see cref="Jump"/>) - it said "no sockets" there too.
    /// </summary>
    public string RemoveHint => _panel.CanEmptySockets
        ? L("Remove socket") + Environment.NewLine + L("Its socketed item comes out") + Environment.NewLine + L("Shift + LMB: no sockets")
        : L("Remove socket") + Environment.NewLine + L("Shift + LMB: down to the filled ones");

    /// <summary>The + button's tooltip: one more, up to the most; Shift for the most at once.</summary>
    public string AddHint => L($"Add socket ({Limits})") + Environment.NewLine + L("Shift + LMB: max sockets");

    public bool CanAdd => _panel.Selected is not null && Staged < Maximum && _panel.Draft.Runeword is null;

    /// <summary>
    /// A socket can go: there is one, no runeword is staged - and, where gems cannot come out (the
    /// vault's Edit page), only while an empty one is left to take, so nothing would be ejected.
    /// </summary>
    public bool CanRemove => _panel.Selected is { } tile && Staged > 0 && _panel.Draft.Runeword is null
                             && (_panel.CanEmptySockets || Staged > tile.Item.SocketedItems.Count);

    /// <summary>Whether the socket rows offer unsocketing: never on the vault's Edit page.</summary>
    public bool CanEmpty => _panel.CanEmptySockets;

    public bool AreChanged =>
        _panel.Selected is not null
        && (Staged != Stored || _ejecting.Count > 0 || _deleting.Count > 0);

    public string Header => L($"Sockets  ({Rows.Count(socket => socket.IsFilled)} / {Rows.Count})");

    /// <summary>The gems that leave at KEEP: those marked, and any in a socket being closed.</summary>
    public IReadOnlyList<Item> Leaving(Item item) =>
        [.. item.SocketedItems.Where((gem, index) => _ejecting.Contains(gem) || _deleting.Contains(gem) || index >= Staged)];

    /// <summary>
    /// What <see cref="Commit"/> will come to, on a copy: the gems on their way out gone from it, the count
    /// as staged, and a runeword with no runes left no runeword. For the item's summary; nothing is moved.
    /// Left alone while a runeword is staged - the copy wears that word's own runes.
    /// </summary>
    public void Wear(Item shadow, Item real)
    {
        if (!AreChanged || _panel.Draft.Runeword is not null)
            return;

        var leaving = Leaving(real);
        if (shadow.SocketedItems.Count == real.SocketedItems.Count)
        {
            for (int at = real.SocketedItems.Count - 1; at >= 0; at--)
            {
                if (leaving.Contains(real.SocketedItems[at]))
                    shadow.SocketedItems.RemoveAt(at);
            }

            shadow.NumberOfSocketedItems = (byte)shadow.SocketedItems.Count;
        }

        shadow.TotalNumberOfSockets = (byte)Math.Max(Staged, shadow.SocketedItems.Count);
        shadow.IsSocketed = shadow.TotalNumberOfSockets > 0;

        if (shadow.IsRuneword && shadow.SocketedItems.Count < real.SocketedItems.Count)
            UndoRuneword(shadow);
    }

    /// <summary>
    /// The word taken off an item, its runes left as they lie: the flag and the id go, and so does the property
    /// list the runeword kept its own properties in - the writer only writes that list while the flag is set, and
    /// a list left behind would be written by nothing and read as the item's own. What each rune still in it
    /// gives is the rune's, as in any socketed base.
    /// </summary>
    private static void UndoRuneword(Item item)
    {
        item.IsRuneword = false;
        item.RunewordId = 0;

        while (item.StatLists.Count > 1)
            item.StatLists.RemoveAt(item.StatLists.Count - 1);
    }

    /// <summary>The count the item has, nothing marked: every discard and every new item starts here.</summary>
    public void Reset()
    {
        Staged = Stored;
        _ejecting.Clear();
        _deleting.Clear();
        _picked.Clear();
        _chosen = false;
        (_dropGem, _dropAt) = (null, null);
    }

    // ---- the strip's selection: there was no drag and drop or selection ------------
    // The sockets stand in a row, in the order they were filled, and one of them is selected by a click: the
    // tools over the row act on it, and a line under the row says what it is and what it gives this item. A
    // row a socket, with its own buttons and a fold for its effect, stood here until then.
    //
    // Several, for a fast eject or a drop into the item: Ctrl + click, Shift + click and a rectangle drawn from the section's
    // empty room, by the same hand as on the Items page and the vault (DragPick, through IDragPicker below).
    // The tools act on all that are selected; one of them dragged carries them all out. A double click opens a
    // jewel (SocketsSection.Socket_Click).
    //
    // Always one at least: the caption's room is fixed, and with nothing selected it stands too empty,
    // so there is no unselected state. With none chosen the first filled
    // socket is, else the first. The one selected alone, clicked again, is not let go, so the caption never
    // folds. The one selected so is not chosen: Del deletes the items of sockets the user selected, never of
    // that one (SocketCarry.DeleteSelection) - else Del on any socketed item would take its gem.

    /// <summary>The sockets selected, by their places in the row: kept while the rows are built again.</summary>
    private readonly SortedSet<int> _picked = [];

    /// <summary>Whether the user selected those - a click, the rectangle, a key - rather than the default one.</summary>
    private bool _chosen;

    /// <summary>The one selected, where it is one alone: what the caption says, and Edit jewel opens.</summary>
    public SocketViewModel? SelectedSocket => _picked.Count == 1 && _picked.Min < Rows.Count ? Rows[_picked.Min] : null;

    /// <summary>All that are selected, in the row's order.</summary>
    public IReadOnlyList<SocketViewModel> SelectedSockets => [.. _picked.Where(at => at < Rows.Count).Select(at => Rows[at])];

    /// <summary>A socket clicked: selected, alone. The one that is selected alone, clicked again, stays so.</summary>
    [RelayCommand]
    private void Select(SocketViewModel? socket)
    {
        _panel.Press();
        _picked.Clear();
        if (socket is not null && Rows.IndexOf(socket) is >= 0 and int at)
        {
            _picked.Add(at);
            _chosen = true;
        }
        MarkSelected();
    }

    /// <summary>Selected alone, whatever was: the right button's press, a double click, a drag begun on one not among those selected.</summary>
    public void SelectOnly(SocketViewModel socket)
    {
        _picked.Clear();
        if (Rows.IndexOf(socket) is >= 0 and int at)
        {
            _picked.Add(at);
            _chosen = true;
        }
        MarkSelected();
    }

    // The same hand as the Items page's and the vault's selection (DragPick): Ctrl and Shift on a socket, the
    // rectangle, a click on the section's empty room.
    bool IDragPicker.BeginDragPick(bool keepSelected) => _panel.Selected is not null && !IsCarrying;

    bool? IDragPicker.IsPicked(object item) => item is SocketViewModel socket && Rows.Contains(socket) ? socket.IsSelected : null;

    void IDragPicker.SetPicked(object item, bool picked)
    {
        if (item is not SocketViewModel socket || Rows.IndexOf(socket) is not (>= 0 and int at))
            return;

        if (picked)
            _picked.Add(at);
        else
            _picked.Remove(at);
        socket.IsSelected = picked;
        _chosen = true;
    }

    void IDragPicker.ClearPicks()
    {
        _picked.Clear();
        foreach (var row in Rows)
            (row.IsSelected, row.IsCaptioned) = (false, false);
    }

    void IDragPicker.ClickedEmpty()
    {
        _picked.Clear();
        MarkSelected();
    }

    void IDragPicker.EndPick() => MarkSelected();

    /// <summary>A plain drag off a socket: the section's own press has begun the carry by now, where gems come out - else it is a rectangle's start.</summary>
    bool IDragPicker.BeginCarry(object item) => IsCarrying;

    private void MarkSelected()
    {
        _picked.RemoveWhere(at => at >= Rows.Count);
        if (_picked.Count == 0 && Rows.Count > 0)
        {
            _picked.Add(Math.Max(0, Rows.ToList().FindIndex(row => row.IsFilled)));
            _chosen = false;
        }

        for (int at = 0; at < Rows.Count; at++)
        {
            Rows[at].IsSelected = _picked.Contains(at);
            Rows[at].IsCaptioned = _picked.Count == 1 && _picked.Contains(at);
        }

        OnPropertyChanged(nameof(CanCutSelected));

        DeleteArmed = false;
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteHint));
        var carry = PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared;
        if (_picked.Count > 0 && _chosen)
            carry.DeleteSelection = _deleteByKey;
        else if (ReferenceEquals(carry.DeleteSelection, _deleteByKey))
            carry.DeleteSelection = null;

        OnPropertyChanged(nameof(SelectedSocket));
        OnPropertyChanged(nameof(SelectedSockets));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CaptionName));
        OnPropertyChanged(nameof(CaptionBrush));
        OnPropertyChanged(nameof(CaptionEffect));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanCopySelected));
        OnPropertyChanged(nameof(CanUnsocketSelected));
        OnPropertyChanged(nameof(UnsocketHint));
    }

    public bool HasSelection => _picked.Count > 0;

    /// <summary>The line under the row: the selected socket's name, in its item's color - "Empty socket" for a bare one; with several, how many.</summary>
    public string CaptionName => SelectedSocket?.Name ?? (_picked.Count > 1 ? L($"{_picked.Count} sockets selected") : "");

    public Avalonia.Media.IBrush? CaptionBrush => SelectedSocket?.NameBrush ?? SocketViewModel.PlainBrush;

    /// <summary>And what it gives the item it is in, a line to each: what its row listed under a fold.</summary>
    public string CaptionEffect => SelectedSocket is { IsFilled: true } socket ? socket.EffectLines : "";

    /// <summary>The tools, for what is selected: dimmed with none, or where it is not theirs. Edit jewel is one jewel's.</summary>
    public bool CanEditSelected => SelectedSocket is { IsJewel: true };

    public bool CanCopySelected => SelectedSockets.Any(each => each.IsFilled) && CopiesSocketed;

    public bool CanCutSelected => SelectedSockets.Any(each => each is { IsFilled: true, IsGoing: false }) && CanEject;

    /// <summary>The clipboard changed: the socket that is cut is faint, the rest as they were.</summary>
    public void SayCut()
    {
        foreach (var row in Rows)
            row.SayCut();

        OnPropertyChanged(nameof(CanPasteHere));
    }

    public bool CanUnsocketSelected => SelectedSockets.Any(each => each.IsFilled) && _panel.CanEmptySockets;

    /// <summary>Unsocket, or for what is marked to leave already, the mark taken back.</summary>
    public string UnsocketHint =>
        SelectedSockets.Where(each => each.IsFilled).ToList() is { Count: > 0 } filled && filled.All(each => each.IsGoing) ? L("Keep socketed") : L("Unsocket");

    // ---- the tools, for what is selected ---------------------------------------------------------------------------

    [RelayCommand]
    private void EditSelected() => OpenJewel(SelectedSocket);

    /// <summary>Copy, for the sockets selected: what is in them held to be pasted as seconds of themselves (<see cref="CopySocketed"/>).</summary>
    [RelayCommand]
    private void CopySelected()
    {
        var gems = SelectedSockets.Select(each => each.Gem).OfType<Item>().ToList();
        if (gems.Count == 0 || _panel.Selected is null || !CopiesSocketed)
            return;

        _panel.Press();
        Clip.HoldItems(PD2SPCompanion.Shell.Services.Generic.ClipKind.Copy, gems);
        _panel.Say(Plural(gems.Count, "1 item copied. Paste to place it.", $"{gems.Count} items copied. Paste to place them."));
    }

    /// <summary>Cut, for the sockets selected: marked, and turned out together by the paste (<see cref="CutSocketed"/>).</summary>
    [RelayCommand]
    private void CutSelected()
    {
        var gems = SelectedSockets.Where(each => each is { IsFilled: true, IsGoing: false }).Select(each => each.Gem!).ToList();
        if (gems.Count == 0 || _panel.Selected is not { } tile || !CanEject)
            return;

        _panel.Press();
        Clip.HoldSocketed(tile.Item, gems);
        _panel.Say(Plural(gems.Count, "1 item cut. Paste to move it.", $"{gems.Count} items cut. Paste to move them."));
    }

    /// <summary>Unsocket, for the sockets selected: all of them marked to leave - or, all marked already, kept.</summary>
    [RelayCommand]
    private void UnsocketSelected() => MarkLeaving([.. SelectedSockets.Where(each => each.IsFilled)]);

    /// <summary>
    /// Paste, into the item's sockets:
    /// live while the clipboard holds gems, runes or jewels and the item has a socket for the first - a cut goes
    /// in, a copy's second goes in. Into the next socket, whichever is selected: the game has no gap between two.
    /// Where gems come out, they go in: the character's Edit page.
    /// </summary>
    public bool CanPasteHere => _panel.CanEmptySockets && _panel.Selected is { } tile && Clip.FitsSockets?.Invoke(tile) == true;

    [RelayCommand]
    private void PasteHere()
    {
        if (!CanPasteHere || _panel.Selected is not { } tile)
            return;

        _panel.Press();
        Clip.PasteIntoSockets?.Invoke(tile);
    }

    /// <summary>Stages the sockets a confirmed copy has - a corruption bores them, a clean closes them.</summary>
    public void StageFrom(Item shadow)
    {
        Staged = shadow.TotalNumberOfSockets;
        _ejecting.Clear();
        _deleting.Clear();
        Build();
    }

    /// <summary>The rows built again from the item and what is staged, and everything they show told again.</summary>
    public void Build()
    {
        Rows.Clear();

        if (_panel.Selected is { } tile)
        {
            var gems = tile.Item.SocketedItems;

            for (int n = 0; n < Math.Max(Stored, Staged); n++)
            {
                var gem = n < gems.Count ? gems[n] : null;

                Rows.Add(new SocketViewModel(gem, tile.Item)
                {
                    // Past the staged count is a socket on its way out, and a gem inside
                    // one of those is leaving whether it was asked to or not.
                    IsGoing = n >= Staged || (gem is not null && _ejecting.Contains(gem)),
                });
            }
        }

        MarkSelected();
        Notify();
        _panel.Changed();
    }

    /// <summary>Everything the section shows, told again.</summary>
    public void Notify()
    {
        OnPropertyChanged(nameof(HasSockets));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(CanSocket));
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(BoxMaximum));
        OnPropertyChanged(nameof(Limits));
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(CanType));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanEmpty));
        OnPropertyChanged(nameof(CopiesSocketed));
        OnPropertyChanged(nameof(CanUnsocketAll));
        OnPropertyChanged(nameof(UnsocketAllHint));
        OnPropertyChanged(nameof(CanPasteHere));
        OnPropertyChanged(nameof(AreChanged));
        OnPropertyChanged(nameof(CanRoll));
        OnPropertyChanged(nameof(RollHint));

        // The toolbar over the selected sockets: the play mode decides these as well as the selection.
        OnPropertyChanged(nameof(CanCutSelected));
        OnPropertyChanged(nameof(CanCopySelected));
        OnPropertyChanged(nameof(DeletesGems));
        OnPropertyChanged(nameof(CanDeleteSelected));
    }

    /// <summary>
    /// Carries out what the rows have been showing, at KEEP: every gem marked to leave, and every
    /// gem in a socket being closed, goes to the inventory, and then the count changes.
    /// <para>
    /// A gem with nowhere to go stays where it is, and the socket stays with it. Losing an
    /// item to a button press is not something an editor should do quietly.
    /// </para>
    /// </summary>
    public void Commit(ItemTileViewModel tile)
    {
        if (!AreChanged)
            return;

        // Read before anything moves. Unsocketing rebuilds the page, which replaces the
        // selected tile - and Maximum is worked out from that selection, so asking it
        // afterwards gave 0 and clamped every socket away.
        int most = Math.Max(Maximum, tile.Item.TotalNumberOfSockets);
        int wanted = Staged;
        int runes = tile.Item.SocketedItems.Count;

        // The one let go of on a cell, or pasted on one, goes first: the rest land about it (ItemLanding.Wish), and
        // in socket order one of them took the cell first and put it beside.
        int deleted = 0;
        string? deletedName = null;
        foreach (var gem in Leaving(tile.Item).OrderBy(gem => ReferenceEquals(gem, _dropGem) ? 0 : 1).ToList())
        {
            // Deleted: out of the item and nowhere else (DeleteSelected).
            if (_deleting.Contains(gem))
            {
                tile.Item.SocketedItems.Remove(gem);
                if (!tile.Item.IsCompact)
                    tile.Item.NumberOfSocketedItems = (byte)tile.Item.SocketedItems.Count;
                ItemForge.Renumber(tile.Item);
                deletedName = PD2SPCompanion.Core.Items.ItemPresenter.Describe(gem).Name;
                deleted++;
                continue;
            }

            // Onto the cell it was let go of on, or pasted on - one gem's; the rest where they land.
            string? refused = ReferenceEquals(gem, _dropGem) && _dropAt is { } cell ? _panel.EjectTo(tile, gem, cell) : _panel.Eject(tile, gem);
            if (refused is not null)
            {
                // It could not be placed, so the socket holding it has to stay.
                wanted = Math.Max(wanted, tile.Item.SocketedItems.Count);
                break;
            }
        }

        tile.Item.TotalNumberOfSockets = (byte)Math.Clamp(
            wanted, tile.Item.SocketedItems.Count, Math.Max(most, tile.Item.SocketedItems.Count));
        tile.Item.IsSocketed = tile.Item.TotalNumberOfSockets > 0;

        // A runeword less a rune is not a runeword: only the word goes, and the runes stay in the base like an unfinished runeword, a base with some runes for some stats -
        // what the game itself holds where runes spell no word. Until then one rune out took them all.
        if (tile.Item.IsRuneword && tile.Item.SocketedItems.Count < runes)
            UndoRuneword(tile.Item);

        _ejecting.Clear();
        _deleting.Clear();
        (_dropGem, _dropAt) = (null, null);
        Staged = tile.Item.TotalNumberOfSockets;
        if (deleted > 0)
            _panel.Say(deleted == 1 ? L($"{deletedName} deleted.") : L($"{deleted} socketed items deleted."));

        // Several selected and gone: their places are other sockets' now, which nobody selected.
        if (_picked.Count > 1 && tile.Item.SocketedItems.Count < runes)
            _picked.Clear();
    }

    /// <summary>
    /// What the die rolls over (<see cref="SocketRolls"/>; the die and the Puzzlebox's
    /// button are one): the range of what gave the item its sockets - its own row or affix, its corruption - and
    /// the Puzzlebox's on everything else. The whole of it, whatever is socketed: a roll under that closes sockets
    /// as - does, and what was in them leaves for the inventory at KEEP, shown going until then. Held to
    /// <see cref="Minimum"/> where gems cannot come out. Null on a runeword, staged or made: its count is its runes'.
    /// </summary>
    private SocketRolls.Range? Rolls
    {
        get
        {
            if (_panel.Selected is not { } tile || _panel.IsTabTile || _panel.Draft.Runeword is not null || tile.Item.IsRuneword
                || SocketRolls.For(tile.Item) is not { } range)
            {
                return null;
            }

            int least = Math.Max(range.Least, Minimum);
            return least <= range.Most ? range with { Least = least } : null;
        }
    }

    /// <summary>Shift on the die: one to the most the base takes, as the cube's socket recipes roll a plain base.</summary>
    private (int Least, int Most)? Whole
    {
        get
        {
            if (Rolls is null || _panel.Selected is not { } tile || SocketRolls.Whole(tile.Item) is not { } whole)
                return null;

            int least = Math.Max(whole.Least, Minimum);
            return least <= whole.Most ? (least, whole.Most) : null;
        }
    }

    /// <summary>Whether the die has anything to roll: an item that takes sockets, and no runeword on it.</summary>
    public bool CanRoll => Rolls is not null;

    /// <summary>Whether the count is the item's own to roll, as a stat is: what the Demonic Cube rolls with the rest.</summary>
    public bool RollsItsOwn => Rolls is { From: SocketRolls.Source.Own } range && range.Least < range.Most;

    /// <summary>The die's tooltip: what a click rolls and where that comes from, then Shift's range, by its numbers.</summary>
    public string? RollHint
    {
        get
        {
            if (Rolls is not { } range)
                return null;

            string within = range.From switch
            {
                SocketRolls.Source.Puzzlebox => L($"Reroll within {range.Least}-{range.Most} (Puzzlebox)"),
                SocketRolls.Source.Corruption => L($"Reroll within {range.Least}-{range.Most} (corruption)"),
                _ => L($"Reroll within {range.Least}-{range.Most}"),
            };

            return Whole is { } whole && (whole.Least, whole.Most) != (range.Least, range.Most)
                ? within + Environment.NewLine + L($"Shift + LMB: within {whole.Least}-{whole.Most}")
                : within;
        }
    }

    /// <summary>
    /// The die: a socket count rolled evenly inside the range of its source, staged as + and - stage theirs. The
    /// game rolls each once - its Puzzlebox takes only an item with none, its Demonic Cube leaves the count.
    /// </summary>
    [RelayCommand]
    private void RollSockets() => RollAndSay(whole: false);

    /// <summary>Shift on the die: one to the most the base takes.</summary>
    public void RollWhole() => RollAndSay(whole: true);

    private void RollAndSay(bool whole)
    {
        if (_panel.Selected is not { } tile || Rolls is null)
            return;

        _panel.Press();

        if (Reroll(Random.Shared, whole) is { } rolled)
            _panel.Say(L($"{tile.Name}: {rolled} sockets."));
    }

    /// <summary>
    /// The count rolled again and staged - the die's work, and the Demonic Cube's part of it where the count is
    /// the item's own (<see cref="RollsItsOwn"/>; our demonic cube also rerolls the sockets,
    /// the game's leaves the count). The count it landed on, or null where nothing rolls.
    /// </summary>
    public int? Reroll(Random random, bool whole = false)
    {
        // The game's odds: even inside the range, but a corruption's sockets by its outcomes' chances
        // (SocketRolls.Roll). Shift's one to the most is even, as the cube's socket recipes roll it.
        int rolled;
        if (whole)
        {
            if (Whole is not { } span)
                return null;
            rolled = random.Next(span.Least, span.Most + 1);
        }
        else
        {
            if (Rolls is not { } range || _panel.Selected is not { } tile)
                return null;
            rolled = SocketRolls.Roll(tile.Item, range, random);
        }
        if (rolled != Staged)
        {
            Staged = rolled;
            Build();
        }

        return rolled;
    }

    /// <summary>A normal or superior base, no runeword, nothing socketed, room for one: what <see cref="DropRoll"/> rolls.</summary>
    public bool CanDropRoll =>
        _panel.Selected is { } tile && _panel.Draft.Runeword is null && !tile.Item.IsRuneword
        && tile.Item.SocketedItems.Count == 0 && !Rows.Any(socket => socket.IsFilled) && Maximum >= 1
        && (_panel.Draft.Quality ?? tile.Item.Quality) is ItemQuality.Normal or ItemQuality.Superior;

    /// <summary>
    /// The sockets a normal or superior base drops with - the Forge's Shift roll: one in three is socketed, with one to the most the base takes at its item
    /// level, each alike (vanilla's Item Generation Tutorial; PD2 left as vanilla). Never on a runeword, whose runes
    /// fix the count, nor on an item holding anything socketed. The count it landed on, or null where it does not roll.
    /// </summary>
    public int? DropRoll(Random random)
    {
        if (!CanDropRoll)
            return null;

        int rolled = random.Next(3) == 0 ? random.Next(1, Maximum + 1) : 0;
        if (rolled != Staged)
        {
            Staged = rolled;
            Build();
        }

        return rolled;
    }

    [RelayCommand]
    private void Add()
    {
        if (_panel.Selected is null || Staged >= Maximum)
            return;

        _panel.Press();
        Staged++;
        Build();
    }

    /// <summary>
    /// Shift on a stepper: + to the most sockets the item takes, - to none - or,
    /// where gems cannot come out (the vault's Edit page), to as many as are filled, as - one at a time stops.
    /// </summary>
    public void Jump(bool up)
    {
        if (_panel.Selected is not { } tile || _panel.Draft.Runeword is not null)
            return;

        int target = up ? Maximum : _panel.CanEmptySockets ? 0 : tile.Item.SocketedItems.Count;
        if (target == Staged || (up && target < Staged))
            return;

        _panel.Press();
        Staged = target;
        Build();
    }

    [RelayCommand]
    private void Remove()
    {
        if (!CanRemove)
            return;

        _panel.Press();
        Staged--;
        Build();
    }

    /// <summary>Whether there is anything for Unsocket all to take: a filled socket, on a page gems can leave.</summary>
    public bool CanUnsocketAll => _panel.CanEmptySockets && Rows.Any(socket => socket.IsFilled);

    /// <summary>What Unsocket all will do: mark every one, or - with every one marked - keep them.</summary>
    public string UnsocketAllHint =>
        _panel.Selected is { } tile && tile.Item.SocketedItems.Count > 0 && tile.Item.SocketedItems.All(_ejecting.Contains)
            ? L("Keep all socketed")
            : L("Unsocket all");

    /// <summary>
    /// Marks every gem, rune and jewel to leave for the inventory when the item is kept, or - all of
    /// them marked already - none (one press rather than one per socket). On a
    /// runeword that is what a single unsocket already does.
    /// </summary>
    [RelayCommand]
    private void UnsocketAll()
    {
        if (_panel.Selected is not { } tile || !CanUnsocketAll)
            return;

        _panel.Press();

        // The runes of a word being replaced or cleaned are the builder's to settle.
        if (_panel.Draft.Runeword is not null)
        {
            _panel.Say(L("Finish the runeword first."));
            return;
        }

        bool going = tile.Item.SocketedItems.All(_ejecting.Contains);
        foreach (var gem in tile.Item.SocketedItems)
        {
            if (going)
                _ejecting.Remove(gem);
            else
                _ejecting.Add(gem);
        }

        if (Runewords.On(tile.Item) is { } word)
        {
            string named = Runewords.GameName(word);
            _panel.Say(going
                ? L($"{named} kept.")
                : L($"{named} undone. Runes come out, {tile.View.BaseName} remains."));
        }

        Build();
    }

    /// <summary>Whether a jewel's row offers "Edit jewel" here: everywhere but the Forge.</summary>
    public bool OpensJewels => _panel.OpensSocketed;

    /// <summary>Whether a filled socket's row offers Duplicate: the character's Items page, where the play mode copies.</summary>
    public bool CopiesSocketed => _panel.CopiesSocketed;

    /// <summary>
    /// Copy, for the socket selected: what is in it held on the clipboard, to be pasted as a second of itself -
    /// on the Items page, in a vault folder - where the play mode makes seconds. The item keeps its own.
    /// </summary>
    [RelayCommand]
    private void CopySocketed(SocketViewModel? socket)
    {
        if (socket?.Gem is not { } gem || _panel.Selected is null || !CopiesSocketed)
            return;

        _panel.Press();
        Clip.HoldItems(PD2SPCompanion.Shell.Services.Generic.ClipKind.Copy, [gem]);
        _panel.Say(L("1 item copied. Paste to place it."));
    }

    private static PD2SPCompanion.Shell.Services.Generic.ItemClipboard Clip => PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared;

    /// <summary>
    /// Cut, for the socket selected: marked - faint in the row - and turned out by the paste, where the paste is
    /// made: a cell or a page of the Items page, a folder of the vault. Nothing leaves until then; Esc lets it go.
    /// </summary>
    [RelayCommand]
    private void CutSocketed(SocketViewModel? socket)
    {
        if (socket?.Gem is not { } gem || _panel.Selected is not { } tile || !CanEject)
            return;

        _panel.Press();
        Clip.HoldSocketed(tile.Item, gem);
        _panel.Say(L("1 item cut. Paste to move it."));
    }

    /// <summary>Whether gems come out here at all: the play mode's rule, the page's, and no runeword being built.</summary>
    private bool CanEject => _panel.CanEmptySockets && Rules.EditsSockets && _panel.Draft.Runeword is null;

    /// <summary>
    /// A gem out of the item now, onto a cell or - with none - where what arrives lands: a drag out of the row let
    /// go of, or a cut pasted. As Unsocket: marked, and the page keeps the change at once. False where it is not
    /// this item's, or gems do not come out here.
    /// </summary>
    public bool Eject(Item gem, GridCell? at) => EjectAll([gem], at);

    /// <summary>Several out together - the sockets selected, dragged out or cut and pasted: the first onto the cell, the rest where they land. One change kept.</summary>
    public bool EjectAll(IReadOnlyList<Item> gems, GridCell? at)
    {
        if (_panel.Selected is not { } tile || gems.Count == 0 || !gems.All(tile.Item.SocketedItems.Contains) || !CanEject)
            return false;

        (_dropGem, _dropAt) = (gems[0], at);
        var leaving = Rows.Where(row => row.Gem is { } gem && gems.Contains(gem) && !_ejecting.Contains(gem)).ToList();
        if (leaving.Count > 0)
            MarkLeaving(leaving);

        return true;
    }

    /// <summary>The gem on its way to a named cell, and the cell: the next <see cref="Commit"/>'s.</summary>
    private Item? _dropGem;

    private GridCell? _dropAt;

    // ---- dragged out of the row --------------------------------------------------------------------------------------

    /// <summary>The socket being dragged out of the row, while it is carried. The view begins and ends it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarrying))]
    private SocketViewModel? _carried;

    public bool IsCarrying => Carried is not null;

    /// <summary>A drag has left a filled socket: carried, where gems come out here. It is selected by it.</summary>
    public bool BeginCarry(SocketViewModel socket)
    {
        if (socket is not { IsFilled: true, IsGoing: false, Gem: { } gem } || !CanEject || IsCarrying)
            return false;

        // One of several selected carries them all, as a pick dragged on the Items page carries the picks; any
        // other is selected alone by it.
        if (!(socket.IsSelected && _picked.Count > 1))
            SelectOnly(socket);

        _carriedWith = [.. SelectedSockets.Where(each => each is { IsFilled: true, IsGoing: false } && !ReferenceEquals(each, socket)).Select(each => each.Gem!)];
        Carried = socket;
        PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.Begin([gem, .. _carriedWith], () => EndCarry(null, cancel: true));
        return true;
    }

    /// <summary>What goes with the socket carried: the rest of those selected.</summary>
    private IReadOnlyList<Item> _carriedWith = [];

    /// <summary>
    /// The carry ended: let go of on a free cell of the Items page, there; anywhere else off the section, where
    /// what arrives lands; back on the section, or called off, nothing.
    /// </summary>
    public void EndCarry(GridCell? at, bool cancel)
    {
        var socket = Carried;
        var with = _carriedWith;
        Carried = null;
        _carriedWith = [];
        PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.End();

        if (!cancel && socket?.Gem is { } gem)
            EjectAll([gem, .. with], at);
    }

    /// <summary>
    /// The carry let go of on another item that takes it (the Items page's to decide, and to put them in): what is
    /// carried, in the row's order, handed to <paramref name="into"/>. Its answer; the carry is over either way.
    /// </summary>
    public bool EndCarryInto(Func<IReadOnlyList<Item>, bool> into)
    {
        if (Carried?.Gem is not { } gem)
            return false;

        var carried = new List<Item> { gem };
        carried.AddRange(_carriedWith);
        var ordered = Rows.Where(row => row.Gem is { } each && carried.Contains(each)).Select(row => row.Gem!).ToList();

        Carried = null;
        _carriedWith = [];
        PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.End();
        return into(ordered);
    }

    // ---- dropped on the section ---------------------------------------------------------------------------------------

    /// <summary>The item the section is of: what a gem dropped on the section goes into.</summary>
    public ItemTileViewModel? Host => _panel.Selected;

    /// <summary>A gem, a rune or a jewel the Items page carries is over the section and would go in: the section is lit.</summary>
    [ObservableProperty] private bool _isDropTarget;

    // ---- a socket's menu ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The menu of a socket of the row - selected by the press, as any right click selects: Unsocket first, Edit
    /// jewel for a jewel, Unsocket all; Cut and Copy at its head. What the page or the play mode forbids is not in
    /// it; an empty socket has what is done to them all, or nothing.
    /// </summary>
    public ActMenu MenuFor(SocketViewModel socket)
    {
        var icons = new List<MenuAct>();
        var rows = new List<MenuAct>();
        bool leaves = _panel.CanEmptySockets && Rules.EditsSockets;

        // One of several selected: the menu is theirs, as an item's menu is the picks' on the Items page.
        if (socket.IsSelected && _picked.Count > 1)
        {
            int filled = SelectedSockets.Count(each => each.IsFilled);
            if (filled > 0 && leaves)
            {
                icons.Add(new(L("Cut"), MenuGlyph.Cut, () => CutSelectedCommand.Execute(null)) { IsEnabled = CanCutSelected });
                rows.Add(new(ActMenu.Counted(UnsocketHint, filled), MenuGlyph.Upload, () => UnsocketSelectedCommand.Execute(null)) { IsMain = true });
            }

            if (filled > 0 && CopiesSocketed)
                icons.Add(new(L("Copy"), MenuGlyph.Duplicate, () => CopySelectedCommand.Execute(null)));

            if (_panel.CanEmptySockets)
                icons.Add(PasteAct());

            if (filled > 0 && DeletesGems)
                icons.Add(DeleteAct());

            if (leaves && CanUnsocketAll)
                rows.Add(new(UnsocketAllHint, MenuGlyph.Upload, () => UnsocketAllCommand.Execute(null)) { RuleAbove = rows.Count > 0 });

            return new ActMenu(icons, rows);
        }

        if (socket.IsFilled && leaves)
        {
            icons.Add(new(L("Cut"), MenuGlyph.Cut, () => CutSocketedCommand.Execute(socket)) { IsEnabled = CanEject });
            rows.Add(new(socket.IsGoing ? L("Keep socketed") : L("Unsocket"), MenuGlyph.Upload, () => UnsocketCommand.Execute(socket)) { IsMain = true });
        }

        if (socket.IsFilled && CopiesSocketed)
            icons.Add(new(L("Copy"), MenuGlyph.Duplicate, () => CopySocketedCommand.Execute(socket)));

        if (_panel.CanEmptySockets)
            icons.Add(PasteAct());

        if (socket.IsFilled && DeletesGems)
            icons.Add(DeleteAct());

        if (socket.IsJewel && OpensJewels)
            rows.Add(new(L("Edit jewel"), MenuGlyph.Open, () => OpenJewelCommand.Execute(socket)));

        if (leaves && CanUnsocketAll)
            rows.Add(new(UnsocketAllHint, MenuGlyph.Upload, () => UnsocketAllCommand.Execute(null)) { RuleAbove = rows.Count > 0 });

        return new ActMenu(icons, rows);
    }

    // ---- deleted outright: the section lacked a direct delete of a socket's item --------------
    //
    // Gone from the item and from the character: no unsocket to the inventory and a delete there. A bin, so it asks -
    // twice on the button, Confirm in the menu, as the Items page's bin does. Where gems come out at all - not the
    // vault's Edit page, where nothing takes them out. Del deletes the selected sockets' items
    // while a socket is selected - never the item they are in (SocketCarry.DeleteSelection).

    private readonly HashSet<Item> _deleting = [];

    /// <summary>The bin's first press asks; the second deletes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    private bool _deleteArmed;

    /// <summary>Whether socketed items are deleted here: where they come out and the play mode edits sockets.</summary>
    public bool DeletesGems => _panel.CanEmptySockets && Rules.EditsSockets;

    public bool CanDeleteSelected =>
        DeletesGems && _panel.Draft.Runeword is null && SelectedSockets.Any(each => each is { IsFilled: true, IsGoing: false });

    public string DeleteHint => DeleteArmed
        ? L("Confirm deletion") + Environment.NewLine + L("Click elsewhere to cancel")
        : ShortcutText.With(L("Delete selected socketed item"), L("Del"));

    [RelayCommand]
    private void DeleteSelected()
    {
        var going = SelectedSockets.Where(each => each is { IsFilled: true, IsGoing: false }).Select(each => each.Gem!).ToList();
        if (going.Count == 0 || !CanDeleteSelected)
            return;

        _panel.Press();
        if (!DeleteArmed)
        {
            DeleteArmed = true;
            _panel.Say(going is [var one]
                ? L($"Delete {PD2SPCompanion.Core.Items.ItemPresenter.Describe(one).Name}? Click red tick again to confirm.")
                : L($"Delete {going.Count} socketed items? Click red tick again to confirm."));
            return;
        }

        DeleteArmed = false;
        _deleting.UnionWith(going);
        _picked.Clear();
        Build();
    }

    /// <summary>Del, from the page that heard it: the selected sockets' items, where there are any. False where not.</summary>
    private bool DeleteByKey()
    {
        if (!CanDeleteSelected)
            return false;

        DeleteSelectedCommand.Execute(null);
        return true;
    }

    private readonly Func<bool> _deleteByKey;

    /// <summary>Delete, in a socket's menu: asked in the menu, so by the time it runs the answer is yes.</summary>
    private MenuAct DeleteAct() => new(L("Delete"), MenuGlyph.Bin, () =>
    {
        DeleteArmed = true;
        DeleteSelectedCommand.Execute(null);
    })
    { Keys = L("Del"), IsBin = true, Confirm = L("Confirm"), IsEnabled = CanDeleteSelected };

    /// <summary>Paste, in a socket's menu: what the tools' Paste does - dimmed, saying what is held, where that goes into no socket.</summary>
    private MenuAct PasteAct() => new(L("Paste"), MenuGlyph.Paste, () => PasteHereCommand.Execute(null))
    {
        Keys = L("Ctrl + V"), IsEnabled = CanPasteHere, Why = CanPasteHere ? Clip.Says : "",
    };

    /// <summary>
    /// A jewel of the item's sockets opened to be edited where it lies: the panel's
    /// second layer, with a KEEP and an UNDO of its own. A gem or a rune has nothing to edit.
    /// </summary>
    [RelayCommand]
    private void OpenJewel(SocketViewModel? socket)
    {
        if (socket is { IsJewel: true, Gem: { } jewel })
            _panel.OpenSocketed(jewel);
    }

    /// <summary>
    /// Marks one gem to leave for the inventory when the item is kept - or, marked already, takes the mark back.
    /// <para>
    /// A runeword is its runes: it is found by them, and the game has no such thing as one with a rune missing.
    /// Taking one out undoes the word and leaves the others in the base as plain runes -
    /// it took them all out until then, as the cube's own wash recipe empties the base.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Unsocket(SocketViewModel? socket) => MarkLeaving(socket is null ? [] : [socket]);

    /// <summary>The same for several at once - the sockets selected: all marked to leave, or - all marked already - all kept. One change.</summary>
    private void MarkLeaving(IReadOnlyList<SocketViewModel> sockets)
    {
        var gems = sockets.Select(each => each.Gem).OfType<Item>().ToList();
        if (gems.Count == 0 || _panel.Selected is not { } tile || !_panel.CanEmptySockets)
            return;

        _panel.Press();

        // The runes of a word being replaced or cleaned are the builder's to settle.
        if (_panel.Draft.Runeword is not null)
        {
            _panel.Say(L("Finish the runeword first."));
            return;
        }

        if (gems.All(_ejecting.Contains))
            _ejecting.ExceptWith(gems);
        else
            _ejecting.UnionWith(gems);

        // A rune of a runeword: the word goes with it, and the other runes stay where they are.
        if (Runewords.On(tile.Item) is { } word)
        {
            string named = Runewords.GameName(word);
            _panel.Say(tile.Item.SocketedItems.Any(_ejecting.Contains)
                ? L($"{named} undone. Its other runes stay in {tile.View.BaseName}.")
                : L($"{named} kept."));
        }

        Build();
    }
}
