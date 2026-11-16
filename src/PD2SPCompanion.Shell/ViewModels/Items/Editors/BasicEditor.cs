using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item panel's Basic section: the item's own numbers, as against the properties rolled onto
/// it - graphic, quantity, item level, defence, ethereal, durability. Typed into boxes, held to
/// what the play mode allows, and written at KEEP.
/// </summary>
public sealed partial class BasicEditor : ViewModelBase
{
    private readonly IItemPanel _panel;

    public BasicEditor(IItemPanel panel) => _panel = panel;

    /// <summary>What the play mode allows, for the section's own bindings.</summary>
    public Services.Settings.PlayRules Rules => _panel.Rules;

    /// <summary>
    /// How many are in the stack, held back like the properties below it: the item is not
    /// touched until KEEP, and leaving it throws the number away.
    /// <para>
    /// Only on a base whose count the game reads. PD2 gave throwing weapons and arrows a
    /// quantity it ignores, and an editable number the game will not honour is worse than
    /// no number at all.
    /// </para>
    /// </summary>
    [ObservableProperty] private string _quantity = "";

    /// <summary>
    /// Which of its base's inventory graphics the item wears.
    /// <para>
    /// A charm, a ring, an amulet and a jewel each have several pictures and the item says
    /// which one it shows - three bits of it, beside the quality. It changes nothing about
    /// what the item does, which is exactly why it is worth being able to set: two rings
    /// that differ only in the picture are two rings you can tell apart on the belt.
    /// </para>
    /// <para>
    /// A unique or a set item is offered it where its picture is picked the same way - most unique
    /// rings, amulets and jewels name no picture of their own (it was refused
    /// to every unique and set item). One whose row names its picture - Annihilus, the Hellfire Torch,
    /// Seraph's Hymn - is not: the number would be set and nothing would change
    /// (ItemPresenter.PictureIsPicked).
    /// </para>
    /// </summary>
    public bool HasGraphic =>
        _panel.Selected is { TabSlot: null } tile
        && ItemPresenter.PictureIsPicked(tile.Item)
        && GraphicCount > 1;

    private int GraphicCount =>
        _panel.Selected is { } tile ? ItemTypes.VariantCount(tile.Item.Code) : 0;

    /// <summary>
    /// Counted from one, as anyone would count pictures. The item counts from nought, so the
    /// number shown is one above the number stored - the only field in this panel where the
    /// two differ, and the one nobody editing an item should have to know that about.
    /// </summary>
    public int GraphicMinimum => 1;

    /// <summary>
    /// The last picture the base has, or the last the three bits can hold - whichever comes
    /// first. Past the end the game shows the first one, so there is nothing to be gained.
    /// </summary>
    public int GraphicMaximum => Math.Max(Math.Min(Math.Max(GraphicCount, 1), 8), StoredGraphic + 1);

    public string GraphicLimits => L($"max {GraphicMaximum}");

    private int StoredGraphic =>
        _panel.Selected is { } tile && tile.Item.HasMultipleGraphics
            ? tile.Item.GraphicId
            : 0;

    /// <summary>What is in the box, which counts from one.</summary>
    public int GraphicTyped =>
        int.TryParse(Graphic, out int parsed) ? parsed : StoredGraphic + 1;

    public bool GraphicIsChanged => HasGraphic && GraphicTyped - 1 != StoredGraphic;

    public bool CanRaiseGraphic => GraphicTyped < GraphicMaximum;
    public bool CanLowerGraphic => GraphicTyped > GraphicMinimum;

    [ObservableProperty] private string _graphic = "";

    partial void OnGraphicChanged(string value) => _panel.Changed();

    /// <summary>
    /// A step big enough to reach either end of any field here, whatever it holds.
    /// <para>
    /// Every mover below clamps, so the jump is the ordinary move with an outsized step
    /// rather than a second set of bounds to keep in step with the first. Small enough
    /// that adding it to a field's value cannot overflow.
    /// </para>
    /// </summary>
    public const int Far = 1_000_000;

    /// <summary>
    /// Sends one of the panel's fields to its limit - what shift-clicking its - or + does.
    /// Named by field rather than reached by command, because the button knows which
    /// field it belongs to and the command does not.
    /// <para>
    /// As a property's stepper: where the base gives a range - the defense's roll, the maximum
    /// durability's figure - Shift goes to its end, and Ctrl + Shift (<paramref name="full"/>) in free editing to the
    /// field's own. Held to the rules the field ends at the base anyway.
    /// </para>
    /// </summary>
    public void Jump(string field, bool up, bool full = false)
    {
        full &= _panel.Rules.EditsDetailsFreely;
        switch (field)
        {
            case "defense" when !full && DefenseEnds is (int low, int high):
                MoveDefense((up ? high : low) - DefenseTyped);
                break;
            case "maxdurability" when !full && up && Basis?.Durability is { } most:
                MoveMaxDurability(most - MaxDurabilityTyped);
                break;
            default:
                Step(field, up ? Far : -Far);
                break;
        }
    }

    /// <summary>
    /// The base's range as Shift reaches it: the roll, or for an item spawned with enhanced defense its one figure on
    /// top (<see cref="SpawnedEnhanced"/>).
    /// </summary>
    private (int Low, int High)? DefenseEnds => SpawnedEnhanced ? BaseDefense : DefenseRoll;

    public string LowerDefenseHint => EndHint(up: false, DefenseEnds is not null);
    public string RaiseDefenseHint => EndHint(up: true, DefenseEnds is not null);

    /// <summary>Nothing gives a maximum durability below its base: Shift on the - goes to the field's floor.</summary>
    public string LowerMaxDurabilityHint => EndHint(up: false, toBase: false);
    public string RaiseMaxDurabilityHint => EndHint(up: true, Basis?.Durability is not null);

    private string EndHint(bool up, bool toBase)
    {
        string said = L("Ctrl + LMB: 10 at a time") + "\n" + (toBase
            ? up ? L("Shift + LMB: base max") : L("Shift + LMB: base min")
            : up ? L("Shift + LMB: max value") : L("Shift + LMB: min value"));
        return toBase && _panel.Rules.EditsDetailsFreely
            ? said + "\n" + (up ? L("Ctrl + Shift + LMB: field max") : L("Ctrl + Shift + LMB: field min"))
            : said;
    }

