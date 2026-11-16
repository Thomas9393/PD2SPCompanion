using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;

namespace PD2SPCompanion.Shell.Services.Character;

// What may be worn and by whom: requirements, wearing, Refit, the belt.
public sealed partial class CharacterSession
{

    /// <summary>
    /// Why a worn item cannot come off, or null. A belt holding potions above its bottom row: a
    /// character with no belt has that one row of four, and a potion left in a slot past it
    /// would be in a place the game has no room for. Taking the potions out first is the
    /// game's own order of things.
    /// <para>
    /// The character's own belt only. The mercenary's is worn at the waist too, and holds no potions:
    /// asked without telling them apart, the character's potions kept the mercenary's belt on.
    /// </para>
    /// </summary>
    public string? CannotTakeOff(Item item)
    {
        if (item.Mode != ItemMode.Equipped)
            return null;

        var own = _save?.PlayerItemList?.Items ?? [];
        if (!own.Any(each => ReferenceEquals(each, item)))
            return null;

        // What gives the Iron Golem its skill takes the golem with it: its item needs room first.
        if (GolemKeepsOn(item) is { } golem)
            return golem;

        if (item.Location != ItemLocation.Waist)
            return null;

        if (!own.Any(each => each.Mode == ItemMode.Belt && each.X >= Belts.Default))
            return null;

        // Its potions above the first row go with it where they have somewhere to go (BeltSpill): refused
        // only where they have not.
        return BeltSpill is { } spill ? spill(1, true, out _) : L("Require belt's upper rows empty.");
    }

    /// <summary>
    /// The belt's potions above the rows it is left with, landed elsewhere: taking a belt off is not
    /// blocked, its content goes to the open page, and only a full page blocks it. The same for a belt of another
    /// size taking its place: the difference is ejected unless there is no room. ItemLanding's to do, a shared page being beyond the session: given the rows left and whether
    /// only to ask, it answers null - they fit, or have been moved - or why not. Until then a belt with potions
    /// above its first row stayed on, and a smaller belt was refused over them.
    /// <para>
    /// <c>back</c>: what puts the potions moved in their boxes again, for an act that then does not happen - null where
    /// none moved (a belt lifted and put back, or a heavier one refused, left them spilled).
    /// </para>
    /// </summary>
    public BeltSpiller? BeltSpill { get; set; }

    /// <summary>See <see cref="BeltSpill"/>.</summary>
    public delegate string? BeltSpiller(int rows, bool dry, out Action? back);

    /// <summary>Where a potion lay in the belt, and where among the character's items: what putting it back asks.</summary>
    public sealed record BeltBox(Item Potion, ItemMode Mode, ItemLocation Location, byte Page, byte X, byte Y, int Index);

    /// <summary>A potion's box as it stands, before it is moved.</summary>
    public BeltBox BoxOf(Item potion) =>
        new(potion, potion.Mode, potion.Location, potion.Page, potion.X, potion.Y, _save?.PlayerItemList?.Items.IndexOf(potion) ?? 0);

    /// <summary>
    /// Potions already out of every list put in their boxes, each where it stood among the character's items - in the
    /// order they stood, so the list comes out as it was and the file reads as saved again. All before the belt is
    /// settled (MarkEdited): one at a time, a third row's potion would fall into the second row's empty box.
    /// </summary>
    public void PutBackInBelt(IEnumerable<BeltBox> boxes)
    {
        if (_save?.PlayerItemList is not { } own)
            return;

        foreach (var box in boxes.OrderBy(each => each.Index))
        {
            var potion = box.Potion;
            (potion.Mode, potion.Location, potion.Page, potion.X, potion.Y) = (box.Mode, box.Location, box.Page, box.X, box.Y);
            own.Items.Insert(Math.Min(box.Index, own.Items.Count), potion);
        }

        own.Count = (ushort)own.Items.Count;
        MarkEdited();
    }

