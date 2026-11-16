using System.Text.Json;
using System.Text.Json.Nodes;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What comes from outside the editor - the trade site's answers, the armory's, a copied text - when it is not what the
/// row-by-row rebuild expects: a worded refusal or an item left out, never a crash and
/// never another item under its name.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class TradeUntrustedTests(Game game)
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static T WithStamps<T>(Func<T> run)
    {
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps("105", "103");
        try { return run(); }
        finally { SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp); }
    }

    // ---- a malformed answer -----------------------------------------------------------------------------------

    [CorpusTheory]
    [InlineData("[]")]
    [InlineData("12")]
    [InlineData("\"text\"")]
    [InlineData("not json")]
    public void A_search_answer_of_no_shape_is_refused_in_words(string body)
    {
        var refused = Assert.Throws<TradeGate.TradeGateException>(() => TradeSearch.ListingsIn(body));
        Assert.Contains("unreadable", refused.Message);
    }

    [CorpusFact]
    public void A_record_that_is_no_listing_is_passed_over()
    {
        var found = TradeSearch.ListingsIn("""
            { "data": [ 1, null, [], { "_id": 123, "item": {} }, { "_id": "noitem" }, { "_id": "baditem", "item": 3 },
                        { "_id": "good", "item": { "base_code": "cap" } } ] }
            """);

        Assert.Equal("good", Assert.Single(found).Id);
    }

    [CorpusTheory]
    [InlineData("""{ "data": 3 }""")]
    [InlineData("""{ "data": [] }""")]
    public void A_search_answer_with_no_records_finds_nothing(string body) =>
        Assert.Empty(TradeSearch.ListingsIn(body));

    [CorpusTheory]
    [InlineData("[]")]
    [InlineData("3")]
    [InlineData("""{ "data": [ 1 ] }""")]
    [InlineData("""{ "data": [ { "item": 1 } ] }""")]
    public void A_listing_answer_of_no_shape_is_none(string body) =>
        Assert.Null(TradeListings.FirstIn(body));

    [CorpusTheory]
    [InlineData("null")]
    [InlineData("\"abc\"")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999999")]
    public void A_listing_with_any_id_builds_for_a_save(string id)
    {
        if (!game.Available)
            return;

        var made = TradeItems.Build(Json($$"""
            { "base_code": "cap", "name": "Cap", "quality": { "id": 2 }, "item_level": 5, "is_identified": true, "id": {{id}} }
            """), Game.Stamp);

        Assert.Equal("cap", made.Item.Code.Trim());
    }

    [CorpusTheory]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[1]")]
    [InlineData("[1, \"y\"]")]
    [InlineData("[1, 2.5]")]
    [InlineData("[1, null]")]
    public void A_runeword_listings_damage_line_of_any_shape_builds(string values)
    {
        if (!game.Available)
            return;

        var spirit = ItemForge.Runeword(Runewords.All.First(word => word.Name == "Spirit"), "crs", Game.Stamp).Item;
        string holes = string.Join(",", spirit.SocketedItems.Select(rune => $$"""{ "base_code": "{{rune.Code.Trim()}}", "is_simple": true }"""));

        var made = TradeItems.Build(Json($$"""
            { "base_code": "crs", "name": "Spirit", "is_runeword": true, "quality": { "id": 2 }, "socket_count": 4, "item_level": 50,
              "is_identified": true, "socketed": [ {{holes}} ], "modifiers": [ { "name": "maxdamage_percent", "values": {{values}} } ] }
            """), Game.Stamp);

        Assert.True(made.Item.IsRuneword);
        Assert.Null(TradeItems.NoRow(made));
    }

    [CorpusTheory]
    [InlineData("[]")]
    [InlineData("3")]
    [InlineData("""{ "character": 3 }""")]
    [InlineData("not json")]
    public void An_armory_answer_of_no_shape_is_refused_in_words(string body) =>
        Assert.Throws<ArmoryImport.ArmoryException>(() => ArmoryImport.ArmoryIn(body, "Someone"));

    // ---- a runeword no row has ----------------------------------------------------------------------------------

    private const string NoSuchRuneword = """
        { "base_code": "crs", "name": "Bygone Word", "is_runeword": true, "quality": { "id": 2 }, "item_level": 50, "is_identified": true }
        """;

    [CorpusFact]
    public void A_runeword_no_row_matches_says_so_as_a_unique_does()
    {
        if (!game.Available)
            return;

        var made = TradeItems.Build(Json(NoSuchRuneword), Game.Stamp);

        Assert.False(made.Sound);
        Assert.NotNull(TradeItems.NoRow(made));
    }

    [CorpusFact]
    public void An_armory_character_with_a_runeword_no_row_matches_comes_in_without_it()
    {
        if (!game.Available)
            return;

        string file = Path.Combine(Game.Corpus, "aliases", "ladder-1--ladder-s14-softcore.json");
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        root["items"]!.AsArray().Add(JsonNode.Parse(NoSuchRuneword));
        var armory = new ArmoryImport.Armory(JsonSerializer.SerializeToElement(root));

        using var tables = GameData.Open(Game.Root, Game.Tables);
        var (bytes, notes) = ArmoryImport.Build(tables, Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"), armory, "Importer");

        Assert.NotEmpty(bytes);
        Assert.Contains(notes, note => note.Contains(TradeItems.NotThisSeason, StringComparison.Ordinal) && note.Contains("runeword", StringComparison.Ordinal));
    }

    // ---- a unique no row has --------------------------------------------------------------------------------

    /// <summary>A base one unique row lives on, and that row.</summary>
    private static GrailCatalog.Entry OnlyUniqueOfItsBase() =>
        GrailCatalog.Uniques().GroupBy(entry => entry.Item.Code?.Trim() ?? "").First(group => group.Count() == 1 && group.Key.Length > 0).Single();

    /// <summary>A base several unique rows live on, by the name the game's text calls it.</summary>
    private static string BaseOfSeveralUniques() =>
        ItemPresenter.BaseName(GrailCatalog.Uniques().GroupBy(entry => entry.Item.Code?.Trim() ?? "").First(group => group.Count() > 1 && group.Key.Length > 0).Key);

    [CorpusFact]
    public void A_pasted_unique_with_no_row_says_so_where_its_base_has_several()
    {
        if (!game.Available)
            return;

        var made = GameClipboard.Read($$"""{ "quality": "Unique", "name": "Bygone Wonder", "type": "{{BaseOfSeveralUniques()}}", "iLevel": 50 }""", Game.Stamp);

        Assert.NotNull(made);
        Assert.False(made!.Sound);
        Assert.NotNull(TradeItems.NoRow(made));
    }

    [CorpusFact]
    public void A_socketed_jewel_with_no_row_stays_out_of_its_socket()
    {
        if (!game.Available)
            return;

        var made = GameClipboard.Read("""
            { "quality": "Normal", "type": "Cap", "iLevel": 50, "sockets": 1,
              "socketed": [ { "quality": "Unique", "name": "Bygone Facet", "type": "Jewel", "iLevel": 50 } ] }
            """, Game.Stamp);

        Assert.NotNull(made);
        Assert.False(made!.Sound);
        Assert.Empty(made.Item.SocketedItems);
        Assert.Null(TradeItems.NoRow(made));
    }

    [CorpusFact]
    public void A_listing_taken_by_its_base_says_so_and_is_not_whole_when_its_lines_are_not_that_rows()
    {
        if (!game.Available)
            return;

        var row = OnlyUniqueOfItsBase();
        var made = TradeItems.Build(Json($$"""
            { "base_code": "{{row.Item.Code!.Trim()}}", "name": "Bygone Wonder", "quality": { "id": {{(int)ItemQuality.Unique}} }, "item_level": 90, "is_identified": true,
              "modifiers": [ { "name": "strength", "values": [ 500 ] } ] }
            """), Game.Stamp);

        Assert.Equal((uint)row.Row, made.Item.FileIndex);
        Assert.Contains(made.Notes, note => note.StartsWith(TradeItems.TakenByBase, StringComparison.Ordinal)
                                            && note.Contains("Bygone Wonder", StringComparison.Ordinal)
                                            && note.Contains(GrailCatalog.ShownName(row), StringComparison.Ordinal));
        Assert.False(made.Sound);
    }

    [CorpusFact]
    public void A_unique_found_by_its_name_is_not_taken_by_its_base()
    {
        if (!game.Available)
            return;

        var row = OnlyUniqueOfItsBase();
        var made = TradeItems.Build(Json($$"""
            { "base_code": "{{row.Item.Code!.Trim()}}", "name": {{JsonSerializer.Serialize(GrailCatalog.ShownName(row))}}, "quality": { "id": {{(int)ItemQuality.Unique}} }, "item_level": 90, "is_identified": true }
            """), Game.Stamp);

        Assert.DoesNotContain(made.Notes, note => note.StartsWith(TradeItems.TakenByBase, StringComparison.Ordinal));
    }

    // ---- the editor's own text ------------------------------------------------------------------------------

    [CorpusFact]
    public void A_copied_legacy_item_is_fitted_when_pasted_not_only_stamped()
    {
        if (!game.Available || Game.Season != "S14")
            return;

        WithStamps(() =>
        {
            var word = ItemForge.Runeword(Runewords.All.First(each => each.Name == "Spirit"), "crs", "103").Item;
            uint right = word.RunewordId;

            // Its season's string number: S14's strings moved the key by 130 (Migration.Fit).
            word.RunewordId = right - 130;
            string text = GameClipboard.Write(word, 10, Vault.InstalledSeason(), out string? refused) ?? throw new InvalidOperationException(refused);

            var made = GameClipboard.Read(text, "105");

            Assert.NotNull(made);
            Assert.Equal("105", made!.Item.Version);
            Assert.Equal(right, made.Item.RunewordId);
            Assert.False(ItemSeason.IsLegacy(made.Item));
            return 0;
        });
    }

    [CorpusFact]
    public void Another_seasons_copy_of_a_rare_keeps_its_skills_and_charges()
    {
        if (!game.Available)
            return;

        var wand = ItemForge.PlainBase("wnd", 0, Game.Stamp).Item;
        wand.Quality = ItemQuality.Magic;
        var home = ItemStats.Home(wand);
        home.Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("item_singleskill"), Stat = "item_singleskill", Param = 36, Value = 3 });
        home.Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("item_charged_skill"), Stat = "item_charged_skill", SkillId = 54, SkillLevel = 5, Value = 12, MaxCharges = 20 });
        home.Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("item_addskill_tab"), Stat = "item_addskill_tab", Param = 10, SkillTab = 2, CharacterClass = 1, Value = 2 });

        // Another season's key: the lines are what crosses.
        string text = GameClipboard.Write(wand, 10, "S1", out string? refused) ?? throw new InvalidOperationException(refused);
        var made = GameClipboard.Read(text, Game.Stamp);

        Assert.NotNull(made);
        var stats = ItemStats.Every(made!.Item).ToList();

        var single = stats.Single(stat => stat.Stat == "item_singleskill");
        Assert.Equal((36, 3), (single.Param, single.Value));

        var charged = stats.Single(stat => stat.Stat == "item_charged_skill");
        Assert.Equal((54, 5, 12, 20), (charged.SkillId, charged.SkillLevel, charged.Value, charged.MaxCharges));

        var tab = stats.Single(stat => stat.Stat == "item_addskill_tab");
        Assert.Equal((10, 2, 1, 2), (tab.Param, tab.SkillTab, tab.CharacterClass, tab.Value));
    }
}