    /// <summary>
    /// Ten at a time - what ctrl-clicking a - or + does. A graphic has too few for it: one step.
    /// <para>
    /// The item level goes to the least its affixes need instead, where they need one (<see cref="AffixFloor"/>):
    /// ctrl + toward it lands on it, ctrl + away from it is a plain step: below the least, ctrl + click on + goes to
    /// it, and ctrl + click on - does nothing, just like a plain minus click; above it, the other way about.
    /// </para>
    /// </summary>
    public void Leap(string field, bool up)
    {
        if (field == "itemlevel" && AffixFloor is { } floor)
        {
            if (TowardFloor(up))
                ItemLevel = Math.Clamp(floor, ItemLevelMinimum, ItemLevelMaximum).ToString();
            else
                MoveItemLevel(up ? 1 : -1);
            return;
        }

        Step(field, (field == "graphic" ? 1 : LeapSize) * (up ? 1 : -1));
    }

    /// <summary>
    /// The least item level the item's affixes - staged ones included - need (MagicAffixes.LeastItemLevel),
    /// where that is above the field's own floor; null where they need nothing more than any level gives.
    /// </summary>
    public int? AffixFloor =>
        HasItemLevel && _panel.Selected is { } tile
        && MagicAffixes.LeastItemLevel(_panel.Staged(tile.Item)) is { } least && least > ItemLevelMinimum
            ? Math.Min(least, ItemLevelMaximum)
            : null;

    private bool TowardFloor(bool up) =>
        AffixFloor is { } floor && (up ? ItemLevelTyped < floor : ItemLevelTyped > floor);

    public string LowerItemLevelHint => ItemLevelHint(up: false);
    public string RaiseItemLevelHint => ItemLevelHint(up: true);

    private string ItemLevelHint(bool up)
    {
        string jump = up ? L("Shift + LMB: max value") : L("Shift + LMB: min value");
        if (AffixFloor is not { } floor)
            return L("Ctrl + LMB: 10 at a time") + "\n" + jump;

        return TowardFloor(up) ? L($"Ctrl + LMB: to {floor}, what the affixes need") + "\n" + jump : jump;
    }

    /// <summary>The affixes changed: what ctrl does on the item level, and the floor it shows, told again.</summary>
    public void AffixesChanged()
    {
        OnPropertyChanged(nameof(CanRerollItemLevel));
        OnPropertyChanged(nameof(ItemLevelRerollHint));
        OnPropertyChanged(nameof(AffixFloor));
        OnPropertyChanged(nameof(ItemLevelLimits));
        OnPropertyChanged(nameof(LowerItemLevelHint));
        OnPropertyChanged(nameof(RaiseItemLevelHint));
    }

    /// <summary>A ctrl-click's step.</summary>
    public const int LeapSize = 10;

    private void Step(string field, int by)
    {

        switch (field)
        {
            case "graphic": MoveGraphic(by); break;
            case "itemlevel": MoveItemLevel(by); break;
            case "defense": MoveDefense(by); break;
            case "durability": MoveDurability(by); break;
            case "maxdurability": MoveMaxDurability(by); break;
            case "quantity": MoveQuantity(by); break;
        }
    }

    [RelayCommand] private void RaiseGraphic() => MoveGraphic(+1);
    [RelayCommand] private void LowerGraphic() => MoveGraphic(-1);

    private void MoveGraphic(int by) =>
        Graphic = Math.Clamp(GraphicTyped + by, GraphicMinimum, GraphicMaximum).ToString();

    /// <summary>The pictures the base has, as the game picks among them when the item drops.</summary>
    private int GraphicChoices => Math.Min(Math.Max(GraphicCount, 1), 8);

    public bool CanRerollGraphic => GraphicChoices > 1;

    public string GraphicRerollHint => L($"Reroll within 1-{GraphicChoices}");

    /// <summary>
    /// A picture drawn as the game draws one - each alike, the one worn among them: the rolls' (the Demonic Cube's, the
    /// Forge's), where the die beside the box never lands on the same. True where it changed.
    /// </summary>
    public bool DrawGraphic(Random random)
    {
        if (!CanRerollGraphic)
            return false;

        string drawn = random.Next(1, GraphicChoices + 1).ToString();
        if (drawn == Graphic)
            return false;

        Graphic = drawn;
        return true;
    }

    /// <summary>Another picture, never the one it wears: a die that lands on the same picture looks broken.</summary>
    [RelayCommand]
    private void RerollGraphic()
    {
        if (!CanRerollGraphic)
            return;

        int others = Random.Shared.Next(1, GraphicChoices);
        Graphic = (others >= GraphicTyped ? others + 1 : others).ToString();
    }

    /// <summary>
    /// The count can be typed: a materials-tab slot's by "typing materials counts", a stack's by
    /// the item details - one box, two settings.
    /// </summary>
    public bool EditsQuantity => _panel.IsTabTile ? _panel.Rules.TypesMaterials : _panel.Rules.EditsDetails;

    public bool HasQuantity => _panel.Selected is { } tile
        && (tile.TabSlot is not null || Stacks.IsCounted(tile.Item.Code));

    /// <summary>A tab slot may be emptied; an item's stack cannot go below one.</summary>
    public int QuantityMinimum => _panel.IsTabTile ? 0 : Math.Min(1, Stored);

    /// <summary>
    /// What the box will take. Never below what the item already holds: a box handed a
    /// number above its maximum keeps the maximum and writes it back, so a stack larger than
    /// the table allows would be trimmed on sight, with nobody having touched it.
    /// </summary>
    public int QuantityMaximum => _panel.IsTabTile ? PD2SPCompanion.Core.Items.Materials.FieldMaximum
        // One, whatever the play mode: a magic, rare or unique map does not stack (Stacks.IsOneOnly).
        : _panel.Selected is { } one && Stacks.IsOneOnly(one.Item) ? Math.Max(1, Stored)
        // Any value: what the field holds, as defence and durability do.
        : _panel.Rules.EditsDetailsFreely ? Stacks.FieldMaximum
        : Math.Max(
        _panel.Selected is { } tile
            ? Stacks.MaximumFor(tile.Item.Code) ?? Stacks.FieldMaximum
            : Stacks.FieldMaximum,
        Stored);