    /// <summary>
    /// Where a piece pushed off by one put on lands when the character's own pages have no room for it - beyond
    /// the session, on the shared page a pane shows: it still follows the rule, not
    /// blocked, and the message strip says where it goes. ItemLanding's to answer: null where there is
    /// no room there either; else what puts it there, called once the piece has left the character's lists and
    /// the one put on has been judged, and answering null, or why not. Until then a small weapon over a large one was
    /// refused, "No free space", with a shared page open and empty beside it.
    /// </summary>
    public Func<IReadOnlyList<Item>, Func<string?>?>? Beyond { get; set; }

    /// <summary>
    /// What comes off when <paramref name="item"/> is put on at <paramref name="at"/>: the piece worn there, and
    /// - for a weapon held in both hands - what the other hand holds (Equipping.AlsoComesOff). In that order;
    /// none for an empty slot.
    /// </summary>
    public IReadOnlyList<Item> PushedOffBy(Item item, ItemLocation at, bool mercenary = false)
    {
        var into = mercenary ? _save?.MercenaryItemList?.ItemList : _save?.PlayerItemList;
        if (_save is null || into is null)
            return [];

        Item? WornAt(ItemLocation where) => into.Items.FirstOrDefault(each =>
            each.Mode == ItemMode.Equipped && each.Location == where && !ReferenceEquals(each, item));

        var also = Equipping.AlsoComesOff(item, at, mercenary ? null : CharacterClasses.CodeOf(_save.ClassId), WornAt);
        return [.. new[] { WornAt(at), also == ItemLocation.None ? null : WornAt(also) }.OfType<Item>()];
    }

    /// <summary>
    /// The tab, 1 or 2, the game shows the weapons in hand under (<see cref="WeaponSets"/>): the main hand slots
    /// are that tab's, the swap slots the other's. 1 with no character open.
    /// </summary>
    public int TabInHand => _save is null ? 1 : WeaponSets.TabInHand(_save);

    /// <summary>
    /// Why the weapon sets cannot be swapped, or null: something is being carried, or the pair in hand gives the
    /// Iron Golem its skill and the golem's item has nowhere to go - what keeps that item on (<see cref="CannotTakeOff"/>).
    /// </summary>
    public string? CannotSwapWeapons()
    {
        if (_save?.PlayerItemList is not { } own)
            return L("No character open.");

        if (IsHolding)
            return L("Put the item down first.");

        return own.Items
            .Where(item => item is { Mode: ItemMode.Equipped, Location: ItemLocation.RightHand or ItemLocation.LeftHand })
            .Select(GolemKeepsOn)
            .FirstOrDefault(refused => refused is not null);
    }

    /// <summary>
    /// The weapon swap, as the game's own - its W key, its I and II tabs: the save says which weapon set
    /// is on, the editor shows that one, and a change here is written back. The pair in hand goes to the swap slots and the other pair comes to hand; the tab the save names
    /// turns; and the skills on the two buttons change places with the ones kept for the other pair - all of
    /// what D2Game's weapon switch does to what a save holds. An edit like any other: the figure, the
    /// requirements and what the gear gives follow it, and it is saved and undone as one.
    /// </summary>
    /// <returns>Null when swapped; else why not (<see cref="CannotSwapWeapons"/>).</returns>
    public string? SwapWeapons()
    {
        if (CannotSwapWeapons() is { } refused)
            return refused;

        foreach (var item in _save!.PlayerItemList.Items.Where(item => item.Mode == ItemMode.Equipped))
        {
            item.Location = item.Location switch
            {
                ItemLocation.RightHand => ItemLocation.SwapRight,
                ItemLocation.LeftHand => ItemLocation.SwapLeft,
                ItemLocation.SwapRight => ItemLocation.RightHand,
                ItemLocation.SwapLeft => ItemLocation.LeftHand,
                var same => same,
            };
        }

        // The pair come to hand is put on right hand first, as the engine's switch places them (it takes all
        // four out and sets them down in the order right, left of the pair leaving, then right, left of the
        // pair coming): after a swap the right hand slot's weapon is the first put on.
        var items = _save.PlayerItemList.Items;
        foreach (var slot in new[] { ItemLocation.SwapRight, ItemLocation.SwapLeft, ItemLocation.RightHand, ItemLocation.LeftHand })
        {
            if (items.FirstOrDefault(item => item.Mode == ItemMode.Equipped && item.Location == slot) is { } moved)
            {
                items.Remove(moved);
                items.Add(moved);
            }
        }

        _save.ActiveWeapon = _save.ActiveWeapon == 0 ? 1u : 0u;
        (_save.LeftSkill, _save.LeftSwapSkill) = (_save.LeftSwapSkill, _save.LeftSkill);
        (_save.RightSkill, _save.RightSwapSkill) = (_save.RightSwapSkill, _save.RightSkill);

        MarkEdited();
        Rebuild();
        return null;
    }

