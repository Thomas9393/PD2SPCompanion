using System.Text.Json;
using System.Text.Json.Nodes;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The game's Ctrl+C text of an item made back into the item (
/// <see cref="GameClipboard"/>). The fixtures are real: thirty
/// items the editor forged onto a character, each copied in game and pasted into two files, and the save they
/// were copied from - so every text has the item it must come back as (TestCorpus\clipboard).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class GameClipboardTests(Game game)
{
    private static string Fixture(string name) => Path.Combine(Game.Corpus, "clipboard", name);

    private static string Line(ItemStat stat) =>
        $"{stat.Id}={stat.Value}" + ((stat.SkillId ?? stat.Param) is { } subject ? $"[{subject}]" : "")
        + (stat.SkillLevel is { } level ? $"L{level}" : "") + (stat.MaxCharges is { } most ? $"/{most}" : "");

    /// <summary>Everything an item holds that the text could carry, as one string: two items alike read alike.</summary>
    private static string Told(Item item) =>
        string.Join(" ", new[]
        {
            item.Code.Trim(), item.Quality.ToString(), $"ilvl{item.ItemLevel}", item.IsEthereal ? "eth" : "",
            $"sock{item.TotalNumberOfSockets}", $"qty{item.Quantity}", $"def{item.Armor}",
            $"dur{item.Durability}/{item.MaxDurability}", $"row{item.FileIndex}", $"word{item.RunewordId}",
            $"auto{(item.IsAutoAffix ? item.AutoAffixId : 0)}",
            "in:" + string.Join(",", item.SocketedItems.Select(each => each.Code.Trim() + "{" + string.Join(" ", ItemStats.Every(each).Select(Line).Order(StringComparer.Ordinal)) + "}")),
            "stats:" + string.Join(" ", ItemStats.Every(item).Select(Line).Order(StringComparer.Ordinal)),
        });

    private (IReadOnlyList<Item> Real, List<Forged> Made)? Loaded()
    {
        if (!game.Available || !File.Exists(Fixture("specimens.d2s")))
            return null;

        var (save, _) = SaveVerifier.Load(Fixture("specimens.d2s"));
        var real = save.PlayerItemList.Items;
        string era = real.Select(item => item.Version).First(version => version is { Length: > 0 })!;

        var made = new List<Forged>();
        foreach (string file in new[] { "round1.txt", "round2.txt" })
            made.AddRange(GameClipboard.ReadAll(File.ReadAllText(Fixture(file)), era));

        return (real, made);
    }

    [CorpusFact]
    public void Every_copied_item_comes_back_as_the_item_it_was_copied_from()
    {
        if (Loaded() is not var (real, made))
            return;

        Assert.Equal(30, made.Count);

        var saved = real.Select(Told).ToHashSet();
        var apart = made.Where(each => !saved.Contains(Told(each.Item))).ToList();

        // One cannot: a magic map whose "of the Armorer" line the text has not - it shares "Monsters have" with
        // the prefix's - so the number is nowhere, and the import says it put the line back.
        var map = Assert.Single(apart);
        Assert.Equal("t27", map.Item.Code.Trim());
        Assert.Contains(map.Notes, note => note.Contains("not in the game's text", StringComparison.Ordinal));
        Assert.Contains(ItemStats.Every(map.Item), stat => stat.Stat == "map_mon_droparmor");
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Every_copied_item_made_is_one_the_game_could_have_made()
    {
        if (!game.Pinned("S13"))
            return;

        if (Loaded() is not var (_, made))
            return;

        foreach (var each in made)
            Assert.Empty(Legitimacy.Check(each.Item).Select(finding => $"{ItemPresenter.Describe(each.Item).Name}: {finding.Subject}"));
    }

    [CorpusFact]
    public void A_line_the_text_drops_for_sharing_its_name_is_put_back()
    {
        if (Loaded() is not var (real, made))
            return;

        // Blood Call, a rare: maximum damage per level (218) is under "to Maximum Damage" with stat 22, and the
        // text keeps the first. Without it the affix that wrote both is not found.
        var call = made.Single(each => ItemPresenter.Describe(each.Item).Name == "Blood Call").Item;
        var theirs = real.Single(item => ItemPresenter.Describe(item).Name == "Blood Call");

        Assert.Equal(ItemStats.Every(theirs).Single(stat => stat.Id == 218).Value, ItemStats.Every(call).Single(stat => stat.Id == 218).Value);
        Assert.Equal(theirs.MagicPrefixIds.Count(id => id > 0), call.MagicPrefixIds.Count(id => id > 0));
        Assert.Equal(theirs.MagicSuffixIds.Count(id => id > 0), call.MagicSuffixIds.Count(id => id > 0));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_pasted_number_stands_over_the_Forges_best()
    {
        if (Loaded() is not var (real, _))
            return;

        string era = real.Select(item => item.Version).First(version => version is { Length: > 0 })!;

        // Nagelring as it was copied, its magic find and attack rating taken down to the bottom of their ranges.
        var texts = new List<JsonObject>();
        var reader = new Utf8JsonReader(File.ReadAllBytes(Fixture("round1.txt")), new JsonReaderOptions { AllowMultipleValues = true });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                texts.Add((JsonObject)JsonNode.Parse(ref reader)!);
        }

        var ring = texts.Single(each => (string?)each["name"] == "Nagelring");
        foreach (var line in ring["stats"]!.AsArray())
        {
            if (line!["range"] is { } range)
                line["value"] = (int)range["min"]!;
        }

        var made = GameClipboard.Read(ring.ToJsonString(), era)!.Item;

        Assert.Equal(15, ItemStats.Every(made).Single(stat => stat.Id == 80).Value);
        Assert.Equal(50, ItemStats.Every(made).Single(stat => stat.Id == 19).Value);
        // What the text gave no range for is as it was: the corruption's strength, and its id beside it.
        Assert.Equal(7, ItemStats.Every(made).Single(stat => stat.Id == 0).Value);
        Assert.Contains(ItemStats.Every(made), stat => stat.Stat == Corruptions.IdStat);
        Assert.Contains(ItemStats.Every(made), stat => stat.Stat == Corruptions.FlagStat);
        Assert.Empty(Legitimacy.Check(made));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void The_Forges_boxes_say_what_a_pasted_item_is()
    {
        if (Loaded() is not var (_, made))
            return;

        var search = new ForgeSearch();
        search.Load();

        foreach (var each in made)
        {
            var item = each.Item;
            string name = ItemPresenter.Describe(item).Name;

            Assert.True(search.Show(item), name);
            Assert.NotNull(search.What);
            Assert.Equal(item.Code.Trim(), search.Base.Code);

            // What the boxes name, made, is the same kind of thing on the same base.
            var named = ForgeSearch.Make(search.What!, item.Version);
            Assert.Equal(item.Code.Trim(), named.Code.Trim());
            Assert.Equal(item.IsRuneword, named.IsRuneword);
            if (!item.IsRuneword)
                Assert.Equal(item.Quality, named.Quality);
            if (item.Quality is ItemQuality.Unique or ItemQuality.Set)
                Assert.Equal(item.FileIndex, named.FileIndex);
        }
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_copied_item_is_named_as_the_game_shows_it_without_being_made()
    {
        if (Loaded() is not var (_, made))
            return;

        // The paste button says what it would paste: each text's name is its item's.
        var names = new List<string?>();
        foreach (string file in new[] { "round1.txt", "round2.txt" })
        {
            var reader = new Utf8JsonReader(File.ReadAllBytes(Fixture(file)), new JsonReaderOptions { AllowMultipleValues = true });
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                    names.Add(GameClipboard.NameOf(JsonNode.Parse(ref reader)!.ToJsonString()));
            }
        }

        Assert.Equal(made.Select(each => ItemPresenter.Describe(each.Item).Name), names);
        // The text names a unique by its row's index, the game by its string: the Torch of Iro is "Iros Torch" there.
        Assert.Contains("Torch of Iro", names);
        Assert.Null(GameClipboard.NameOf("Harlequin Crest"));
        Assert.Null(GameClipboard.NameOf(null));
    }

    [CorpusTheory]
    [InlineData("")]
    [InlineData("Harlequin Crest")]
    [InlineData("{ \"name\": \"not an item\" }")]
    [InlineData("{ \"quality\": \"Unique\", \"type\": \"Ring\", \"name\": ")]
    public void A_text_that_is_no_copied_item_makes_nothing(string text)
    {
        if (!game.Available)
            return;

        Assert.Empty(GameClipboard.ReadAll(text, "103"));
        Assert.Null(GameClipboard.Read(text, "103"));
    }

    /// <summary>
    /// The way out: an item written as text is the game's shape with a key of the
    /// editor's own that holds the whole item - read back, it is the item, durability and all, which the
    /// game's own text cannot say. Every item of the specimens' character, the worn and the socketed too.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void An_item_written_out_is_read_back_whole()
    {
        if (Loaded() is not var (real, _) || !PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out string season, out _))
            return;

        string era = real.Select(item => item.Version).First(version => version is { Length: > 0 })!;
        int written = 0;

        foreach (var item in real)
        {
            string? text = GameClipboard.Write(item, 10, season, out string? refused);
            Assert.True(text is not null, $"{ItemPresenter.Describe(item).Name}: {refused}");

            // The game's shape is there for whoever reads it: it looks like a copied item, and names it.
            Assert.True(GameClipboard.LooksLike(text));
            Assert.Equal(ItemPresenter.Describe(item).Name, GameClipboard.NameOf(text));

            var back = GameClipboard.Read(text!, era);
            Assert.NotNull(back);
            Assert.True(back!.Sound);
            Assert.Empty(back.Notes);
            Assert.Equal(Told(item), Told(back.Item));
            written++;
        }

        Assert.True(written > 20, $"only {written} items written");
    }

    /// <summary>
    /// The editor's key is its season's: another season's is passed by, and the item is made from its lines as
    /// one the game copied is - a likeness, not the bytes of another encoding read as this one's.
    /// </summary>
    [CorpusFact]
    public void Another_seasons_key_is_passed_by()
    {
        if (Loaded() is not var (real, _) || !PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out string season, out _))
            return;

        string era = real.Select(item => item.Version).First(version => version is { Length: > 0 })!;
        var unique = real.First(item => item.Quality == ItemQuality.Unique && !item.IsRuneword);

        var text = JsonNode.Parse(GameClipboard.Write(unique, 10, season, out _)!)!.AsObject();
        text[GameClipboard.OwnKey]!["season"] = "S00";
        text[GameClipboard.OwnKey]!["item"] = "AAAA";

        var back = GameClipboard.Read(text.ToJsonString(), era);
        Assert.NotNull(back);
        Assert.Equal(unique.Code.Trim(), back!.Item.Code.Trim());
        Assert.Equal(ItemPresenter.Describe(unique).Name, ItemPresenter.Describe(back.Item).Name);

        // And a key that does not read, this season's: the lines again, not an error.
        text[GameClipboard.OwnKey]!["season"] = season;
        text[GameClipboard.OwnKey]!["item"] = "not base64 at all";
        Assert.NotNull(GameClipboard.Read(text.ToJsonString(), era));
    }
}