    public string QuantityLimits => L($"max {QuantityMaximum:N0}");

    public int QuantityTyped => int.TryParse(Quantity, out int parsed) ? parsed : Stored;

    /// <summary>
    /// What the field starts at: what the game shows rather than the raw number, since a
    /// stack of one is stored as zero. Changed-ness is measured against this too, so a
    /// field nobody touched is not reported as an edit. See <see cref="Stacks.Shown"/>.
    /// </summary>
    private int Stored => _panel.Selected is { } tile
        ? tile.Tally ?? Stacks.Shown(tile.Item.Quantity)
        : 0;

    public bool QuantityIsChanged => HasQuantity && QuantityTyped != Stored;

    public bool CanRaiseQuantity => QuantityTyped < QuantityMaximum;
    public bool CanLowerQuantity => QuantityTyped > QuantityMinimum;

    partial void OnQuantityChanged(string value) => _panel.Changed();

    [RelayCommand]
    private void RaiseQuantity() => MoveQuantity(+1);

    [RelayCommand]
    private void LowerQuantity() => MoveQuantity(-1);

    private void MoveQuantity(int by) =>
        Quantity = Math.Clamp(QuantityTyped + by, QuantityMinimum, QuantityMaximum).ToString();

    /// <summary>
    /// The table's own stack range (<c>minstack</c> to <c>maxstack</c>) - a key 1-50, a tome 1-80. Not on a
    /// materials-tab slot, which is a count kept rather than a stack the game makes, nor on a map that is
    /// one map whatever (Stacks.IsOneOnly). The game drops a stack at a fixed size (<c>spawnstack</c>: six
    /// keys, one rune), so this is a roll over what the game allows, not one it makes.
    /// </summary>
    private (int Low, int High)? QuantityRoll =>
        _panel.Selected is { TabSlot: null } tile && !Stacks.IsOneOnly(tile.Item)
        && Stacks.MinimumFor(tile.Item.Code) is { } low && Stacks.MaximumFor(tile.Item.Code) is { } high && high > low
            ? (low, high)
            : null;

    public bool CanRerollQuantity => QuantityRoll is not null;

    public string QuantityRerollHint => QuantityRoll is (int low, int high) ? L($"Reroll within {low}-{high}") : "";

    [RelayCommand]
    private void RerollQuantity()
    {
        if (QuantityRoll is (int low, int high))
            Quantity = Math.Clamp(Random.Shared.Next(low, high + 1), QuantityMinimum, QuantityMaximum).ToString();
    }

    /// <summary>
    /// The level the item rolled at, held back like everything else on this page.
    /// <para>
    /// Seven bits in the file, and only on a full item - a simple one carries no level to
    /// show. It is what decides which affixes an item could have had, so it is worth
    /// setting, and worth showing even where nothing changes it.
    /// </para>
    /// </summary>
    [ObservableProperty] private string _itemLevel = "";



    public bool HasItemLevel => _panel.Selected is { TabSlot: null } tile && !tile.Item.IsCompact;

    /// <summary>As above: never a floor above what the item already holds.</summary>
    public int ItemLevelMinimum => Math.Min(1, StoredLevel);

    /// <summary>
    /// Capped at what the game reaches, not at what the field holds.
    /// <para>
    /// The field is seven bits and would take 127. The game reaches 99 and stops: Baal
    /// drops level 99 items, and PD2's uber bosses are the only others that high. The
    /// area table alone would suggest less - its highest is 93, Act 5 Throne Room - because
    /// act bosses carry a monster level of their own rather than their area's.
    /// </para>
    /// <para>
    /// An item level is a generation parameter rather than a measure of power, so there is
    /// nothing above 99 to want, and this is the one place a limit narrower than the file
    /// is worth enforcing.
    /// </para>
    /// <para>
    /// Properties are the opposite case and stay on the field limit: what an affix would
    /// roll cannot always be computed, so clamping there would refuse real items.
    /// </para>
    /// </summary>
    // Never below what the item holds: a box corrects nothing by being looked at - see DurabilityCeiling.
    public int ItemLevelMaximum => Math.Max(99, StoredLevel);

    /// <summary>
    /// The field's ceiling, and the least the affixes need while the level is at or past it - below it the
    /// red line under the field says so (AffixEditor.AffixLevelNote), so it is not said twice.
    /// </summary>
    public string ItemLevelLimits =>
        AffixFloor is { } floor && ItemLevelTyped >= floor
            ? L($"affixes {floor}+ · max {ItemLevelMaximum}")
            : L($"max {ItemLevelMaximum}");

    public int ItemLevelTyped => int.TryParse(ItemLevel, out int parsed) ? parsed : StoredLevel;

    private int StoredLevel => _panel.Selected?.Item.ItemLevel ?? 0;

    public bool ItemLevelIsChanged => HasItemLevel && ItemLevelTyped != StoredLevel;

    public bool CanRaiseItemLevel => ItemLevelTyped < ItemLevelMaximum;
    public bool CanLowerItemLevel => ItemLevelTyped > ItemLevelMinimum;

    /// <summary>
    /// The affix list is measured against the level on the page, so it has to hear about
    /// the level changing - what is out of reach at 12 is within it at 40, and both the
    /// orange marks and the requirement beside the field answer to it.
    /// </summary>
    partial void OnItemLevelChanged(string value)
    {
        _panel.Changed();
        _panel.AffixesChanged(rebuild: false);
    }

    [RelayCommand]
    private void RaiseItemLevel() => MoveItemLevel(+1);

    [RelayCommand]
    private void LowerItemLevel() => MoveItemLevel(-1);

    private void MoveItemLevel(int by) =>
        ItemLevel = Math.Clamp(ItemLevelTyped + by, ItemLevelMinimum, ItemLevelMaximum).ToString();