    /// <summary>What the character's belt holds, in no order.</summary>
    public IReadOnlyList<Item> BeltItems =>
        [.. (_save?.PlayerItemList?.Items ?? []).Where(item => item.Mode == ItemMode.Belt)];

    /// <summary>
    /// The character's worn belt about to leave its slot - lifted, let go, deleted, marked for the vault: its
    /// potions above the first row are landed first (<see cref="BeltSpill"/>). Asked already whether they fit
    /// (<see cref="CannotTakeOff"/>); nothing for any other item. Answers what puts them back, for an act that may yet
    /// not happen; null where none moved.
    /// </summary>
    public Action? SpillBeltOf(Item item)
    {
        if (item is { Mode: ItemMode.Equipped, Location: ItemLocation.Waist }
            && (_save?.PlayerItemList?.Items ?? []).Any(each => ReferenceEquals(each, item))
            && BeltSpill is { } spill)
        {
            spill(1, false, out var back);
            return back;
        }

        return null;
    }

    /// <summary>
    /// Tells each tile whether the open character can use it: worn items and charms as the game judges them,
    /// the gear helping, never an item toward itself, the unmet giving nothing; what is only
    /// carried against the base and what the gear in use gives. The mercenary's are left unjudged
    /// for now - its stats are step 4. No character: every verdict let go.
    /// </summary>
    public void JudgeTiles(IEnumerable<ItemTileViewModel> tiles)
    {
        if (_save is null)
        {
            foreach (var tile in tiles)
                tile.Judge(null, null);
            return;
        }

        string? classCode = CharacterClasses.CodeOf(_save.ClassId);
        int level = _save.Level;
        int strength = AttributeScale.Display("strength", _save.Attributes.Stats.GetValueOrDefault("strength"));
        int dexterity = AttributeScale.Display("dexterity", _save.Attributes.Stats.GetValueOrDefault("dexterity"));
        int inHand = WeaponSets.InHand;

        var verdicts = new Dictionary<Item, Requirements.Judged>(ReferenceEqualityComparer.Instance);
        var active = Requirements.Judge(_save, inHand, strength, dexterity);
        foreach (var judged in active)
            verdicts[judged.Item] = judged;

        // The other set's weapons, as they would be judged in hand.
        foreach (var judged in Requirements.Judge(_save, 1 - inHand, strength, dexterity))
            verdicts.TryAdd(judged.Item, judged);

        // What the gear in use gives, its sets' bonuses with it.
        var carried = Requirements.TotalOf(active.Where(judged => judged.Met).Select(judged => judged.Item), level);
        var hired = _save.MercenaryItemList?.ItemList?.Items ?? [];

        // The mercenary's against its own level and stats (hireling.txt); unknown kind, unjudged.
        int mercenaryLevel = Mercenary.Level;
        var forMercenary = Requirements.JudgeMercenary(_save, mercenaryLevel).ToDictionary(judged => judged.Item, (System.Collections.Generic.IEqualityComparer<D2SLib.Model.Save.Item>)ReferenceEqualityComparer.Instance);
        var mercenaryStats = _save.Mercenary is { Id: not 0 } mercenary
            ? Hirelings.StatsOf(mercenary.TypeId, mercenaryLevel, _save.Status.IsExpansion)
            : null;

        // What each wears of ethereal items, as its sheet counts them: a Purgatory says its total for whoever holds it.
        int etherealsWorn = Requirements.EtherealsWorn(active.Where(judged => judged.Met).Select(judged => judged.Item));
        int? mercenaryEthereals = mercenaryStats is null
            ? null
            : Requirements.EtherealsWorn(forMercenary.Values.Where(judged => judged.Met).Select(judged => judged.Item));

        foreach (var tile in tiles)
        {
            if (hired.Contains(tile.Item))
            {
                tile.Judge(null, forMercenary.TryGetValue(tile.Item, out var its) && mercenaryStats is { } stats
                    ? its.Lines(mercenaryLevel, stats.Strength, stats.Dexterity)
                    : null, etherealsWorn: mercenaryEthereals, holderLevel: mercenaryLevel);
            }
            else if (verdicts.TryGetValue(tile.Item, out var judged))
                tile.Judge(classCode, judged.Lines(level, strength, dexterity), Requirements.AmmunitionMissing(_save, tile.Item), etherealsWorn, level);
            else
            {
                var need = Requirements.Of(tile.Item, classCode);
                tile.Judge(classCode, (level >= need.Level, strength + carried.Strength >= need.Strength,
                                       dexterity + carried.Dexterity >= need.Dexterity), etherealsWorn: etherealsWorn, holderLevel: level);
            }
        }
    }

