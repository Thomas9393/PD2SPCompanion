namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// A mercenary's own skills on its sheet:
/// none of what its skills give for good was counted - the Rogue's 66% Pierce (monstats'
/// RoguePierce, which hireling.txt does not give), her Dodge, an Iron Wolf's mastery - nor the party's skills on its
/// aura. Whirlwind's Desert Guard as its page reads (screenshot), and a Rogue by the tables.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MercenaryOwnSkillsTests(Game game)
{
    private static CharacterSheet Mercenary(GameData data, params string[] path)
    {
        var (save, _) = SaveVerifier.Load(Path.Combine([Game.Corpus, .. path]));
        return CharacterSheet.ForMercenary(save, data)!;
    }

    [CorpusFact]
    public void Whirlwinds_guard_reads_as_its_page()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var sheet = Mercenary(data, "sheet", "ingame-2026-10-20", "Whirlwind-mercenary.d2s");
        int Of(string stat) => sheet.Totals.Of(stat);

        // "Damage Reduction: 10/50% 15/0%": its gear's 40% (Crown of Ages 21, String of Ears 19) and its Defiance -
        // 18, and 3 of Whirlwind's Battle Command: 21, lvl/2 = 10 and 10%.
        Assert.Equal((10, 50, 15), (Of("normal_damage_reduction"), Of("damageresist"), Of("magic_damage_reduction")));

        // "Hit Recovery: 50 Run/Walk: 45", "Attack Rate: 80 IAS: 90", "HP/MP per Kill: 4 / 0", "Projectile Pierce: 0",
        // "Elemental Mastery: 0% 0% 0% 0% 0%".
        Assert.Equal((50, 45, 80, 90), (Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal((4, 0, 0), (Of("item_healafterkill"), Of("item_manaafterkill"), sheet.ProjectilePierce.Value));
        Assert.Equal(0, Of("passive_fire_mastery") + Of("passive_cold_mastery") + Of("passive_ltng_mastery"));

        // "Base Damage: ... 2h: 61-1333": its ethereal superior Pride's per-level max damage rounded apart from the 20%.
        Assert.Equal((61, 1333), (Of("secondary_mindamage"), Of("secondary_maxdamage")));
    }

    [CorpusFact]
    public void A_Rogue_has_her_66_pierce_and_Dodges_hit_recovery()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);

        // A level 99 Paladin's Fire Rogue, Hell, level 98, nothing worn: RoguePierce's 66 (skills.txt passivecalc1, and the wiki:
        // "a baseline 66% pierce chance"), Dodge's "4 + lvl" at 5 - not yet seen on the game's page.
        var sheet = Mercenary(data, "aliases", "paladin-99-1--stress.d2s");
        Assert.Equal(66, sheet.ProjectilePierce.Value);
        Assert.Equal(9, sheet.Totals.Of("item_fastergethitrate"));
    }
}
