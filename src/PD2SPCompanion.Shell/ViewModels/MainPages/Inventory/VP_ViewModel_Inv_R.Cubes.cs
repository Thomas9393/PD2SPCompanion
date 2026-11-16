using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Core.Graphics;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

// What a socket filler gives, the tag under the name, and the cubes: the Demonic Cube's reroll and the
// Horadric Cube's upgrade. Corruption itself is CorruptionEditor.
public partial class VP_ViewModel_Inv_R
{
    /// <summary>
    /// What this gem, rune or jewel would give the item it went into.
    /// <para>
    /// Shown for one lying loose, the way the game's own tooltip does it: a line per kind
    /// of host, because a rune is a different thing in a weapon than in a helm. A socketed
    /// one is described on the socket row of the item holding it instead, where the host is
    /// known and only one of the three applies.
    /// </para>
    /// </summary>
    public bool IsSocketFiller => Session.SelectedItem is { } tile
        && tile.Item.Mode != ItemMode.Socket
        && SocketEffects.Known(tile.Item.Code);

    public IReadOnlyList<string> WeaponEffect => Filling.Weapon;
    public IReadOnlyList<string> ArmorEffect => Filling.Armor;
    public IReadOnlyList<string> ShieldEffect => Filling.Shield;

    public bool HasWeaponEffect => WeaponEffect.Count > 0;
    public bool HasArmorEffect => ArmorEffect.Count > 0;
    public bool HasShieldEffect => ShieldEffect.Count > 0;

    private (IReadOnlyList<string> Weapon, IReadOnlyList<string> Armor,
             IReadOnlyList<string> Shield) Filling =>
        SocketEffects.Everywhere(Session.SelectedItem?.Item.Code);

    /// <summary>
    /// A Giant Conch, for the button that makes a normal item superior (picked after an armour
    /// with an arrow, the Hellforge Hammer and an Archon Plate): the game's own picture of
    /// one, read from the install as the cubes' are.
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap? MakeSuperiorIcon =>
        SpriteBitmap.From(SpriteLoader.Current?.Load("invfhl"));

    /// <summary>The Horadric Cube, which is what a craft recipe is put through.</summary>
    public Avalonia.Media.Imaging.Bitmap? HoradricCubeIcon =>
        SpriteBitmap.From(SpriteLoader.Current?.Load("invbox"));

    /// <summary>
    /// Whether the reroll is the Forge's - an item nobody owns yet, made as a new drop - and not the game's
    /// Demonic Cube on an item a character has (IItemEditHost.RollsAsDrop).
    /// </summary>
    public bool RollsAsDrop => Session.RollsAsDrop;

    /// <summary>
    /// Whether the cube's button is the page's: an item that is someone's, with a number to roll - of a kind the cube
    /// takes (<see cref="CubeTakesKind"/>).
    /// </summary>
    public bool ShowsCube => !RollsAsDrop && CanReroll && CubeTakesKind;

    /// <summary>
    /// Whether the Edit page's cube takes this kind of item:
    /// in Free, anything with a range; else the game's Demonic Cube's own - a unique or a set item, the only ones its
    /// sixteen rows in cubemain.txt take (a plain staff with staff mods refused, seen in game).
    /// </summary>
    private bool CubeTakesKind =>
        Rules.EditsPropertiesFreely
        || (Session.SelectedItem is { } tile && Staged(tile.Item).Quality is ItemQuality.Unique or ItemQuality.Set);

    /// <summary>
    /// A corrupted item, which the game's cube refuses (seen in game): the cube dimmed and saying so. In
    /// Free it rolls the corruption's numbers with the rest.
    /// </summary>
    private bool CubeRefusesCorruption =>
        !RollsAsDrop && !Rules.EditsPropertiesFreely
        && Session.SelectedItem is { } tile && Corruptions.IsCorrupted(Staged(tile.Item));

    /// <summary>The roll's button may be pressed: dimmed only where the cube refuses a corrupted item.</summary>
    public bool CanRollNow => !CubeRefusesCorruption;

    /// <summary>Whether the Forge's orb is: an item to be made, with anything of it to roll.</summary>
    public bool ShowsOrb => RollsAsDrop && CanReroll;