    /// <summary>
    /// Why the character cannot wear <paramref name="item"/> at <paramref name="at"/>, or null -
    /// its slot, its class, the hands, a belt's rows (<see cref="Equipping.CannotWear"/>). The
    /// item itself is left out of what is worn, should it be moving between slots.
    /// </summary>
    public string? CannotWear(Item item, ItemLocation at)
    {
        if (_save?.PlayerItemList is not { } list)
            return L("No character open.");

        string? classCode = CharacterClasses.CodeOf(_save.ClassId);
        Item? WornAt(ItemLocation where) => list.Items.FirstOrDefault(each =>
            each.Mode == ItemMode.Equipped && each.Location == where && !ReferenceEquals(each, item));

        int beltRows = list.Items.Where(each => each.Mode == ItemMode.Belt)
            .Select(each => each.X / Belts.Columns + 1).DefaultIfEmpty(0).Max();

        // A belt with fewer rows than the potions reach: they spill, where they have somewhere to go
        // (BeltSpill) - asked last, so a thing that is no belt is told that first.
        int rows = Belts.RowsFor(item.Code);
        if (at == ItemLocation.Waist && beltRows > rows && BeltSpill is { } spill)
            return Equipping.CannotWear(item, at, classCode, WornAt, rows) ?? spill(rows, true, out _);

        return Equipping.CannotWear(item, at, classCode, WornAt, beltRows);
    }

    /// <summary>
    /// Why the mercenary cannot wear <paramref name="item"/> at <paramref name="at"/>, or null -
    /// its weapons (<c>hireling.txt</c> and what PD2 adds for its kind), a class's own items only where its
    /// kind takes them, the body locations (<see cref="Equipping.CannotWearMercenary"/>).
    /// </summary>
    public string? CannotWearMercenary(Item item, ItemLocation at)
    {
        if (_save?.MercenaryItemList?.ItemList is not { } hired || _save.Mercenary is not { Id: not 0 } mercenary)
            return L("No mercenary.");

        Item? WornAt(ItemLocation where) => hired.Items.FirstOrDefault(each =>
            each.Mode == ItemMode.Equipped && each.Location == where && !ReferenceEquals(each, item));

        return Equipping.CannotWearMercenary(item, at, Hirelings.ActOf(mercenary.TypeId, _save.Status.IsExpansion),
            Hirelings.WeaponsOf(mercenary.TypeId, _save.Status.IsExpansion), WornAt);
    }

