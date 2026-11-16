using System.Collections.Generic;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// What the item panel holds and has not written yet. Every editor stages into this one object;
/// the panel's preview reads it, KEEP writes it and UNDO drops it. The editors' pending fields move in as each editor
/// becomes a view model of its own.
/// </summary>
public sealed class ItemDraft
{
    /// <summary>
    /// The corruption: null for no change, zero for cleaned, otherwise the id it will carry. The last
    /// piece of the item that had a write path of its own: the panel used to put its answer straight
    /// onto the item and rebuild, which threw away every other staged edit without saying so.
    /// </summary>
    public int? Corruption { get; set; }

    /// <summary>
    /// The desecration: null for no change, zero for cleaned, otherwise the value <c>desecrated</c>
    /// will carry. Staged as the corruption is, for the same reason.
    /// </summary>
    public int? Desecration { get; set; }

    /// <summary>The mod the staged desecration wrote - the file keeps no record of it.</summary>
    public Corruptions.CorruptionOption? DesecrationMod { get; set; }

    /// <summary>
    /// The craft family settled on and not yet written. Nothing writes it at KEEP - a family is
    /// the properties its recipe left, staged like any other - it is kept so the panel names the
    /// family the moment it is confirmed rather than the one the file still holds.
    /// </summary>
    public string? CraftFamily { get; set; }

    /// <summary>
    /// The item as a craft made from it in place will be, until KEEP: its quality, level, name and
    /// affix slots are copied from here, its properties by the rows as any edit's.
    /// </summary>
    public Item? CraftFrom { get; set; }

    /// <summary>
    /// A runeword change staged in place, consistent with every other staged edit: a
    /// word and the list its properties will be - the same list the pending rows edit - or no word
    /// at all, the runeword cleaned back to its white base.
    /// </summary>
    public (Runewords.Word? Word, ItemStatList? List)? Runeword { get; set; }

    /// <summary>
    /// The base one tier up, from the Horadric Cube's upgrade, until KEEP. The item's code is what
    /// the whole panel reads the base from, so the staged copy wears it until then.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// The quality the base is given, until KEEP writes it: Superior for a normal item made superior,
    /// whose bonus is chosen after from the Superior line - or, on a runeword the Forge
    /// is making, Normal again when that line is set to none. Null leaves the quality be.
    /// </summary>
    public ItemQuality? Quality { get; set; }

    /// <summary>
    /// The automagic row chosen on its line (AutomagicEditor), until KEEP: null for no change, zero for none,
    /// otherwise the row's id. Its properties are the rows', staged as any edit's.
    /// </summary>
    public int? AutoAffix { get; set; }

    /// <summary>The affix ids settled on in the picker, by slot and side, until KEEP writes them.</summary>
    public Dictionary<(int Slot, bool Prefix), int> AffixIds { get; } = [];

    /// <summary>Everything dropped, for a rebuild or UNDO.</summary>
    public void Clear()
    {
        Corruption = null;
        Code = null;
        Desecration = null;
        DesecrationMod = null;
        CraftFamily = null;
        CraftFrom = null;
        Runeword = null;
        Quality = null;
        AutoAffix = null;
        AffixIds.Clear();
    }
}

/// <summary>What an item editor needs from the panel it sits on.</summary>
public interface IItemPanel
{
    /// <summary>The item being edited, or null.</summary>
    ItemTileViewModel? Selected { get; }

    ItemDraft Draft { get; }

    /// <summary>
    /// A copy of the item with everything staged on it - what KEEP would write - and, unless
    /// <paramref name="withPicks"/> is false, the affix picker's picks while it is open.
    /// </summary>
    Item Staged(Item item, bool withPicks = true);

    /// <summary>
    /// <see cref="Staged"/>, but a copy of the caller's own, to write to: within one pass of the page's questions
    /// <see cref="Staged"/> hands every question the same copy (VP_ViewModel_Inv_R.OnePass).
    /// </summary>
    Item StagedToChange(Item item, bool withPicks = true);

    /// <summary>The property rows, where a confirmed number lives until KEEP.</summary>
    IReadOnlyList<StatRowViewModel> StatRows { get; }

    /// <summary>The rare name's editor, whose name the named-affix list shows.</summary>
    RareNameEditor RareName { get; }

    /// <summary>Whether anything is staged on the panel and not yet on the item.</summary>
    bool HasChanges { get; }

    /// <summary>A copy to confirm an editor's plan onto, or null when there is no item.</summary>
    Item? Pending();