    /// <summary>
    /// Whether this item has something worth rolling again: a property's number, or the class or skill a
    /// property drew at random - and, on the Forge, its graphic, its base defense or its own socket range.
    /// </summary>
    public bool CanReroll => Session.SelectedItem is { } tile
                             && (DemonicCube.CanTake(Staged(tile.Item))
                                 || Stats.Any(row => !row.IsDeleted && (row.ClassIsDrawn || row.SkillIsDrawn))
                                 || RollsItsGraphic
                                 || (RollsAsDrop && (Sockets.RollsItsOwn || RollsItsDefense || RollsDurability)));

    /// <summary>An item of several pictures - a ring, an amulet, a jewel, a charm - where the graphic's own die is offered.</summary>
    private bool RollsItsGraphic => Rules.EditsDetails && Basic.HasGraphic && Basic.CanRerollGraphic;

    /// <summary>Armor whose base defense the game rolls in a range - not one that spawned with Enhanced Defense, whose base is fixed.</summary>
    private bool RollsItsDefense => Rules.EditsDetails && Basic.HasDefense && Basic.CanRerollDefense;

    /// <summary>An item with durability, whose current one a new drop rolls (ItemBases.DurabilityRoll).</summary>
    private bool RollsDurability => Rules.EditsDetails && Basic.HasDurability && Basic.CanRerollDurability;

    /// <summary>
    /// Gear a drop comes ethereal or plain by chance (GrailCatalog.DropsEitherWay): not a bow, a crossbow or another
    /// base the game never makes ethereal; not a row ethereal always - an Ethereal Edge drawn
    /// plain 19 times in 20 - nor an indestructible one.
    /// </summary>
    private bool RollsEthereal => Rules.EditsDetails && Basic.HasEthereal
        && Session.SelectedItem is { } tile && PD2SPCompanion.Core.Collection.GrailCatalog.DropsEitherWay(tile.Item);

    /// <summary>What the base itself rolled - its automod's row, its staff mods, a superior bonus: drawn where affixes are edited.</summary>
    private bool RollsKinds => Rules.EditsAffixes;

    /// <summary>A magic, rare or crafted item, whose affixes a new drop draws.</summary>
    private bool RollsAffixes => Rules.EditsAffixes && Affixes.CanRerollAffixes;

    /// <summary>One drop in so many is ethereal: the game's odds, as every die of the editor's keeps them.</summary>
    private const int EtherealOneIn = 20;

    /// <summary>
    /// The button's tooltip. The cube's is its name and key; the Forge's says what its roll touches on the item
    /// in the holder, and what Shift adds.
    /// </summary>
    public string RerollHint
    {
        get
        {
            if (!RollsAsDrop)
            {
                string cube = ShortcutText.With(L("Reroll all stats"), L("Ctrl + R"));
                return CubeRefusesCorruption ? ShortcutText.Dimmed(cube, L("Corrupted.")) : cube;
            }

            var rolled = new List<string>();
            if (Session.SelectedItem is { } tile
                && (DemonicCube.CanTake(Staged(tile.Item)) || Stats.Any(row => !row.IsDeleted && (row.ClassIsDrawn || row.SkillIsDrawn))))
            {
                rolled.Add(L("stats"));
            }

            if (StaffSkills.HasMods)
                rolled.Add(L("staffmods"));
            if (RollsItsGraphic)
                rolled.Add(L("graphic"));
            if (RollsItsDefense)
                rolled.Add(L("defense"));
            if (Sockets.RollsItsOwn)
                rolled.Add(L("sockets"));
            if (RollsDurability)
                rolled.Add(L("durability"));

            var more = new List<string>();
            if (RollsEthereal)
                more.Add(L("ethereal"));
            if (RollsAffixes)
                more.Add(L("affixes"));
            if (RollsKinds && Automagic.Shows)
                more.Add(L("automod"));
            if (RollsKinds && StaffSkills.Shows)
                more.Add(L("staffmods"));
            if (RollsKinds && Superior.CanDraw)
                more.Add(L("superior"));
            if (Sockets.CanDropRoll)
                more.Add(L("sockets"));

            var lines = new List<string> { ShortcutText.With(L("Roll as a new drop"), L("Ctrl + R")) };
            if (rolled.Count > 0)
                lines.Add(L($"Rerolls {Listed(rolled)}"));
            if (more.Count > 0)
                lines.Add(L($"Shift + LMB: {Listed(more)} too"));
            return string.Join(Environment.NewLine, lines);

            static string Listed(List<string> words) => words.Count switch
            {
                1 => words[0],
                _ => L($"{string.Join(", ", words.Take(words.Count - 1))} and {words[^1]}"),
            };
        }
    }

