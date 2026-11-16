using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;

using PD2SPCompanion.Core.Diagnostics;

using Xunit;
using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The search's text box finds any text of an item (as the game's stash search does):
/// the phrase as typed, in any line of the tooltip, and a pattern where what is typed is one. Asked of a
/// real character's items, and checked against the lines the vault kept for each.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultTextSearchTests(Game game, ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-text-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string File => Path.Combine(_folder, "text.pd2lib");

    private Vault Open()
    {
        Directory.CreateDirectory(_folder);
        return Vault.Open(File, "S13", 10);
    }

    private static void Store(Vault vault)
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        foreach (var item in save.PlayerItemList!.Items)
            vault.Add(item, null, out _);
    }

    /// <summary>The lines the vault keeps of each item, read off the file itself.</summary>
    private Dictionary<long, string> Texts()
    {
        var texts = new Dictionary<long, string>();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = File, Pooling = false }.ToString());
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT item_key, text FROM item_meta;";
        using var row = read.ExecuteReader();
        while (row.Read())
            texts[row.GetInt64(0)] = row.GetString(1);

        return texts;
    }

    private static HashSet<long> Found(Vault vault, string typed) =>
        [.. vault.Page(new VaultQuery(Name: typed), "Stored", descending: false, 0, int.MaxValue).Select(row => row.Key)];

    private static HashSet<long> With(Dictionary<long, string> texts, Func<string, bool> has) =>
        [.. texts.Where(pair => has(pair.Value)).Select(pair => pair.Key)];

    [CorpusFact]
    public void A_phrase_finds_the_items_whose_name_base_or_stats_say_it()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        Store(vault);
        var texts = Texts();
        Assert.NotEmpty(texts);

        // A stat, not the name: what the box could not find before.
        var damage = With(texts, text => text.Contains("Enhanced Damage", StringComparison.Ordinal));
        Assert.NotEmpty(damage);
        Assert.NotEqual(texts.Count, damage.Count);
        Assert.Equal(damage, Found(vault, "Enhanced Damage"));
        Assert.Equal(damage, Found(vault, "enhanced damage"));

        // Every line kept of every item finds its item, and only the items that say it.
        int asked = 0;
        foreach (string line in texts.Values.SelectMany(text => text.Split('\n')).Distinct().Take(120))
        {
            var say = With(texts, text => text.Contains(line, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(say, Found(vault, line));
            asked++;
        }

        // "attack speed" is what gives it - two Shael runes in a spear, summed into its lines as in game -
        // and not every weapon, each of which has a base attack speed on its tooltip.
        var fast = Found(vault, "attack speed");
        Assert.NotEmpty(fast);
        Assert.All(fast, key => Assert.Contains("Increased Attack Speed", texts[key], StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(texts.Values, text => text.Contains("Base Attack Speed", StringComparison.Ordinal));

        // What every item of a kind says is not asked: what it requires, its levels, its durability, its damage.
        Assert.Empty(Found(vault, "Required Level"));
        Assert.Empty(Found(vault, "Item Level"));
        Assert.Empty(Found(vault, "Durability:"));
        Assert.Empty(Found(vault, "Two-Hand Damage:"));

        // The class a base is for is a requirement; a skill line that names a class is a stat, and stays.
        Assert.DoesNotContain(texts.Values.SelectMany(text => text.Split('\n')), line => line == "(Amazon Only)");
        Assert.NotEmpty(Found(vault, "(Amazon Only)"));

        // The base is: a spear by its base's name, whatever it is called.
        Assert.NotEmpty(Found(vault, "Matriarchal Spear"));
        Assert.NotEmpty(Found(vault, "Stone Barb"));

        // And what is in a socket finds the item that holds it: the spear's skull and its jewel.
        var spear = Found(vault, "Stone Barb");
        Assert.Subset(Found(vault, "Perfect Skull"), spear);
        Assert.Subset(Found(vault, "Ruby Jewel of Envy"), spear);
        Assert.Subset(Found(vault, "Shael Rune"), spear);

        // A property row asks the same sums the lines say: the spear's attack speed is its two Shaels' 40,
        // none of it the spear's own, and its enhanced damage 562 with its jewel's 38.
        HashSet<long> Having(string key, double least) =>
            [.. vault.Page(new VaultQuery(Properties: [new PropertyWant(key, Min: least)]), "Stored", descending: false, 0, int.MaxValue)
                     .Select(row => row.Key)];

        Assert.Subset(Having("item_fasterattackrate", 40), spear);
        Assert.Subset(Having("item_maxdamage_percent", 562), spear);
        Assert.Empty(Having("item_maxdamage_percent", 563));
        var numbers = vault.ValuesOf(spear, [new PropertyWant("item_fasterattackrate"), new PropertyWant("item_maxdamage_percent")]);
        Assert.Equal([40d, 562d], numbers[spear.Single()].Select(number => number ?? 0));

        // And by its jewel's own number: the Ruby Jewel of Envy in it is 38% enhanced damage, the spear 562
        // with it. Asked for 38 and no more, the spear is found, and the list says the 38 it was found by.
        HashSet<long> Between(string key, double least, double most) =>
            [.. vault.Page(new VaultQuery(Properties: [new PropertyWant(key, Min: least, Max: most)]), "Stored", descending: false, 0, int.MaxValue)
                     .Select(row => row.Key)];

        Assert.Subset(Between("item_maxdamage_percent", 38, 38), spear);
        Assert.DoesNotContain(spear.Single(), Between("item_maxdamage_percent", 39, 500));
        var by = vault.ValuesOf(spear, [new PropertyWant("item_maxdamage_percent", Min: 38, Max: 38), new PropertyWant("item_maxdamage_percent", Min: 100)]);
        Assert.Equal([38d, 562d], by[spear.Single()].Select(number => number ?? 0));

        // A rune's share is not a number of its own: 20 and no more finds nothing by the Shaels.
        Assert.DoesNotContain(spear.Single(), Between("item_fasterattackrate", 20, 20));

        // The phrase as typed, within one line: two stats of two lines find nothing.
        Assert.Empty(Found(vault, "Enhanced Damage to Attack Rating"));

        output.WriteLine($"{texts.Count} items, {asked} lines asked; \"Enhanced Damage\" on {damage.Count}, \"attack speed\" on {fast.Count}.");
        output.WriteLine(texts.Values.OrderByDescending(text => text.Length).First());
    }

    [CorpusFact]
    public void A_pattern_finds_what_it_describes_and_a_phrase_stays_a_phrase()
    {
        if (!game.Available)
            return;

        using var vault = Open();
        Store(vault);
        var texts = Texts();

        var damage = With(texts, text => text.Contains("% Enhanced Damage", StringComparison.Ordinal));
        var rating = With(texts, text => text.Contains(" to Attack Rating", StringComparison.Ordinal));
        Assert.NotEmpty(damage);
        Assert.NotEmpty(rating);

        // A whole line, by its ends; either of two; a number's shape.
        var whole = With(texts, text => text.Split('\n').Any(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\+\d+% Enhanced Damage$")));
        Assert.NotEmpty(whole);
        Assert.Equal(whole, Found(vault, @"^\+\d+% enhanced damage$"));
        Assert.Equal([.. damage.Union(rating)], Found(vault, "% enhanced damage| to attack rating"));

        // A dot stays within a line: nothing runs from one line into the next.
        Assert.Empty(Found(vault, "enhanced damage.*attack rating"));
        Assert.Empty(Found(vault, "attack rating.*enhanced damage"));

        // No pattern: nothing it is made of, half typed, or one that would find every item.
        Assert.Null(SearchPattern.Of("attack speed"));
        Assert.Null(SearchPattern.Of("speed ("));
        Assert.Null(SearchPattern.Of("+2 to"));
        Assert.Null(SearchPattern.Of("speed|"));
        Assert.Null(SearchPattern.Of("x*"));
        Assert.NotNull(SearchPattern.Of("speed|rate"));

        // Typed with a sign that is no pattern, the phrase is looked for as it is.
        var plus = With(texts, text => text.Contains("+1", StringComparison.Ordinal));
        Assert.NotEmpty(plus);
        Assert.Equal(plus, Found(vault, "+1"));

        // Half a pattern finds nothing rather than everything, and does not fail.
        Assert.Empty(Found(vault, "speed ("));
        Assert.NotEqual(texts.Count, Found(vault, "speed|").Count);
    }

    [CorpusFact]
    public void An_item_that_will_not_read_gets_no_row_is_logged_and_the_rest_go_on()
    {
        if (!game.Available)
            return;

        using (var vault = Open())
            Store(vault);

        int held = Texts().Count;
        Assert.True(held > 1, "the specimen holds too little to tell");

        long bad;
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = File, Pooling = false }.ToString()))
        {
            db.Open();
            using var write = db.CreateCommand();
            write.CommandText = "SELECT MIN(key) FROM item;";
            bad = Convert.ToInt64(write.ExecuteScalar());
            write.CommandText = $"UPDATE item SET blob = x'00' WHERE key = {bad}; UPDATE vault SET projection_version = 8 WHERE id = 1;";
            write.ExecuteNonQuery();
        }

        string log = Path.Combine(_folder, "reproject.log");
        Log.Open(log, "reproject");
        try
        {
            using var vault = Open();
            Assert.Equal(held, vault.Count());
        }
        finally
        {
            Log.Close();
        }

        var texts = Texts();
        Assert.Equal(held - 1, texts.Count);
        Assert.DoesNotContain(bad, texts.Keys);

        string said = System.IO.File.ReadAllText(log);
        Assert.Contains($"Item {bad} left out of the search", said);
        Assert.Contains($"1 of {held} items left out of the search.", said);
    }

    [CorpusFact]
    public void A_vault_of_before_is_given_its_lines_and_one_in_another_language_its_own_again()
    {
        if (!game.Available)
            return;

        using (var vault = Open())
            Store(vault);

        var before = Texts();

        void Run(string sql)
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = File, Pooling = false }.ToString());
            db.Open();
            using var write = db.CreateCommand();
            write.CommandText = sql;
            write.ExecuteNonQuery();
        }

        // As an editor of before left it: the derived tables of version 8, no lines.
        Run("UPDATE vault SET projection_version = 8 WHERE id = 1; UPDATE item_meta SET text = ''; DROP TABLE item_meta_language;");
        using (var vault = Open())
            Assert.NotEmpty(Found(vault, "Enhanced Damage"));
        Assert.Equal(before, Texts());

        // Made in another language: the lines are written again in this one.
        Run("UPDATE item_meta_language SET code = 'xx-XX'; UPDATE item_meta SET text = 'other words';");
        using (var vault = Open())
            Assert.NotEmpty(Found(vault, "Enhanced Damage"));
        Assert.Equal(before, Texts());
    }
}