    /// <summary>
    /// Puts <paramref name="item"/> on at <paramref name="at"/> - on the character, or with
    /// <paramref name="mercenary"/> on the mercenary. What was worn there goes where the item came
    /// from if it fits, else to the first room on the character, else beyond it - the shared page a
    /// pane shows (<see cref="Beyond"/>) - else nothing moves. Then its
    /// requirements, counting the gear of whoever wears it: unmet, it comes off again unless
    /// <paramref name="force"/> - Legit's rule - and with it, a warning.
    /// <para>
    /// <paramref name="arriving"/>: the item is not the character's yet - the shared stash has
    /// let go of it, or the vault is handing it out - and is taken in, if stamped for the
    /// character's era, its identifier kept unless it clashes. <paramref name="keepId"/> keeps it
    /// even then: the vault's withdraw, where a shared id is how a copy shows (see Adopt).
    /// </para>
    /// </summary>
    /// <returns>Null when worn and met; otherwise what to say - refused, or worn with a warning (<paramref name="worn"/>).</returns>
    public string? Wear(Item item, ItemLocation at, bool arriving, bool force, out bool worn, bool mercenary = false,
                        bool keepId = false)
    {
        worn = false;
        if (_save?.PlayerItemList is not { } own)
            return L("No character open.");

        var hired = _save.MercenaryItemList?.ItemList;
        var into = mercenary ? hired : own;
        if (into is null)
            return L("No mercenary.");

        if ((mercenary ? CannotWearMercenary(item, at) : CannotWear(item, at)) is { } refused)
            return refused;

        if (arriving && Refuses(item) is { } no)
            return no;

        // A belt going on the character: the potions above its rows land elsewhere first (asked in CannotWear)
        // - before anything is moved, so they are placed on pages as they stand, and before the belt it pushes
        // off is given a place, which is then found among them.
        Action? potionsBack = null;
        if (!mercenary && at == ItemLocation.Waist && BeltSpill is { } spill
            && spill(Belts.RowsFor(item.Code), false, out potionsBack) is { } noRoom)
            return noRoom;

        // What comes off for it: the piece worn there, and what the other hand holds where both hands take it.
        var displaced = PushedOffBy(item, at, mercenary);

        // Where each one taken off goes, on the character: the first into the cell the new one leaves, else the
        // first room - each found after the ones before, so no two are given one cell. With no room there,
        // beyond the character (Beyond) - asked now, done once the new one stays on. All of them or nothing.
        var homes = new Dictionary<Item, (byte Page, int X, int Y)>(ReferenceEqualityComparer.Instance);
        Func<string?>? beyond = null;
        if (displaced.Count > 0)
        {
            var maps = new Dictionary<byte, ItemMover.Occupancy>();
            ItemMover.Occupancy MapOf(byte page)
            {
                if (!maps.TryGetValue(page, out var map))
                {
                    maps[page] = map = new ItemMover.Occupancy(SizeOfPage(page),
                        own.Items.Where(each => each.Mode == ItemMode.Stored && each.Page == page && !ReferenceEquals(each, item)));
                }

                return map;
            }

            void Take(Item pushed, byte page, int x, int y)
            {
                var (wide, tall) = ItemMover.Footprint(pushed);
                MapOf(page).Block(x, y, wide, tall);
                homes[pushed] = (page, x, y);
            }

            foreach (var pushed in displaced)
            {
                if (homes.Count == 0 && !arriving && item.Mode == ItemMode.Stored && own.Items.Contains(item))
                {
                    var there = own.Items.Where(each => each.Mode == ItemMode.Stored && each.Page == item.Page && !ReferenceEquals(each, item));
                    if (ItemMover.CanPlace(pushed, there, SizeOfPage(item.Page), item.X, item.Y).Allowed)
                    {
                        Take(pushed, item.Page, item.X, item.Y);
                        continue;
                    }
                }

                foreach (byte page in LandingOrder)
                {
                    if (ItemMover.FirstFreeCell(pushed, MapOf(page)) is { } cell)
                    {
                        Take(pushed, page, cell.X, cell.Y);
                        break;
                    }
                }
            }

            var homeless = displaced.Where(pushed => !homes.ContainsKey(pushed)).ToList();
            if (homeless.Count > 0 && (beyond = Beyond?.Invoke(homeless)) is null)
            {
                potionsBack?.Invoke();
                return L($"No free space for {ItemPresenter.Describe(homeless[0]).Name}.");
            }
        }

        // As it was, should its requirements send it back: fields, and which list each is in.
        var before = (item.Mode, item.Location, item.Page, item.X, item.Y);
        var displacedBefore = displaced.Select(pushed => (pushed.Mode, pushed.Location, pushed.Page, pushed.X, pushed.Y)).ToList();
        var from = arriving ? null : own.Items.Contains(item) ? own : hired?.Items.Contains(item) == true ? hired : CorpseHolding(item);
        uint id = item.Id;

        void Move(Item what, ItemList? leaving, ItemList? joining)
        {
            if (ReferenceEquals(leaving, joining))
                return;

            leaving?.Items.Remove(what);
            joining?.Items.Add(what);
        }

        void Recount()
        {
            own.Count = (ushort)own.Items.Count;
            if (hired is not null)
                hired.Count = (ushort)hired.Items.Count;
        }

        if (arriving && !keepId && into.Items.Concat(own.Items).Any(each => each.Id == item.Id))
            item.Id = NewItemId();

        // Put in one of the character's hands, it is the last put on: after every other item in the save.
        // The game draws the weapon put on FIRST in the right hand, whichever slot each is in, and one picked
        // up is one taken off - set down again anywhere, it is the later of the two (found in game).
        // The game loads a save's items in their order, so the order is how a save says it.
        bool lastOn = !mercenary && at is ItemLocation.RightHand or ItemLocation.LeftHand;
        int wasAt = own.Items.IndexOf(item);

        Move(item, from, into);
        Equipping.Place(item, at);
        if (lastOn && own.Items.Remove(item))
            own.Items.Add(item);

        foreach (var pushed in displaced)
        {
            // With a place on the character, there; else in neither list while the new one is judged - its
            // stats must not count - and beyond once that is settled.
            bool placed = homes.TryGetValue(pushed, out var to);
            Move(pushed, into, placed ? own : null);
            if (placed)
                ItemMover.Place(pushed, to.Page, to.X, to.Y);
        }

        Recount();

        // The requirements, as the game judges them - the character's in the hand the slot
        // belongs to, or the mercenary's against its own stats.
        Requirements.Judged? verdict;
        if (mercenary)
        {
            verdict = Requirements.JudgeMercenary(_save, Mercenary.Level).FirstOrDefault(each => ReferenceEquals(each.Item, item));
        }
        else
        {
            int set = at is ItemLocation.SwapRight or ItemLocation.SwapLeft ? 1
                    : WeaponSets.InHand;
            int strength = AttributeScale.Display("strength", _save.Attributes.Stats.GetValueOrDefault("strength"));
            int dexterity = AttributeScale.Display("dexterity", _save.Attributes.Stats.GetValueOrDefault("dexterity"));
            verdict = Requirements.Judge(_save, set, strength, dexterity).FirstOrDefault(each => ReferenceEquals(each.Item, item));
        }

        // Back as it was: nothing moved.
        void Undo()
        {
            for (int each = 0; each < displaced.Count; each++)
            {
                var pushed = displaced[each];
                Move(pushed, homes.ContainsKey(pushed) ? own : null, into);
                (pushed.Mode, pushed.Location, pushed.Page, pushed.X, pushed.Y) = displacedBefore[each];
            }

            Move(item, into, from);
            (item.Mode, item.Location, item.Page, item.X, item.Y) = before;
            item.Id = id;

            // And where it was among the character's items, should it have been made the last put on.
            if (lastOn && wasAt >= 0 && own.Items.Remove(item))
                own.Items.Insert(Math.Min(wasAt, own.Items.Count), item);
            Recount();

            potionsBack?.Invoke();
        }

        string? warning = null;
        if (verdict is { Met: false, Short: { } lacking })
        {
            if (!force)
            {
                Undo();
                return L($"Require {lacking}.");
            }

            warning = L($"Require {lacking} - equipped anyway.");
        }

        // The one pushed off with no room on the character: onto the shared page now, the new one staying on. Should the
        // page turn it away, it is worn again: nothing else has a place for it, and in no list it would be lost.
        if (beyond?.Invoke() is { } turnedAway)
        {
            Undo();
            return turnedAway;
        }

        // A belt let go of on the slot it was lifted from has gone nowhere: what it spilled comes back.
        if (!mercenary && before.Mode == ItemMode.Equipped && before.Location == at && ReferenceEquals(from, own))
            PutSpillBack();

        worn = true;
        Held = null;
        HeldIsShared = false;
        HeldGroup = [];

        MarkEdited();
        Rebuild();
        return warning;
    }