    /// <summary>
    /// The levels the item could hold with nothing more said against it, the staged item judged at each
    /// (Legitimacy.LegalItemLevels) - what its affixes, superior bonus, automagic, staff mod and sockets allow.
    /// <para>
    /// Worked out when the die is pressed, never to draw it: it is ninety-nine full checks, which drawn on every
    /// refresh cost each pick and each step of the level's box (measured: ~400 checks a keystroke;
    /// then ~115 ms on opening the affix picker). Kept while the staged item is the same with its level set aside -
    /// the level typed plays no part in it - so the die says its range from the first press on.
    /// </para>
    /// </summary>
    private IReadOnlyList<int> ItemLevelWindow(bool work)
    {
        if (!HasItemLevel || _panel.Selected is not { } tile)
            return [];

        var staged = _panel.StagedToChange(tile.Item);
        staged.ItemLevel = 1;
        byte[] key = Item.Write(staged, 0x60);
        if (_window is not null && key.AsSpan().SequenceEqual(_windowKey))
            return _window;

        if (!work)
            return [];

        _windowKey = key;
        return _window = [.. PD2SPCompanion.Core.Items.Legitimacy.LegalItemLevels(staged)
            .Where(level => level >= ItemLevelMinimum && level <= ItemLevelMaximum)];
    }

    private byte[] _windowKey = [];
    private IReadOnlyList<int>? _window;

    /// <summary>Idle only where a press found the level it has the only one: before a press, the die is offered.</summary>
    public bool CanRerollItemLevel => HasItemLevel && ItemLevelWindow(work: false).Count != 1;

    public string ItemLevelRerollHint =>
        ItemLevelWindow(work: false) is { Count: > 1 } window ? L($"Reroll within {window[0]}-{window[^1]}") : L("Reroll item level");

    /// <summary>
    /// An even roll among the levels the item could hold - never the one
    /// it has, as the picture's die. Where monsters live plays no part: every level from 1 to 96, and 99, drops
    /// somewhere, and the rest are gambled or crafted.
    /// </summary>
    [RelayCommand]
    private void RerollItemLevel()
    {
        var others = ItemLevelWindow(work: true).Where(level => level != ItemLevelTyped).ToList();
        if (others.Count > 0)
            ItemLevel = others[Random.Shared.Next(others.Count)].ToString();
        else
            _panel.Say(L("No other item level fits."));

        OnPropertyChanged(nameof(CanRerollItemLevel));
        OnPropertyChanged(nameof(ItemLevelRerollHint));
    }

    /// <summary>
    /// Defence, and the two durability numbers. Staged like everything else here.
    /// <para>
    /// The base figures are shown beside them and never enforced: the real ceiling is the
    /// base times ethereal and superior and enhanced defence, plus whatever a socketed jewel
    /// adds, and clamping to the base alone would refuse items the game made.
    /// </para>
    /// </summary>
    [ObservableProperty] private string _defense = "";
    [ObservableProperty] private string _durability = "";
    [ObservableProperty] private string _maxDurability = "";

    private ItemBases.Basis? Basis => _panel.Selected is { } tile
        ? ItemBases.Of(_panel.Draft.Code ?? tile.Item.Code)
        : null;

    public bool HasDefense => _panel.Selected is { TabSlot: null } tile
        && (tile.Item.Armor > 0 || Basis is { IsArmor: true, HasDefense: true });

    /// <summary>
    /// What the field will take. Free editing answers with what the format holds - eleven bits
    /// with a Save Add of 300, so 1747; held to the rules it answers with what the base could
    /// have rolled, ethereal included - the same bargain the property rows make.
    /// </summary>
    public int DefenseMaximum => _panel.Rules.EditsDetailsFreely || BaseDefense is not { } range
        ? 1747
        : Math.Max(range.High, StoredDefense);

    public int DefenseMinimum => _panel.Rules.EditsDetailsFreely || BaseDefense is not { } range
        ? Math.Min(0, StoredDefense)
        : Math.Min(range.Low, StoredDefense);

    /// <summary>
    /// The base's defence range as it reads on this item. An ethereal one carries its
    /// bonus in the stored number, so the table's raw pair is not what to hold it to.
    /// </summary>
    private (int Low, int High)? BaseDefense
    {
        get
        {
            if (BaseDefenceRange is not (var low, var high))
                return null;

            return IsEthereal
                ? (ItemBases.Ethereal(low), ItemBases.Ethereal(high))
                : (low, high);
        }
    }

    /// <summary>
    /// What the base may be, before ethereal is reckoned in.
    /// <para>
    /// <b>One above the table's own maximum.</b> The Arreat Summit puts the rule plainly:
    /// any piece of armour that spawns with +% Enhanced Defense has a base defence of
    /// maxac+1. Every armour on the character here bears it out, five for five - a Studded
    /// Leather of 35-37 carrying Enhanced Defense stores 38, a Ghoul Fringe of 3-3 stores 4.
    /// </para>
    /// <para>
    /// The extra point is allowed whether or not the item still carries that affix, because
    /// the base was settled when the item spawned and nothing since can lower it. Change the
    /// affixes afterwards and the 38 stays; asked whether the item still has Enhanced
    /// Defense, this accused a piece the game itself had made.
    /// </para>
    /// </summary>
    private (int Low, int High)? BaseDefenceRange =>
        Basis is { MinimumDefense: { } low, MaximumDefense: { } high }
            ? (low, high + 1)
            : null;

    public string? DefenseBasis
    {
        get
        {
            if (Basis is not { MinimumDefense: { } low, MaximumDefense: { } high })
                return null;

            if (!IsEthereal)
                return low == high ? L($"base {low}") : L($"base {low}-{high}");

            int lowest = ItemBases.Ethereal(low);
            int highest = ItemBases.Ethereal(high);

            return lowest == highest
                ? L($"base (eth) {lowest}")
                : L($"base (eth) {lowest}-{highest}");
        }
    }