    /// <summary>
    /// The reroll by its key - Ctrl + R, and on the Forge Ctrl + Shift + R for the fuller one - only where its
    /// button is: the play mode edits affixes and the item has something to roll. True where it rolled.
    /// </summary>
    public bool RerollByKey(bool full = false)
    {
        // The fuller roll is the Forge's alone: on an item that is someone's, the key is no key.
        if (full && !RollsAsDrop)
            return false;

        if (!Rules.EditsAffixes || !CanReroll || !RerollItemCommand.CanExecute(null)
            || (!RollsAsDrop && (!CubeTakesKind || CubeRefusesCorruption)))
            return false;

        Roll(full);
        return true;
    }

    /// <summary>The button pressed: the cube's reroll, or the Forge's plain one.</summary>
    [RelayCommand]
    private void RerollItem() => Roll(full: false);

    /// <summary>The Forge's fuller roll, by Shift on its button (the view's, which sees the key held).</summary>
    public void RollAsNewDrop() => Roll(full: true);

    /// <summary>
    /// Two rerolls with two aims: in game the Demonic Cube only rerolls stats and changes nothing else; on the Forge
    /// this button is an easy reroll to simulate an item's spawn.
    /// <para>
    /// <b>On an item that is someone's - the Edit page - it is the game's Demonic Cube</b>:
    /// every property rolled again inside its own range; the class or skill a
    /// property drew beside its number (Hellfire Torch's class, seen in game); and the picture, where it
    /// is one of the base's several (Gheed's Fortune, a unique ring - seen in game; Annihilus and the Torch wear
    /// their own art and keep it). Nothing is added and nothing is taken away; a reroll that landed worse is taken
    /// back by Ctrl + Z. Not the socket count (fifty rerolls in game), not the base defense (never changed in game).
    /// Outside Free it takes what the game's takes - a unique or set item, not corrupted (<see cref="CubeTakesKind"/>,
    /// <see cref="CubeRefusesCorruption"/>); in Free anything with a range, a corruption's numbers with the rest.
    /// </para>
    /// <para>
    /// <b>On the Forge it makes the item as a new drop</b>, the Horadrim Orb's button. Plain, every number: the
    /// properties', the drawn class or skill, the staff mods' levels; and what else the game rolls when it makes one -
    /// its picture, its base defense where the base has a range, its socket count where that is its own range, and
    /// last its current durability, as a new item's. <b>With Shift</b> (<paramref name="full"/>), what kind of item it
    /// is too (a plain roll only on numbers, Shift touching the type as well): ethereal or not, at one in
    /// twenty; new affixes, as the Perfect Skull draws them, and a rare's name; the automod's row, the staff mods, a
    /// superior item's bonus; a normal or superior base's sockets, as it drops. Drawn first, the numbers rolled on them
    /// after, durability last. Every draw at the game's odds - it may land on what the item has. Not the item level:
    /// it says where an item dropped and decides what affixes it can have (deliberately left out).
    /// </para>
    /// <para>
    /// A property's number is asked for by the stat and what its parameter names, as the roll keys its answer
    /// (AffixRanges.KeyOf): asked by the bare name, a line that names a skill, a tree or a class was never
    /// found and kept its number.
    /// </para>
    /// </summary>
    private void Roll(bool full)
    {
        if (Session.SelectedItem is not { } tile || !CanReroll || (!RollsAsDrop && (!CubeTakesKind || CubeRefusesCorruption)))
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        var random = new Random();
        int moved = 0;

        // A new drop of its kind: ethereal or not, then the affixes it has and a rare's name - each written
        // as it is drawn, the rows built again on the new affixes before their numbers are rolled.
        if (RollsAsDrop && full)
        {
            if (RollsEthereal && (random.Next(EtherealOneIn) == 0) != Basic.IsEthereal)
            {
                Basic.ToggleEtherealCommand.Execute(null);
                moved++;
            }

            if (RollsAffixes)
            {
                Affixes.RerollAllAffixesNowCommand.Execute(null);
                moved++;
            }

            if (RareName.RollName(random))
                moved++;

            // What the base itself rolled: its automod's row where its group has several, its staff mods, a superior
            // item's bonus - each drawn as the game draws it, before the numbers rolled on them.
            if (RollsKinds)
            {
                if (Automagic.Shows && Automagic.Draw(random))
                    moved++;
                if (StaffSkills.Shows && StaffSkills.Draw(random))
                    moved++;
                if (Superior.Shows && Superior.Draw(random))
                    moved++;
            }

            if (Session.SelectedItem is not { } still || !ReferenceEquals(still, tile))
                return;
        }

        // A list's rows from its own sources: the item's own, or its runeword's (DemonicCube.Rolled).
        var rolls = DemonicCube.CanTake(Staged(tile.Item)) ? DemonicCube.Roll(Staged(tile.Item), random) : null;

        // The roll holds every number, the ones a property writes together already alike.
        _settingRows = true;
        try
        {
            foreach (var row in Stats)
            {
                // A row on its way out takes no new number.
                if (row.IsDeleted || row.IsSetBonus || row.Stat.Stat is not { Length: > 0 } name || rolls is null)
                    continue;

                var fresh = row.IsRuneword ? rolls.Runeword : rolls.Own;
                if (!fresh.TryGetValue(AffixRanges.KeyOf(row.Stat), out int rolled) && !fresh.TryGetValue(name, out rolled))
                    continue;

                // Lines of one key share the roll, as the cube's own (DemonicCube.Reroll): the first takes what the others leave.
                var key = AffixRanges.KeyOf(row.Stat);
                var sharing = Stats.Where(other => !other.IsDeleted && !other.IsSetBonus && other.IsRuneword == row.IsRuneword
                                                   && AffixRanges.KeyOf(other.Stat) == key).ToList();
                if (sharing.Count > 1)
                {
                    if (!ReferenceEquals(sharing[0], row))
                        continue;
                    rolled = Math.Max(0, rolled - sharing.Skip(1).Sum(other => other.Stat.Value));
                }

                string typed = rolled.ToString();
                if (row.Value == typed)
                    continue;

                row.Value = typed;
                moved++;
            }

            // Then what a property drew beside its number - its class, its skill. After the numbers: they are
            // asked for by the stat and what its parameter names, which a new class or skill changes.
            foreach (var row in Stats)
            {
                if (!row.IsDeleted && (row.ClassIsDrawn || row.SkillIsDrawn) && row.RerollDrawn())
                    moved++;
            }

            // Ormus' Robes: its two lines' one skill, drawn again together (RandomSkillCast).
            if (DrawSkillCast(tile.Item, random))
                moved++;
        }
        finally
        {
            _settingRows = false;
        }

        // The staff mods' levels: numbers of a new drop's, no row of the game's cube taking a staff with them.
        if (RollsAsDrop && RollsKinds && StaffSkills.RollLevels(random))
            moved++;

        // The picture, the rows settled, where it is one of the base's several - the cube's as the Forge's, each
        // picture alike: its own staging tells the panel, as the ones below.
        if (RollsItsGraphic && Basic.DrawGraphic(random))
            moved++;

        if (RollsAsDrop)
        {
            // The base's own defense, in the table's range - ethereal reckoned in, as the die's.
            if (RollsItsDefense)
            {
                string before = Basic.Defense;
                Basic.RerollDefenseCommand.Execute(null);
                if (Basic.Defense != before)
                    moved++;
            }

            // The socket count last: only a count that is the item's own stat - a unique's range, Mechanist's -
            // never the Puzzlebox's or a corruption's. A count rolled under what is socketed stages those to
            // leave, as the section's - does.
            int socketsBefore = Sockets.Staged;
            if (Sockets.RollsItsOwn && Sockets.Reroll(random) is { } sockets && sockets != socketsBefore)
                moved++;

            // With Shift, a normal or superior base's sockets as it drops - never a runeword's, nor below what is in it.
            if (full && Sockets.DropRoll(random) is { } dropped && dropped != socketsBefore)
                moved++;

            // Last, the current durability as a new item's: half the maximum to one under it - the maximum as the rest
            // left it (a superior bonus's durability, ethereal).
            if (RollsDurability)
            {
                string before = Basic.Durability;
                Basic.RerollDurabilityCommand.Execute(null);
                if (Basic.Durability != before)
                    moved++;
            }
        }

        NotifyChanges();

        string what = RollsAsDrop ? L("rolled") : L("rerolled");
        if (moved == 0)
            Say(L($"{tile.Name} {what}: no change."));
        else
            Say(L($"{tile.Name} {what}: {moved} changed."));
    }

