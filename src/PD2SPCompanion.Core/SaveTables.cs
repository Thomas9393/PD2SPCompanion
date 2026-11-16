using D2SLib;
using D2SLib.Model.TXT;

using PD2SPCompanion.Core.Graphics;

namespace PD2SPCompanion.Core;

/// <summary>
/// Bridges the live MPQ chain to the save parser's table model. The vendored parser
/// was written to load tables from a folder of .txt files; here they come straight
/// out of the installed game instead, so they are never stale.
/// </summary>
public static class SaveTables
{
    public static TXT Load(GameData game) => new()
    {
        ItemStatCostTXT = ItemStatCostTXT.Read(Excel(game, "itemstatcost")),
        ItemsTXT = new ItemsTXT
        {
            ArmorTXT = ArmorTXT.Read(Excel(game, "armor")),
            WeaponsTXT = WeaponsTXT.Read(Excel(game, "weapons")),
            MiscTXT = MiscTXT.Read(Excel(game, "misc")),
            UniqueTXT = UniqueTXT.Read(Excel(game, "uniqueitems")),
            SetItemsTXT = SetItemsTXT.Read(Excel(game, "setitems")),
            RunewordTXT = RunewordTXT.Read(Excel(game, "runes")),
        },
    };

    /// <summary>
    /// Installs the tables as the parser's globally-held set. <paramref name="announce"/> off: installed, and
    /// said to no one until <see cref="InstalledTables.Announce"/> (InstalledTables.InstalledQuietly).
    /// </summary>
    public static void Install(GameData game, bool announce = true)
    {
        D2SLib.Core.TXT = Load(game);

        // Which season's encoding columns this table carries, found by pattern. The reader
        // needs it before it touches an item, and it changes with the installed season.
        StatEncoding.Discover(D2SLib.Core.TXT.ItemStatCostTXT);

        // First: a skill's shown name is a string key, and a table read before the strings
        // were in had nothing to resolve it against.
        StringTable.Load(game);

        SkillNames.Load(game);
        MonsterNames.Load(game);
        StateNames.Load(game);
        CharmArea.Load(game);
        Stacks.Load(game);
        ItemTier.Load(game);
        StaffMods.Load(game);
        ItemBases.Load(game);
        Belts.Load(game);
        ItemTypes.Load(game);
        SocketEffects.Load(game);
        StatText.Load(game);
        Dyes.Load(game);
        SkillTabs.Load(game);
        // Before the affix and corruption tables, which ask it which properties keep two
        // fields where the others keep a range, and must not sort those two.
        AffixRanges.Load(game);
        AffixNames.Load(game);
        MagicAffixes.Load(game);

        // Level, strength and dexterity items ask - after the affixes, whose levelreq it reads.
        Requirements.Load(game);

        // The skill tree's tooltips: skilldesc's hard-point lines and synergies.
        SkillTips.Load(game);

        // The character sheet: each skill's formulas, and the animations its breakpoints are counted on.
        SkillRows.Load(game);
        SkillStates.Load(game);
        AnimData.Load(game);

        // Where an item may be worn: body locations and hands.
        Equipping.Load(game);
        Hirelings.Load(game);
        StatCatalogue.Load(game);
        Corruptions.Load(game);
        Puzzlebox.Load(game);

        // Before the ranges, which ask it what a runeword grants, and before anything that
        // names an item: a runeword's name is the recipe's, not the base's.
        Runewords.Load(game);
        SetNames.Load(game);
        QualityItems.Load(game);
        BaseUpgrades.Load(game);

        // After the bases, the types and the stacks it asks, and the materials tab's list.
        ForgeScope.Load(game);

        // After the affix tables, whose properties it reads a recipe's by, and the stat catalogue.
        MapRecipes.Load(game);
        ColourMaps.Load(game);
        ItemTints.Load(game);
        SpriteLoader.Install(game, Palette.Load(game));
        SkillIcons.Install(game, Palette.Load(game));
        FigureLoader.Install(game, Palette.Load(game));
        TownBackdrops.Install(game);
        AttributeScale.Reset();

        // Last: whatever was built from the tables before is now another install's.
        if (announce)
            InstalledTables.Installed();
        else
            InstalledTables.InstalledQuietly();
    }

    private static Stream Excel(GameData game, string table)
    {
        var result = game.Read(GameData.ExcelPath(table))
            ?? throw new FileNotFoundException($"'{table}.txt' is not present in any loaded archive.");
        return new MemoryStream(result.Data);
    }
}