    /// <summary>
    /// Ethereal, staged like everything else here.
    /// <para>
    /// On armour it is worth a question, because the defence stored on the item already has
    /// the ethereal bonus in it - toggling the flag alone leaves a number the game would not
    /// have produced. On a weapon there is nothing to ask: what ethereal changes there is
    /// damage, and no base damage is stored in the file at all.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isEthereal;

    /// <summary>
    /// Ethereal is a thing that happens to gear. It halves durability and raises defence or
    /// damage, none of which a ring, an amulet, a charm, a jewel or a quiver has - and the
    /// toggle was offered on every one of them, because the only question asked was whether
    /// the tables knew the base at all. See <see cref="ItemTypes.IsGear"/>.
    /// </summary>
    // Nor where the item has one form only (Ethereals.Of): a Lightsabre, Tyrael's Might, Ethereal Edge, a runeword
    // Phase Blade - offered still on one in the other form, so it can be put right.
    public bool HasEthereal => _panel.Selected is { TabSlot: null } tile
        && ItemTypes.IsGear(ItemTypes.TypeOf(tile.Item.Code))
        && PD2SPCompanion.Core.Items.Ethereals.Of(tile.Item) switch
        {
            PD2SPCompanion.Core.Items.Ethereals.Form.Never => tile.Item.IsEthereal,
            PD2SPCompanion.Core.Items.Ethereals.Form.Always => !tile.Item.IsEthereal,
            _ => true,
        };

    private bool StoredEthereal => _panel.Selected?.Item.IsEthereal ?? false;

    public bool EtherealIsChanged => HasEthereal && IsEthereal != StoredEthereal;

    /// <summary>
    /// What ethereal was worth on armour before S13's quarter (<see cref="ItemBases.Ethereal"/>):
    /// half again, which items dropped then still store. Used only as the questionable-defence
    /// ceiling, where too low a figure marks real items.
    /// </summary>
    private const double OlderEtherealDefense = 1.5;

    /// <summary>
    /// Flips the flag and moves the defence with it.
    /// <para>
    /// No question first. The defence stored on armour already has the ethereal bonus in
    /// it, so the two always move together - and a question whose sensible answer is the
    /// same every time is a keystroke, not a decision. Both land in the same staged edit,
    /// so UNDO takes back the pair.
    /// </para>
    /// <para>
    /// A weapon has nothing to move: what ethereal changes there is damage, and no base
    /// damage is stored in the file.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void ToggleEthereal()
    {
        if (_panel.Selected is null)
            return;

        _panel.Press();

        bool wanted = !IsEthereal;
        Ethereal(wanted, withDefense: Basis is { IsArmor: true } && DefenseTyped > 0);
    }

    private int Adjusted(bool ethereal) => ethereal
        ? ItemBases.Ethereal(DefenseTyped)
        : ItemBases.BehindEthereal(DefenseTyped);

    private void Ethereal(bool wanted, bool withDefense)
    {
        // The new figure first, from the defence as it reads now; then the switch, so the bounds it is
        // held to are the new state's. Clamped while the switch still said the old state, a quarter more
        // met the plain base's ceiling and was cut straight back: Duriel's Shell, 177, stayed 177 made
        // ethereal instead of 221.
        int moved = Adjusted(wanted);
        IsEthereal = wanted;
        if (withDefense)
            Defense = Math.Clamp(moved, DefenseMinimum, DefenseMaximum).ToString();

        _panel.Changed();
        OnPropertyChanged(nameof(EtherealIsChanged));
        OnPropertyChanged(nameof(DefenseCeiling));
        OnPropertyChanged(nameof(DefenseBasis));
        OnPropertyChanged(nameof(LowerDefenseHint));
        OnPropertyChanged(nameof(RaiseDefenseHint));
        OnPropertyChanged(nameof(DefenseIsQuestionable));
        OnPropertyChanged(nameof(CanRerollDefense));
        OnPropertyChanged(nameof(DefenseRerollHint));
    }

    /// <summary>
    /// The largest maximum durability this base can account for: its own, raised by whatever
    /// durability the item's own properties add. Null when the base is not known.
    /// </summary>
    public int? MaxDurabilityCeiling
    {
        get
        {
            if (_panel.Selected is null || Basis?.Durability is not { } most)
                return null;

            return WithBonus(most);
        }
    }

    /// <summary>
    /// A maximum durability as the game holds the item to it: raised by whatever durability the
    /// item's own properties add, a percentage and then a flat amount - those staged too: a superior's
    /// "Increase Maximum Durability" picked and not yet written held the box under its own new top, and
    /// a base made full at it came out 50 of 60 (the pickers staying open after a pick).
    /// </summary>
    private int WithBonus(int maximum) =>
        _panel.Selected is not { } tile ? maximum
        : ItemStats.MaxDurabilityOf(_panel.HasChanges && _panel.Pending() is { } staged ? staged : tile.Item, maximum);

    /// <summary>
    /// Whether the maximum durability is one the base cannot account for.
    /// <para>
    /// <b>Never asked of an ethereal item.</b> A rare ethereal Hard Leather Armor, base
    /// durability 28 and carrying no durability property at all, is 84 in game - three
    /// times the base - so whatever PD2 does to an ethereal item's durability is not in
    /// the tables we read. Marking it was a plain false positive, and an editor that cries
    /// forgery over a real item teaches you to ignore it.
    /// </para>
    /// </summary>
    public bool DurabilityIsQuestionable =>
        StatRowViewModel.FlagIllegitimate
        && HasDurability
        && _panel.Selected is { } item
        && !item.Item.IsEthereal
        && MaxDurabilityCeiling is { } most
        && MaxDurabilityTyped > most;