    /// <summary>
    /// Every stat the gear in hand gives - worn, but the other weapon set's, and the charms in the inventory -
    /// with sockets and set bonuses, as the requirements count them (Requirements.GearStats): what the skill
    /// tree counts a skill's bonus levels from. Only gear in use: an item whose level, strength or dexterity
    /// is not met gives nothing in the game, and nothing here.
    /// Empty with no character open.
    /// </summary>
    public IReadOnlyList<(string Stat, int Value, int Param)> GearInHand() =>
        _save is null ? [] : [.. Requirements.GearStats(Requirements.InUse(_save))];

    /// <summary>
    /// What the gear in use adds to each of the four attributes, by the save's names
    /// (Requirements.AttributesFrom): shown after an attribute's own points. Nothing with no character open.
    /// </summary>
    public IReadOnlyDictionary<string, int> GearAttributes() =>
        _save is null ? new Dictionary<string, int>() : Requirements.AttributesFrom(GearInHand(), _save.Level);

    /// <summary>
    /// Refit: points into Strength and Dexterity until every worn item of both weapon sets meets them -
    /// unspent points first, then Energy, then Vitality, never below the class's starting values,
    /// one point at a time through the attribute editor, so life and mana unwind as the buttons do -
    /// the gear's own bonuses helping. A level too low cannot be met by points and is only said.
    /// </summary>
    /// <returns>What happened, for the message strip.</returns>
    public string Refit()
    {
        if (_save is null || Attributes is not { IsAvailable: true } attributes)
            return L("No character open.");

        var (strength, dexterity) = Requirements.Least(_save, countGear: true, from: (attributes.Value("strength"), attributes.Value("dexterity")));
        int needStrength = Math.Max(0, strength - attributes.Value("strength"));
        int needDexterity = Math.Max(0, dexterity - attributes.Value("dexterity"));

        string? classCode = CharacterClasses.CodeOf(_save.ClassId);
        int levelNeeded = new[] { 0, 1 }.SelectMany(set => Requirements.Counted(_save, set))
            .Select(item => Requirements.Of(item, classCode).Level).DefaultIfEmpty(0).Max();
        string levelNote = levelNeeded > _save.Level ? " " + L($"Require level {levelNeeded}.") : "";

        if (needStrength == 0 && needDexterity == 0)
            return L("Requirements already met.") + levelNote;

        int fromUnspent = 0, fromEnergy = 0, fromVitality = 0;

        // One point into the attribute, from the first source that has one.
        bool Take(string attribute)
        {
            if (attributes.Unspent > 0)
                fromUnspent++;
            else if (attributes.CanLower("energy"))
            {
                attributes.Lower("energy");
                fromEnergy++;
            }
            else if (attributes.CanLower("vitality"))
            {
                attributes.Lower("vitality");
                fromVitality++;
            }
            else
                return false;

            attributes.Raise(attribute);
            return true;
        }

        int gotStrength = 0, gotDexterity = 0;
        while (gotStrength < needStrength && Take("strength"))
            gotStrength++;
        while (gotDexterity < needDexterity && Take("dexterity"))
            gotDexterity++;

        MarkEdited();
        RaiseAttributesChanged();

        var raised = new List<string>();
        if (gotStrength > 0) raised.Add(L($"Strength +{gotStrength}"));
        if (gotDexterity > 0) raised.Add(L($"Dexterity +{gotDexterity}"));

        var from = new List<string>();
        if (fromUnspent > 0) from.Add(L($"unspent {fromUnspent}"));
        if (fromEnergy > 0) from.Add(L($"Energy {fromEnergy}"));
        if (fromVitality > 0) from.Add(L($"Vitality {fromVitality}"));

        var short_ = new List<string>();
        if (gotStrength < needStrength) short_.Add(L($"{needStrength - gotStrength} Strength"));
        if (gotDexterity < needDexterity) short_.Add(L($"{needDexterity - gotDexterity} Dexterity"));

        string gained = Joined(raised), source = Joined(from);
        string said = raised.Count > 0 ? L($"{gained}, from {source}.") : "";
        if (short_.Count > 0)
            said += (said.Length > 0 ? " " : "") + L($"No points left for {Joined(short_)}.");

        return said + levelNote;
    }