    /// <summary>
    /// A Horadric Cube upgrade can take this item a tier up. Held to the game's recipes -
    /// unique, set, rare and crafted - unless properties are edited freely, which lets in
    /// anything with a tier above it, white and runeword too.
    /// </summary>
    public bool CanUpgradeBase =>
        Session.UpgradesBase
        && Session.SelectedItem is { TabSlot: null } tile
        && BaseUpgrades.CanUpgrade(Staged(tile.Item), anyItem: Rules.EditsPropertiesFreely);

    /// <summary>The button's tooltip: the base it goes to.</summary>
    public string UpgradeHint =>
        Session.SelectedItem is { } tile && ItemTier.Next(Staged(tile.Item).Code) is { } next
            ? L($"Upgrade to {ItemPresenter.BaseName(next)}")
            : L("Upgrade base");

    /// <summary>
    /// The Horadric Cube's upgrade: one tier up, as the game's recipes do it - see
    /// <see cref="BaseUpgrades.Upgrade"/>. Worked out on the item as the page holds it, the
    /// typed defence and durability included, and staged like every other change.
    /// </summary>
    [RelayCommand]
    private void UpgradeBase()
    {
        if (Session.SelectedItem is not { } tile || !CanUpgradeBase || Pending() is not { } shadow)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        if (Basic.HasDefense)
            shadow.Armor = (ushort)Math.Clamp(Basic.DefenseTyped, 0, 1747);
        if (Basic.HasDurability)
        {
            shadow.MaxDurability = (ushort)Math.Max(0, Basic.MaxDurabilityTyped);
            shadow.Durability = (ushort)Math.Clamp(Basic.DurabilityTyped, 0, shadow.MaxDurability);
        }
        shadow.IsEthereal = Basic.IsEthereal;

        BaseUpgrades.Upgrade(shadow, new Random());
        Draft.Code = shadow.Code.Trim();

        // Named now: a host that keeps at once (the Forge) writes the item as the rows below are staged, and
        // the staged base is let go with everything else that was staged - asked for at the end, it was
        // nothing (the Forge's upgrade showed it).
        string upgraded = ItemPresenter.BaseName(shadow.Code.Trim());

        StageStatsFrom(shadow, tile);

        if (Basic.HasDefense)
            Basic.Defense = shadow.Armor.ToString();
        if (Basic.HasDurability)
        {
            Basic.MaxDurability = shadow.MaxDurability.ToString();
            Basic.Durability = shadow.Durability.ToString();
        }

        NotifyChanges();

        Say(L($"{tile.Name} upgraded to {upgraded}."));
    }

    /// <summary>
    /// Ormus' Robes' one random skill, on its two rows, drawn again: the cast's chance and level as the rule gives them
    /// for it (RandomSkillCast). False for an item without them.
    /// </summary>
    private bool DrawSkillCast(Item item, Random random)
    {
        if (AffixRanges.RandomSkillCastsOn(item).FirstOrDefault() is not { Max: > 0 } source)
            return false;

        var grant = Stats.FirstOrDefault(row => !row.IsDeleted && row.Stat.Stat == "item_singleskill" && RandomSkillCast.Pool.Contains(row.Skill?.Id ?? -1));
        var cast = Stats.FirstOrDefault(row => !row.IsDeleted && row.Stat.Stat == "item_skilloncast" && row.Skill?.Id == grant?.Skill?.Id);
        if (grant?.Skill is not { } held || cast is null)
            return false;

        int skill = RandomSkillCast.DrawnAgain(held.Id, random);
        grant.TakeDrawnSkill(skill);
        cast.TakeDrawnSkill(skill, RandomSkillCast.Chance(skill, source.Min), RandomSkillCast.Level(skill, source.Max));
        return true;
    }

}