    /// <summary>
    /// The most defence this item could legitimately show, or null when it cannot be worked
    /// out.
    /// <para>
    /// Unlike a property, this one <b>is</b> computable, because everything that raises it
    /// is on the item rather than in a table we have to guess at. The base tops out at
    /// <c>maxac</c>; ethereal adds to the number itself rather than on top of it at runtime,
    /// so it is in the stored figure already; and enhanced defence is carried as a property
    /// we can read. So the ceiling is maxac, raised by ethereal and by the item's own
    /// enhanced defence.
    /// </para>
    /// <para>
    /// Rounded generously and left null wherever a piece is missing. A ceiling that is too
    /// low marks real items, which is the failure that teaches people to ignore the mark.
    /// </para>
    /// </summary>
    public int? DefenseCeiling
    {
        get
        {
            if (_panel.Selected is not { } tile || BaseDefenceRange is not (_, var most))
                return null;

            double ceiling = most;

            // The staged flag, not the stored one: the question is whether the number in
            // the box is possible on the item as it is being edited.
            if (IsEthereal)
                ceiling *= OlderEtherealDefense;

            int enhanced = tile.Item.StatLists
                .SelectMany(list => list.Stats)
                .Where(stat => stat.Stat == "item_armor_percent")
                .Sum(stat => stat.Value);

            if (enhanced > 0)
                ceiling *= 1 + (enhanced / 100.0);

            return (int)Math.Ceiling(ceiling);
        }
    }

    /// <summary>
    /// True when the defence is above everything the item can account for. Never true from
    /// below: an item can always have been damaged or downgraded.
    /// </summary>
    public bool DefenseIsQuestionable =>
        StatRowViewModel.FlagIllegitimate
        && HasDefense
        && DefenseCeiling is { } most
        && DefenseTyped > most;

    /// <summary>The "?" badge's tooltip by the maximum durability, as <see cref="DefenseNote"/> is the defence's.</summary>
    public string DurabilityNote =>
        MaxDurabilityCeiling is { } most
            ? L($"Potential illegal item:{Environment.NewLine}")
              + L($"{MaxDurabilityTyped} is above the {most} this base can account for.")
            : "";

    public string DefenseNote =>
        DefenseCeiling is { } most
            ? L($"Potential illegal item:{Environment.NewLine}")
              + L($"{DefenseTyped} is above the {most} this base can account for.")
            : "";

    private int StoredDefense => _panel.Selected?.Item.Armor ?? 0;
    public int DefenseTyped => int.TryParse(Defense, out int parsed) ? parsed : StoredDefense;
    public bool DefenseIsChanged => HasDefense && DefenseTyped != StoredDefense;
    public bool CanRaiseDefense => DefenseTyped < DefenseMaximum;
    public bool CanLowerDefense => DefenseTyped > DefenseMinimum;

    partial void OnDefenseChanged(string value)
    {
        OnPropertyChanged(nameof(DefenseIsQuestionable));
        OnPropertyChanged(nameof(DefenseNote));
        _panel.Changed();
    }

    [RelayCommand] private void RaiseDefense() => MoveDefense(+1);
    [RelayCommand] private void LowerDefense() => MoveDefense(-1);

    private void MoveDefense(int by) =>
        Defense = Math.Clamp(DefenseTyped + by, DefenseMinimum, DefenseMaximum).ToString();

    /// <summary>
    /// Whether the item spawned with enhanced defence: its base stored at <c>maxac + 1</c>, which no roll
    /// gives (see <see cref="BaseDefenceRange"/>). Settled when the item was made, so its die rolls nothing.
    /// </summary>
    private bool SpawnedEnhanced =>
        Basis is { MaximumDefense: { } high }
        && (StoredEthereal ? ItemBases.BehindEthereal(StoredDefense) : StoredDefense) == high + 1;

    /// <summary>The base's range as this item would show it - the table's, ethereal reckoned in.</summary>
    private (int Low, int High)? DefenseRoll =>
        Basis is { MinimumDefense: { } low, MaximumDefense: { } high } && !SpawnedEnhanced
            ? IsEthereal ? (ItemBases.Ethereal(low), ItemBases.Ethereal(high)) : (low, high)
            : null;

    public bool CanRerollDefense => DefenseRoll is (int low, int high) && high > low;

    public string DefenseRerollHint =>
        SpawnedEnhanced ? L("Fixed: spawned with enhanced defense")
        : DefenseRoll is (int low, int high) ? L($"Reroll within {low}-{high}")
        : "";

    /// <summary>A roll in the table's range, then the ethereal quarter on it, as the game makes the item.</summary>
    [RelayCommand]
    private void RerollDefense()
    {
        if (!CanRerollDefense || Basis is not { MinimumDefense: { } low, MaximumDefense: { } high })
            return;

        int roll = Random.Shared.Next(low, high + 1);
        Defense = Math.Clamp(IsEthereal ? ItemBases.Ethereal(roll) : roll, DefenseMinimum, DefenseMaximum).ToString();
    }

    /// <summary>Only where the item stores them - one with no maximum stores neither.</summary>
    public bool HasDurability => _panel.Selected is { TabSlot: null } tile && tile.Item.MaxDurability > 0;

    /// <summary>
    /// Eight bits each, for both numbers - or the base's own durability while editing is
    /// held to the rules, since nothing the game made carries more than its base allows.
    /// </summary>
    public int DurabilityMaximum =>
        _panel.Rules.EditsDetailsFreely || Basis?.Durability is not { } most
            ? 255
            : Math.Max(most, StoredMaxDurability);

    public string? DurabilityBasis => Basis?.Durability is { } most ? L($"base {most}") : null;

    private int StoredDurability => _panel.Selected?.Item.Durability ?? 0;
    private int StoredMaxDurability => _panel.Selected?.Item.MaxDurability ?? 0;

    public int DurabilityTyped =>
        int.TryParse(Durability, out int parsed) ? parsed : StoredDurability;

    public int MaxDurabilityTyped =>
        int.TryParse(MaxDurability, out int parsed) ? parsed : StoredMaxDurability;

    /// <summary>
    /// Held at or below the maximum the item really has - the one beside it, raised by what the
    /// item's own properties add - and never below what it holds.
    /// <para>
    /// Held to the stored maximum alone, Duriel's Shell (146 of a stored 50, with +100 maximum
    /// durability of its own: 150 in game) had its 146 pulled down to 50 by the box the moment it
    /// was looked at, which read as an edit - Undo and Keep lit, and Keep would have written 50.
    /// A value is never corrected by being read: the box's floor and ceiling take in the stored
    /// number, as the defence's do. The field itself is 8 bits.
    /// </para>
    /// </summary>
    public int DurabilityCeiling => Math.Max(StoredDurability, Math.Min(WithBonus(MaxDurabilityTyped), 255));