    /// <summary>Takes a confirmed copy's properties into the panel's rows, staged.</summary>
    void StageStatsFrom(Item shadow, ItemTileViewModel tile);

    /// <summary>Something was staged: KEEP, UNDO and the rows are told again.</summary>
    void Changed();

    /// <summary>Says what an action came to, on the right page's strip.</summary>
    void Say(string text);

    /// <summary>The button sound.</summary>
    void Press();

    /// <summary>Scrolls each named list to the row it is sitting on.</summary>
    void Reveal(params string[] lists);

    /// <summary>The open character's level, which the cube's recipes read; null without one.</summary>
    int? CharacterLevel { get; }

    /// <summary>Shows a new item's own level and name as pending, as a craft made in place brings them.</summary>
    void ShowBasicsOf(Item shadow);

    /// <summary>Stages a stored defence, as typing it into the Basic section's box would: a superior Enhanced Defense brings one.</summary>
    void StageDefense(int defense);

    /// <summary>Stages a current durability, as typing it into the Basic section's box would: a full item stays full when a bonus raises its maximum.</summary>
    void StageDurability(int durability);

    /// <summary>Whether the host keeps every change as it is made: nothing then waits on KEEP, and no message should say so.</summary>
    bool KeepsAtOnce { get; }

    /// <summary>
    /// Whether the panel offers to make the item into another kind of item - a craft of a magic or rare
    /// one, a runeword of a socketed base, a superior of a normal one. Not where the page chose the kind
    /// itself (<see cref="Services.Character.IItemEditHost.ChoosesItsItem"/>): the Forge.
    /// </summary>
    bool Transforms { get; }

    /// <summary>The rare name was confirmed: the named affixes' list is built again.</summary>
    void NameChanged();

    /// <summary>
    /// The affixes were changed under the panel: their rows told again, and with <paramref name="rebuild"/>
    /// the slot pairs built again too, as a craft made in place needs.
    /// </summary>
    void AffixesChanged(bool rebuild);

    /// <summary>The socket count staged, which tells the socket corruptions apart.</summary>
    int StagedSockets { get; }

    /// <summary>Stages the sockets a confirmed copy has, as the socket section would.</summary>
    void StageSocketsFrom(Item shadow);

    /// <summary>Whether the item is a count in the materials tab rather than an item.</summary>
    bool IsTabTile { get; }

    /// <summary>The most sockets this base takes at its item level, as the session reckons it.</summary>
    int MaximumSockets(ItemTileViewModel tile);

    /// <summary>Whether gems can come out here: never on the vault's Edit page.</summary>
    bool CanEmptySockets { get; }

    /// <summary>
    /// A worked-out copy would close a socket that holds something, where gems cannot come out: a change that can never
    /// be written, so an editor refuses it at once rather than say it was done and have it dropped on the next selection.
    /// </summary>
    bool WouldTurnOut(Item shadow) =>
        !CanEmptySockets && Selected is { } tile && shadow.TotalNumberOfSockets < tile.Item.SocketedItems.Count;

    /// <summary>Opens a jewel of the selected item's sockets in a layer of its own, to be edited where it lies.</summary>
    void OpenSocketed(Item jewel);

    /// <summary>
    /// Whether a socketed jewel can be opened here: not on the Forge -
    /// its item is being made, and what goes in its sockets is the Forge's own to choose.
    /// </summary>
    bool OpensSocketed { get; }

    /// <summary>
    /// Whether a gem, rune or jewel in a socket can be copied out of it here: on the character's Items page, where
    /// the play mode makes copies, so a socketable need not be ejected to be copied. Not on the vault's Edit page, which only moves; not on the Forge, whose item is
    /// in no file yet.
    /// </summary>
    bool CopiesSocketed { get; }

    /// <summary>Puts a gem out of its socket where it lands; null when placed, otherwise why not.</summary>
    string? Eject(ItemTileViewModel tile, Item gem);

    /// <summary>
    /// Puts a gem out of its socket onto a cell - free, by the asker's word: where it was let go of, dragged out of
    /// the Sockets section's row, or pasted. Where the cell does not take it, where it lands (<see cref="Eject"/>).
    /// </summary>
    string? EjectTo(ItemTileViewModel tile, Item gem, GridCell cell);

    /// <summary>What the play mode allows.</summary>
    Services.Settings.PlayRules Rules { get; }

    /// <summary>Whether a socket added or removed is waiting on KEEP.</summary>
    bool SocketsAreChanged { get; }

    /// <summary>Shows the staged runeword change in the property rows, the old word's lines struck through.</summary>
    void ShowStagedRuneword(ItemTileViewModel tile);
}