    /// <summary>Rows of four boxes the belt worn gives - one with none (see Belts).</summary>
    public int BeltRows =>
        Belts.RowsFor((_save?.PlayerItemList?.Items ?? [])
            .FirstOrDefault(item => item.Mode == ItemMode.Equipped && item.Location == ItemLocation.Waist)?.Code);

    /// <summary>The belt's boxes in use and the kind in each, leaving out what is on its way somewhere.</summary>
    public Dictionary<int, string?> BeltTaken(IEnumerable<Item> moving)
    {
        var leaving = moving.ToHashSet(ReferenceEqualityComparer.Instance);
        return (_save?.PlayerItemList?.Items ?? [])
            .Where(item => item.Mode == ItemMode.Belt && !leaving.Contains(item))
            .GroupBy(item => (int)item.X)
            .ToDictionary(box => box.Key, box => Belts.KindOf(box.First()));
    }

    /// <summary>
    /// Puts items into belt boxes (see Belts for which), one rebuild for the lot. Each is this
    /// character's already, or arriving from elsewhere - the shared stash, which has let go of
    /// it - and then taken in, its identifier kept unless it clashes, as <see cref="Adopt"/> does.
    /// Nothing is put down unless every one can be: one arriving that <see cref="Refuses"/> turns
    /// away refuses the lot.
    /// </summary>
    public string? IntoBelt(IReadOnlyList<(Item Item, int Box, bool Arriving)> placing)
    {
        if (_save is not { PlayerItemList: { } list })
            return Status = L("No character open.");

        foreach (var (item, _, arriving) in placing)
        {
            if (arriving && Refuses(item) is { } refused)
                return Status = refused;
        }

        foreach (var (item, box, arriving) in placing)
        {
            if (arriving)
            {
                if (list.Items.Any(each => each.Id == item.Id))
                    item.Id = NewItemId();

                list.Items.Add(item);
            }

            item.Mode = ItemMode.Belt;
            item.Location = ItemLocation.None;
            item.Page = 0;
            item.X = (byte)box;
            item.Y = 0;
        }

        list.Count = (ushort)list.Items.Count;

        Held = null;
        HeldIsShared = false;

        MarkEdited();
        Rebuild();
        return null;
    }

}