    public int DurabilityMinimum => Math.Min(0, StoredDurability);
    public int MaxDurabilityMinimum => Math.Min(1, StoredMaxDurability);

    public bool DurabilityIsChanged => HasDurability && DurabilityTyped != StoredDurability;
    public bool MaxDurabilityIsChanged => HasDurability && MaxDurabilityTyped != StoredMaxDurability;

    public bool CanRaiseDurability => DurabilityTyped < DurabilityCeiling;
    public bool CanLowerDurability => DurabilityTyped > DurabilityMinimum;
    public bool CanRaiseMaxDurability => MaxDurabilityTyped < DurabilityMaximum;
    public bool CanLowerMaxDurability => MaxDurabilityTyped > MaxDurabilityMinimum;

    partial void OnDurabilityChanged(string value) => _panel.Changed();

    partial void OnMaxDurabilityChanged(string value)
    {
        OnPropertyChanged(nameof(DurabilityIsQuestionable));
        OnPropertyChanged(nameof(DurabilityNote));

        // Lowering the maximum below what is on the item takes the item down with it.
        if (DurabilityTyped > DurabilityCeiling)
            Durability = DurabilityCeiling.ToString();

        OnPropertyChanged(nameof(DurabilityCeiling));
        OnPropertyChanged(nameof(CanRerollDurability));
        OnPropertyChanged(nameof(DurabilityRerollHint));
        _panel.Changed();
    }

    [RelayCommand] private void RaiseDurability() => MoveDurability(+1);
    [RelayCommand] private void LowerDurability() => MoveDurability(-1);
    [RelayCommand] private void RaiseMaxDurability() => MoveMaxDurability(+1);
    [RelayCommand] private void LowerMaxDurability() => MoveMaxDurability(-1);

    private void MoveDurability(int by) =>
        Durability = Math.Clamp(DurabilityTyped + by, DurabilityMinimum, DurabilityCeiling).ToString();

    private void MoveMaxDurability(int by) =>
        MaxDurability = Math.Clamp(
            MaxDurabilityTyped + by, MaxDurabilityMinimum, DurabilityMaximum).ToString();

    /// <summary>
    /// The current durability rolled as a new item's is (ItemBases.DurabilityRoll): half the maximum the item
    /// really has - its bonuses in - to one under it. The maximum itself is the base's, not a roll.
    /// </summary>
    private (int Low, int High) DurabilityRoll => ItemBases.DurabilityRoll(Math.Min(WithBonus(MaxDurabilityTyped), 255));

    public bool CanRerollDurability => DurabilityRoll is (int low, int high) && high > low;

    public string DurabilityRerollHint => DurabilityRoll is (int low, int high) ? L($"Reroll within {low}-{high}") : "";

    [RelayCommand]
    private void RerollDurability()
    {
        if (CanRerollDurability && DurabilityRoll is (int low, int high))
            Durability = Math.Clamp(Random.Shared.Next(low, high + 1), DurabilityMinimum, DurabilityCeiling).ToString();
    }

