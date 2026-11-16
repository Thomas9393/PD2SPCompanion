using D2SLib.Model.Save;

using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// LastChecks's tooltips as screenshots of them read in game: the auto prefixes in the bracket of a
/// weapon or a quiver and not of armor (<see cref="MagicAffixes.InBracketOf"/>), and a blunt weapon's 50% against the undead.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class GreenBracketTests(Game game, ITestOutputHelper output)
{
    private static IReadOnlyList<Item> Items() =>
        SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "LastChecks-all.d2s")).Save.PlayerItemList.Items;

    private static IReadOnlyList<ItemLine> LinesOf(Func<Item, bool> which)
    {
        var item = Items().First(which);
        return ItemLines.Of(item, ItemPresenter.Describe(item), out _, out _);
    }

    private static IReadOnlyList<ItemLine> Named(string name) =>
        LinesOf(item => ItemPresenter.Describe(item).Name.Contains(name, StringComparison.Ordinal));

    [CorpusTheory]
    [InlineData("Tricky Short Staff of the Maelstrom", "Faster Cast Rate", "10", "50")]
    [InlineData("Rift Bludgeon", "Faster Cast Rate", "10", "50")]
    [InlineData("Wraith Song", "Faster Cast Rate", "10", "50")]
    [InlineData("Wraith Song", "Enhanced Damage", "10", "65")]
    [InlineData("Lapis Sharp Arrows of Readiness", "Maximum Damage", "1", "18")]
    [InlineData("Cardinal Blunt Arrows of Defiance", "Maximum Damage", "1", "18")]
    [InlineData("Death Eye", "Maximum Damage", "1", "18")]
    [InlineData("Cardinal Blunt Arrows of Defiance", "Minimum Damage", "1", "14")]
    [InlineData("Jagged War Bolts of Accuracy", "Enemy Physical Resistance", "2", "6")]
    [InlineData("Deadly Light Bolts of Breaking", "Enemy Physical Resistance", "2", "6")]
    [InlineData("Grim Eye", "Enemy Physical Resistance", "2", "6")]
    [InlineData("Marshal's Spiked Shield of the Ox", "Attacker Takes Damage", "1", "400")]
    [InlineData("Holy Bone Shield of Deflecting", "Magic Damage Taken Reduced", "1", "16")]
    [InlineData("Platinum Cap of Restoration", "Attack Rating", "10", "120")]
    [InlineData("Saintly Battle Boots of Speed", "Faster Run/Walk", "10", "40")]
    public void The_bracket_reads_as_in_game(string name, string line, string least, string most)
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var lines = Named(name);
        foreach (var each in lines)
            output.WriteLine($"{each.Text} {each.Range}");

        var found = lines.First(each => each.Text.Contains(line, StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"[{least} - {most}]", found.Range);
    }

    [CorpusTheory]
    [InlineData("Death Eye", "Adds 90-146 fire damage", "[1 - 306 to 325 - 432]")]
    [InlineData("Grim Eye", "Adds 1-84 lightning damage", "[1 - 6 to 433 - 576]")]
    [InlineData("of the Glacier", "Adds 21-71 cold damage", "[1 - 118 to 226 - 348]")]
    [InlineData("of Pestilence", "+50 poison damage over 5 seconds", "")]
    public void An_added_elements_bracket_has_both_ends(string name, string line, string range)
    {
        if (!game.Available || Game.Tables is not null)
            return;

        Assert.Equal(range, Named(name).First(each => each.Text == line).Range);
    }

    [CorpusFact]
    public void A_white_item_prints_no_bracket()
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var shield = LinesOf(item => item.Code.Trim() == "spk" && item.Quality == ItemQuality.Normal);
        Assert.Equal("", shield.First(each => each.Text.Contains("Attacker Takes Damage", StringComparison.OrdinalIgnoreCase)).Range);
    }

    [CorpusTheory]
    [InlineData("Bonesnap", "+100% Damage to Undead", "")]
    [InlineData("Rusthandle", "+110% Damage to Undead", "[50 - 60]")]
    public void A_blunt_weapons_own_undead_line_takes_the_50(string name, string line, string range)
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var lines = Named(name);
        var found = Assert.Single(lines, each => each.Text.Contains("Damage to Undead", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(line, found.Text);
        Assert.Equal(range, found.Range);
    }

    [CorpusTheory]
    [InlineData("wnd")]
    [InlineData("wsp")]
    [InlineData("sst")]
    public void A_blunt_weapon_without_one_says_it_after_its_other_lines(string code)
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var lines = LinesOf(item => item.Code.Trim() == code);
        int at = lines.ToList().FindIndex(each => each.Text == "+50% Damage to Undead");
        Assert.True(at >= 0);
        Assert.DoesNotContain(lines.Skip(at + 1), each => each.Tone == ItemLine.Magic && !each.Text.StartsWith("Socketed", StringComparison.Ordinal));
    }

    [CorpusFact]
    public void A_sword_says_nothing_of_the_undead()
    {
        if (!game.Available || Game.Tables is not null)
            return;

        Assert.DoesNotContain(LinesOf(item => item.Code.Trim() == "clm"), each => each.Text.Contains("Undead", StringComparison.Ordinal));
    }

    [CorpusFact]
    public void A_gem_and_a_rune_in_a_worn_sword_read_as_in_game()
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var save = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "LastChecks-socketed.d2s")).Save;
        var sword = save.PlayerItemList.Items.First(item => item.Code.Trim() == "crs");
        var lines = ItemLines.Of(sword, ItemPresenter.Describe(sword), out _, out _);
        foreach (var each in lines)
            output.WriteLine(each.Text);

        var magic = lines.Where(each => each.Tone == ItemLine.Magic).Select(each => each.Text).ToList();
        Assert.Contains(lines, each => each.Text == "'Thul'");
        Assert.Equal(["Adds 15-20 fire damage", "Adds 3-14 cold damage", "Socketed (2)"], magic.TakeLast(3));
        Assert.Contains(lines, each => each.Text == "Required Level: 23");
        Assert.Contains(lines, each => each.Text == "Required Strength: 43");
    }

    [CorpusTheory]
    [InlineData("hax", ItemQuality.Normal, "Gemmed Hand Axe [1]", "One-Hand Damage: 3 to 6", "Adds 15-20 fire damage", "Required Level: 18")]
    [InlineData("ssd", ItemQuality.Normal, "Gemmed Short Sword [1]", "One-Hand Damage: 2 to 16", "+9 to Maximum Damage", "Required Level: 27")]
    [InlineData("crs", ItemQuality.Superior, "Crystal Sword [1]", "Durability: 50 of 50", "Adds 15-20 fire damage", "Required Level: 18")]
    public void What_is_in_a_socket_names_a_plain_item_only(string code, ItemQuality quality, string title, params string[] said)
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var save = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "HandsTest.d2s")).Save;
        var item = save.PlayerItemList.Items.First(each => each.Code.Trim() == code && each.Quality == quality && each.SocketedItems.Count > 0);
        var view = ItemPresenter.Describe(item);
        var lines = ItemLines.Of(item, view, out _, out _);
        foreach (var each in lines)
            output.WriteLine(each.Text);

        Assert.Equal(title, ItemLines.Title(item, view));
        foreach (string line in said)
            Assert.Contains(lines, each => each.Text == line);
    }

    [CorpusFact]
    public void An_empty_socket_does_not_name_it()
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var save = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "HandsTest.d2s")).Save;
        var item = save.PlayerItemList.Items.First(each => each.Code.Trim() == "crs" && each.Quality == ItemQuality.Normal);
        Assert.Equal("Crystal Sword [1]", ItemLines.Title(item, ItemPresenter.Describe(item)));
    }
}
