using System;
using System.IO;
using System.Linq;

using Xunit;
using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// An item's lines against the game's own tooltip. The corpus character's spear - Stone Barb, a crafted
/// Matriarchal Spear holding two Shael runes, a Perfect Skull and a Ruby Jewel of Envy - as a
/// screenshot of it in game reads: what its sockets give is counted into its own lines.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ItemLinesTests(Game game, ITestOutputHelper output)
{
    /// <summary>
    /// Purgatory's lines as a screenshot of it worn reads: a repair worked out as one point
    /// every 100 / value seconds - Warp Spell's 3 read "in 33 seconds" - and a line of two drawn from
    /// the bottom up, the Gains line on top.
    /// </summary>
    [CorpusFact]
    public void A_repair_and_a_line_of_two_read_as_in_game()
    {
        if (!game.Available)
            return;

        Assert.Equal("Repairs 1 durability in 4 seconds", StatText.Describe("item_replenish_durability", 25));
        Assert.Equal("Repairs 1 durability in 33 seconds", StatText.Describe("item_replenish_durability", 3));
        Assert.Equal("Gains +60% Enhanced Damage per equipped ethereal item\n+60% Enhanced Damage (based on Equipped Ethereals)",
                     StatText.Describe("item_dmgpercent_pereth", 60));

        foreach (string stat in D2SLib.Core.TXT.ItemStatCostTXT.Rows.Select(row => row["Stat"].Value ?? ""))
            if (StatText.Describe(stat, 1) is { } said && said.Contains('\n'))
                output.WriteLine($"{stat}: {said.Replace("\n", " | ")}");
    }

    /// <summary>
    /// Purgatory held by a character says the total for the ethereal items it wears - "+300% Enhanced Damage (based on
    /// Equipped Ethereals)" with five, worn or lifted into the inventory (screenshots) - and the
    /// stored +60 where no one holds it.
    /// </summary>
    [CorpusFact]
    public void Purgatory_says_its_total_for_the_ethereals_its_holder_wears()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-29", "AuraTest.d2s"));
        var purgatory = save.PlayerItemList!.Items.Single(item => ItemPresenter.Describe(item).Name == "Purgatory");
        var gear = Requirements.Judge(save, WeaponSets.InHand, 103, 20).Where(judged => judged.Met).Select(judged => judged.Item);
        int worn = Requirements.EtherealsWorn(gear);

        string[] Said(int? ethereals) =>
            [.. ItemLines.Of(purgatory, ItemPresenter.Describe(purgatory), out _, out _, etherealsWorn: ethereals).Select(line => line.Text)];

        Assert.Equal(5, worn);
        Assert.Contains("Gains +60% Enhanced Damage per equipped ethereal item\n+300% Enhanced Damage (based on Equipped Ethereals)", Said(worn));
        Assert.Contains("Gains +60% Enhanced Damage per equipped ethereal item\n+60% Enhanced Damage (based on Equipped Ethereals)", Said(null));
    }

    [CorpusFact]
    public void A_socketed_item_reads_as_the_game_reads_it()
    {
        if (!game.Available)
            return;

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var spear = save.PlayerItemList!.Items.Single(item => ItemPresenter.Describe(item).Name == "Stone Barb");
        var own = spear.StatLists.SelectMany(list => list.Stats).Select(stat => (stat.Stat, stat.Value)).ToList();

        var lines = ItemLines.Of(spear, ItemPresenter.Describe(spear), out _, out _);
        foreach (var line in lines)
            output.WriteLine($"{line.Tone,-8} {line.Text}  {line.Range}");

        string[] said = [.. lines.Select(line => line.Text)];

        // Above the properties: the runes named in one word, and the damage worked from 524% and the jewel's 38%.
        Assert.Equal(["Matriarchal Spear", "'ShaelShael'", "Two-Hand Damage: 522 to 767", "Durability: 44 of 56"], said[..4]);
        Assert.Equal(ItemLine.Runes, lines[1].Tone);

        // Headed with its socket count, and the class its base is for over what it asks - as in game.
        Assert.Equal("Stone Barb [4]", ItemLines.Title(spear, ItemPresenter.Describe(spear)));
        Assert.Equal(["(Amazon Only)", "Required Dexterity: 142", "Required Strength: 114", "Required Level: 69"], said[4..8]);

        // Where the game has its speed word, the base's number: PD2's Matriarchal Spear is -10.
        Assert.Equal("Base Attack Speed: -10", said[8]);

        // An Amazon's, it is white; a Sorceress is told in red that it is not hers.
        Assert.Equal(ItemLine.Fact, ItemLines.Requirements(spear, "ama", null)[0].Tone);
        Assert.Equal(ItemLine.Unmet, ItemLines.Requirements(spear, "sor", null)[0].Tone);

        // The properties, in the game's order, each stat one line with its sockets' share in it; and
        // nothing under "Socketed (4)".
        string[] properties =
        [
            "Melee Attacks Deal Splash Damage",
            "+1 to Javelin and Spear Skills (Amazon Only)",
            "+40% Increased Attack Speed",
            "+562% Enhanced Damage",
            "+148 to Attack Rating",
            "+148% Damage to Demons",
            "+200 to Attack Rating against Demons",
            "Adds 73-316 cold damage",
            "+20 poison damage over 2 seconds",
            "3% Mana stolen per hit",
            "7% Life stolen per hit",
            "+20 to Life",
            "Poison Resist +14%",
            "Socketed (4)",
        ];
        Assert.Equal(properties, said[Array.IndexOf(said, "Melee Attacks Deal Splash Damage")..]);

        // The item's own stats are as they were: the sums are the tooltip's, not the item's.
        Assert.Equal(own, spear.StatLists.SelectMany(list => list.Stats).Select(stat => (stat.Stat, stat.Value)).ToList());

        // The search reads its name, its base and its stats - the runes' word among them - and none of the
        // plain lines: what it asks, its levels, its damage, its base attack speed.
        string[] text = ItemLines.TextOf(spear).Split('\n');
        Assert.Equal(["Stone Barb", "Matriarchal Spear", "'ShaelShael'", "Corrupted", "Melee Attacks Deal Splash Damage"], text[..5]);
        Assert.Contains("+40% Increased Attack Speed", text);
        // And, after its last line, what is in its sockets by name - which the tooltip does not list.
        Assert.Equal(["Socketed (4)", "Shael Rune", "Shael Rune", "Perfect Skull", "Ruby Jewel of Envy"], text[^5..]);
        Assert.DoesNotContain(text, line => line.StartsWith("Required ", StringComparison.Ordinal) || line.Contains("Level:")
                                            || line.Contains("Damage:") || line.Contains("Durability") || line.Contains("Base Attack Speed")
                                            || line == "(Amazon Only)");
    }

    [CorpusFact]
    public void Read_apart_the_item_says_its_own_and_each_socket_its_share()
    {
        if (!game.Available)
            return;

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var spear = save.PlayerItemList!.Items.Single(item => ItemPresenter.Describe(item).Name == "Stone Barb");

        var lines = ItemLines.Of(spear, ItemPresenter.Describe(spear), out _, out _, socketsApart: true);
        foreach (var line in lines)
            output.WriteLine($"{line.Tone,-8} {line.Text}  {line.Range}");

        string[] said = [.. lines.Select(line => line.Text)];

        // The item's own: its 524%, the damage that makes, its 3% life, and nothing a socket gives.
        Assert.Contains("Two-Hand Damage: 492 to 723", said);
        Assert.Contains("+524% Enhanced Damage", said);
        Assert.Contains("3% Life stolen per hit", said);
        int socketed = Array.IndexOf(said, "Socketed (4)");
        Assert.DoesNotContain(said[..socketed], line => line.Contains("Increased Attack Speed"));
        Assert.DoesNotContain(said[..socketed], line => line.Contains("Mana stolen"));

        // Under "Socketed (4)", after a rule: each socket by name, and what it gives beneath.
        Assert.Equal(ItemLine.Rule, lines[socketed + 1].Tone);
        Assert.Equal(
        [
            "Shael Rune", "+20% Increased Attack Speed",
            "Shael Rune", "+20% Increased Attack Speed",
            "Perfect Skull", "3% Mana stolen per hit", "4% Life stolen per hit",
            "Ruby Jewel of Envy", "+38% Enhanced Damage", "+20 poison damage over 2 seconds",
        ], said[(socketed + 2)..]);
    }

    [CorpusFact]
    public void A_runeword_reads_with_what_its_runes_give()
    {
        if (!game.Available)
            return;

        // Insight in a spear: Ral, Tir, Tal, Sol. The word's list holds none of what the runes give
        // (fire damage, mana after each kill, poison, minimum damage); the game's tooltip has them all.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var insight = save.PlayerItemList!.Items.Single(item => ItemPresenter.Describe(item).Name == "Insight");

        var lines = ItemLines.Of(insight, ItemPresenter.Describe(insight), out _, out _);
        foreach (var line in lines)
            output.WriteLine($"{line.Tone,-8} {line.Text}  {line.Range}");

        string[] said = [.. lines.Select(line => line.Text)];
        Assert.Contains("'RalTirTalSol'", said);
        Assert.Contains(said, line => line.Contains("fire damage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(said, line => line.Contains("poison damage over", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(said, line => line.Contains("Minimum Poison", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(said, line => line.Contains("Mana after each Kill", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Socketed (4)", said[^1]);

        // And each rune's own share, as a card says it under the rune: worded as an item's lines are.
        foreach (var rune in insight.SocketedItems)
            output.WriteLine($"{ItemPresenter.Describe(rune).Name}: {string.Join(" / ", SocketEffects.On(rune, insight))}");

        Assert.Equal(["Adds 5-30 fire damage"], SocketEffects.On(insight.SocketedItems[0], insight));
        Assert.Contains(SocketEffects.On(insight.SocketedItems[2], insight), line => line.Contains("poison damage over", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A runeword's lines carry the bracket its word rolls them in, whatever the base's quality,
    /// and a per-level line's bracket is in the
    /// line's units: "+0.75 to Strength (Based on Character Level) [0.5 - 0.75]", not the eighths the file keeps.
    /// </summary>
    /// <summary>
    /// A per-level line says what it comes to at its holder's level, as the game's "+99 to Maximum Damage (Based on
    /// Character Level)" at 99, and its figure a level where no one holds it: Enigma's 0.75 Strength, rounded down.
    /// </summary>
    [CorpusFact]
    public void A_per_level_line_says_its_total_at_the_holders_level()
    {
        if (!game.Available)
            return;

        var enigma = ItemForge.RunewordShown(Runewords.All.First(word => Runewords.GameName(word) == "Enigma"), "").Item;
        string Said(int? level) => ItemLines.Of(enigma, ItemPresenter.Describe(enigma), out _, out _, holderLevel: level)
            .Single(line => line.Text.EndsWith("to Strength (Based on Character Level)", StringComparison.Ordinal)).Text;

        Assert.Equal("+0.75 to Strength (Based on Character Level)", Said(null));
        Assert.Equal("+74 to Strength (Based on Character Level)", Said(99));
        Assert.Equal("+0 to Strength (Based on Character Level)", Said(1));
    }

    /// <summary>Every gem and rune lying loose, a materials tab's slot too, says what it gives in each kind of host; in a socket it does not.</summary>
    [CorpusFact]
    public void A_loose_gem_or_rune_says_what_it_gives_in_each_host_in_the_tab_too()
    {
        if (!game.Available)
            return;

        string[] Lines(D2SLib.Model.Save.Item item, int? tally = null) =>
            [.. ItemLines.Of(item, ItemPresenter.Describe(item), out _, out _, tally).Select(line => line.Text)];

        var ral = ItemForge.Material("r08", 1, "105");
        string[] said = Lines(ral);
        Assert.Equal(["Weapon", "Armor, Helm", "Shield"], said.Where(line => line is "Weapon" or "Armor, Helm" or "Shield"));
        Assert.Contains(said, line => line.Contains("fire damage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(said, line => line.Contains("Fire Resist", StringComparison.OrdinalIgnoreCase));

        int told = 0;
        for (int slot = 1; slot <= Materials.Every; slot++)
        {
            string code = Materials.CodeAt(slot);
            if (!SocketEffects.Known(code))
                continue;

            told++;
            Assert.Contains("Weapon", Lines(ItemForge.Material(code, 1, "105"), 5));
        }

        output.WriteLine($"{told} slots said what they give");
        Assert.True(told >= 33, $"{told} slots said what they give");

        ral.Mode = D2SLib.Model.Save.ItemMode.Socket;
        Assert.DoesNotContain("Weapon", Lines(ral));
    }

    [CorpusFact]
    public void A_runewords_lines_are_bracketed_and_a_per_level_bracket_reads_in_its_own_units()
    {
        if (!game.Available)
            return;

        D2SLib.Model.Save.Item Made(string name) =>
            ItemForge.RunewordShown(Runewords.All.First(word => Runewords.GameName(word) == name), "").Item;
        ItemLine Line(D2SLib.Model.Save.Item item, string starts) =>
            ItemLines.Of(item, ItemPresenter.Describe(item), out _, out _).Single(line => line.Text.StartsWith(starts, StringComparison.Ordinal));

        var insight = Made("Insight");
        Assert.Equal("[12 - 17]", Line(insight, "Level 17 Meditation Aura").Range);
        Assert.Equal("[25 - 35]", Line(Made("Spirit"), "+35% Faster Cast Rate").Range);

        var enigma = Made("Enigma");
        Assert.Equal("[0.5 - 0.75]", Line(enigma, "+0.75 to Strength (Based on Character Level)").Range);

        // A fixed line stays bare.
        Assert.Equal("", Line(enigma, "+2 to All Skills").Range);
    }

    /// <summary>
    /// Ethereal and the sockets on one line, as the game prints them (a Thunderstroke in game
    /// beside ours): "Ethereal (Cannot be Repaired), Socketed (2)"; an ethereal item with no socket says the first
    /// half alone.
    /// </summary>
    [CorpusFact]
    public void Ethereal_and_the_sockets_are_one_line()
    {
        if (!game.Available)
            return;

        var spear = ItemForge.PlainBase("am5", 2, "103").Item;
        spear.IsEthereal = true;
        var said = ItemLines.Of(spear, ItemPresenter.Describe(spear), out _, out _).Select(line => line.Text).ToList();
        Assert.Contains("Ethereal (Cannot be Repaired), Socketed (2)", said);
        Assert.DoesNotContain(said, line => line == "Socketed (2)" || line == "Ethereal (Cannot be Repaired)");

        var bare = ItemForge.PlainBase("am5", 0, "103").Item;
        bare.IsEthereal = true;
        Assert.Contains("Ethereal (Cannot be Repaired)", ItemLines.Of(bare, ItemPresenter.Describe(bare), out _, out _).Select(line => line.Text));
    }

    /// <summary>
    /// "+3 to Fire Skills" is the grant and the stat that words it, and the game prints one line: Leaf said it twice
    /// (one line is kept).
    /// </summary>
    [CorpusFact]
    public void An_elemental_skill_is_one_line()
    {
        if (!game.Available)
            return;

        var leaf = ItemForge.RunewordShown(Runewords.All.First(word => Runewords.GameName(word) == "Leaf"), "").Item;
        var said = ItemLines.Of(leaf, ItemPresenter.Describe(leaf), out _, out _).Select(line => line.Text).ToList();
        Assert.Single(said, line => line == "+3 to Fire Skills");
    }
}