    /// <summary>
    /// The item's own numbers, as against the properties rolled onto it. Absent only on an
    /// item that has none of them - a simple one, which stores no level either.
    /// </summary>
    [ObservableProperty] private bool _basicOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(BasicOpen));

    partial void OnBasicOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(BasicOpen), value);

    public bool HasBasic =>
        HasItemLevel || HasDefense || HasDurability || HasQuantity || HasGraphic || HasPersonalName;

    [RelayCommand]
    private void ToggleBasic()
    {
        _panel.Press();
        BasicOpen = !BasicOpen;
    }

    /// <summary>
    /// The limits told again - before any value, and that order is the whole of it. Every box
    /// refuses a value outside the Minimum and Maximum it is bound to, and refusing means writing
    /// the clamped number back: sent the new item's durability while the box still held the old
    /// item's ceiling, a spear of 56 landed on a body armour's 32 and wrote 32 back.
    /// </summary>
    public void NotifyLimits()
    {
        foreach (string name in new[]
                 {
                     nameof(QuantityMinimum), nameof(QuantityMaximum),
                     nameof(ItemLevelMinimum), nameof(ItemLevelMaximum),
                     nameof(DefenseMinimum), nameof(DefenseMaximum), nameof(DefenseCeiling),
                     nameof(DurabilityMinimum), nameof(DurabilityMaximum),
                     nameof(DurabilityCeiling),
                     nameof(MaxDurabilityMinimum), nameof(MaxDurabilityCeiling),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    // ---- personalized: the name on the item (Anya's reward) -------------------------------
    // Typed where the play mode edits an item's details (Casual and Free); a character's
    // name by the game's rule, and none takes it off. The tooltip heads the item with it in every mode
    // (ItemLines.Title).

    /// <summary>Whether the item can carry a name: not a gem or a potion, written short, nor an ear.</summary>
    public bool HasPersonalName => _panel.Selected is { TabSlot: null } tile && Personalization.Takes(tile.Item);

    [ObservableProperty] private string _personalName = "";

    private string StoredPersonalName => _panel.Selected is { } tile ? Personalization.NameOn(tile.Item) : "";

    public bool PersonalNameIsChanged => HasPersonalName && PersonalName.Trim() != StoredPersonalName;

    /// <summary>Why the name typed cannot go on the item, or null: nothing is written while it stands.</summary>
    public string? PersonalNameProblem =>
        HasPersonalName && PersonalName.Trim() is { Length: > 0 } name
            ? PD2SPCompanion.Core.Characters.CharacterRename.Problem(name)
            : null;

    public bool PersonalNameIsBad => PersonalNameProblem is not null;

    /// <summary>Under the field's name: what is wrong with the name typed, else what the field is.</summary>
    public string PersonalNameNote => PersonalNameProblem ?? L("name on the item");

    partial void OnPersonalNameChanged(string value)
    {
        OnPropertyChanged(nameof(PersonalNameIsChanged));
        OnPropertyChanged(nameof(PersonalNameProblem));
        OnPropertyChanged(nameof(PersonalNameIsBad));
        OnPropertyChanged(nameof(PersonalNameNote));
        _panel.Changed();
    }

    /// <summary>Everything the section shows about the item, told again: the limits, then the rest.</summary>
    public void Notify()
    {
        NotifyLimits();

        foreach (string name in new[]
                 {
                     nameof(HasQuantity), nameof(EditsQuantity), nameof(QuantityLimits), nameof(QuantityIsChanged),
                     nameof(CanRaiseQuantity), nameof(CanLowerQuantity),
                     nameof(HasItemLevel), nameof(ItemLevelLimits), nameof(ItemLevelIsChanged),
                     nameof(CanRaiseItemLevel), nameof(CanLowerItemLevel),
                     nameof(HasDefense), nameof(DefenseBasis), nameof(LowerDefenseHint), nameof(RaiseDefenseHint),
                     nameof(DefenseIsChanged), nameof(CanRaiseDefense), nameof(CanLowerDefense),
                     nameof(HasDurability), nameof(DurabilityBasis), nameof(LowerMaxDurabilityHint), nameof(RaiseMaxDurabilityHint),
                     nameof(DurabilityIsChanged), nameof(MaxDurabilityIsChanged),
                     nameof(CanRaiseDurability), nameof(CanLowerDurability),
                     nameof(CanRaiseMaxDurability), nameof(CanLowerMaxDurability),
                     nameof(HasBasic),
                     nameof(HasGraphic), nameof(GraphicIsChanged), nameof(GraphicLimits),
                     nameof(CanRaiseGraphic), nameof(CanLowerGraphic),
                     nameof(GraphicMinimum), nameof(GraphicMaximum),
                     nameof(HasEthereal), nameof(EtherealIsChanged),
                     nameof(HasPersonalName), nameof(PersonalNameIsChanged), nameof(PersonalNameProblem),
                     nameof(PersonalNameIsBad), nameof(PersonalNameNote),
                     nameof(DurabilityIsQuestionable), nameof(DurabilityNote),
                     nameof(DefenseIsQuestionable), nameof(DefenseNote),
                     nameof(AffixFloor), nameof(LowerItemLevelHint), nameof(RaiseItemLevelHint),
                     nameof(CanRerollGraphic), nameof(GraphicRerollHint),
                     nameof(CanRerollQuantity), nameof(QuantityRerollHint),
                     nameof(CanRerollDefense), nameof(DefenseRerollHint),
                     nameof(CanRerollDurability), nameof(DurabilityRerollHint),
                     nameof(CanRerollItemLevel), nameof(ItemLevelRerollHint),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>
    /// The boxes set to what the item holds, for a new item or a discard: the limits first (see
    /// <see cref="NotifyLimits"/>), then the numbers, written past their change handlers so that
    /// loading them stages nothing.
    /// </summary>
    public void ShowStored()
    {
        NotifyLimits();

#pragma warning disable MVVMTK0034
        _quantity = Stored.ToString();
        OnPropertyChanged(nameof(Quantity));
        _itemLevel = StoredLevel.ToString();
        OnPropertyChanged(nameof(ItemLevel));
        _defense = StoredDefense.ToString();
        OnPropertyChanged(nameof(Defense));
        _durability = StoredDurability.ToString();
        OnPropertyChanged(nameof(Durability));
        _maxDurability = StoredMaxDurability.ToString();
        OnPropertyChanged(nameof(MaxDurability));
        _isEthereal = StoredEthereal;
        OnPropertyChanged(nameof(IsEthereal));
        _graphic = (StoredGraphic + 1).ToString();
        OnPropertyChanged(nameof(Graphic));
        _personalName = StoredPersonalName;
        OnPropertyChanged(nameof(PersonalName));
#pragma warning restore MVVMTK0034
    }

    /// <summary>
    /// Writes what was typed onto the item, at KEEP, each clamped to its limits - the maximum
    /// durability before the current one, so the current is held against the new ceiling.
    /// </summary>
    public void Commit(Item item)
    {
        // Asked before anything is written: on the real write the item is the one these read, so a maximum written
        // below read as no change at all, and the current stayed over it - 100 of a maximum of 50 in the file.
        bool maxChanged = MaxDurabilityIsChanged, nowChanged = DurabilityIsChanged;

        if (QuantityIsChanged)
            item.Quantity = (ushort)Math.Clamp(QuantityTyped, QuantityMinimum, QuantityMaximum);

        if (ItemLevelIsChanged)
            item.ItemLevel = (byte)Math.Clamp(ItemLevelTyped, ItemLevelMinimum, ItemLevelMaximum);

        if (DefenseIsChanged)
            item.Armor = (ushort)Math.Clamp(DefenseTyped, DefenseMinimum, DefenseMaximum);

        if (EtherealIsChanged)
            item.IsEthereal = IsEthereal;

        if (PersonalNameIsChanged && !PersonalNameIsBad)
            Personalization.Set(item, PersonalName);

        if (GraphicIsChanged)
        {
            // The flag is what makes the item carry the number at all, so it goes on with
            // it: a base that has several pictures and no flag is showing the first.
            item.HasMultipleGraphics = true;
            item.GraphicId = (byte)(Math.Clamp(GraphicTyped, GraphicMinimum, GraphicMaximum) - 1);
        }

        if (maxChanged)
            item.MaxDurability = (ushort)Math.Clamp(MaxDurabilityTyped, MaxDurabilityMinimum, DurabilityMaximum);

        // Up to the maximum the item's bonuses raise it to, as the ceiling the box offers is: an Infinity
        // storing 84 with +20% holds 100. Held to the stored maximum, a durability typed past it went back.
        if (nowChanged || maxChanged)
            item.Durability = (ushort)Math.Clamp(DurabilityTyped, DurabilityMinimum,
                Math.Max(DurabilityMinimum, Math.Min(ItemStats.MaxDurabilityOf(item, item.MaxDurability), 255)));
    }
}
