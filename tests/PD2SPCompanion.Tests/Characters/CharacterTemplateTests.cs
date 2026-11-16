namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// A new character is built on the editor's own template, a character made in game and taken into a game once
/// (CharacterTemplate). It was one of the player's own, and what is not cleared came with it - their summons, in
/// PD2's sections after the golem block -
/// and with no character in the save folder there was nothing to build on at all.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CharacterTemplateTests(Game game)
{
    [CorpusFact]
    public void A_new_character_is_built_on_the_editors_template_and_carries_nothing_of_anyone()
    {
        if (!game.Available)
            return;

        string? template = CharacterTemplate.Fresh();
        Assert.NotNull(template);

        // PD2's sections as a character fresh from the game holds them - its summons' empty - with the seconds it
        // was played zeroed (`st`, the statistics: NewCharacter.Begin).
        var (fresh, _) = SaveVerifier.Load(template!);
        int stats = Convert.ToHexString(fresh.Trailer).IndexOf("7374", StringComparison.Ordinal) / 2;
        Assert.True(stats > 0, "no statistics block in the template's trailer");
        Assert.Contains("737500", Convert.ToHexString(fresh.Trailer), StringComparison.Ordinal);

        string scratch = Path.Combine(Path.GetTempPath(), "pd2spc-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            using var tables = GameData.Open(Game.Root, Game.Tables);
            foreach (byte classId in new byte[] { 2, 5 })
            {
                byte[] made = CharacterCreation.Build(tables, template!, "Newcomer", classId, 30, hardcore: classId == 5, questsDone: false);
                string path = Path.Combine(scratch, $"Newcomer{classId}.d2s");
                File.WriteAllBytes(path, made);

                var (built, result) = SaveVerifier.Load(path);
                Assert.Equal(SaveFormat.Pd2, result.Format);
                Assert.Equal(("Newcomer", classId, (byte)30, classId == 5), (built.Name, built.ClassId, built.Level, built.Status.IsHardcore));
                Assert.Empty(built.PlayerItemList.Items);
                Assert.Equal(0u, built.Mercenary.Id);

                var expected = (byte[])fresh.Trailer.Clone();
                Array.Clear(expected, stats + 2, 62);
                Assert.Equal(expected, built.Trailer);
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [CorpusFact]
    public void An_armory_character_with_a_mercenary_is_built_on_the_template_that_has_none()
    {
        if (!game.Available)
            return;

        // The template never hired, so its file holds no list of what a mercenary wears - and a ladder
        // character with a mercenary must still be written on it.
        string? template = CharacterTemplate.Fresh();
        Assert.NotNull(template);
        string json = Path.Combine(Game.Corpus, "aliases", "assassin-98-1--ladder-softcore.json");
        if (!File.Exists(json))
            return;

        string scratch = Path.Combine(Path.GetTempPath(), "pd2spc-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            using var tables = GameData.Open(Game.Root, Game.Tables);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
            var armory = new ArmoryImport.Armory(document.RootElement.Clone());
            Assert.NotEmpty(armory.MercenaryItems);

            var (bytes, _) = ArmoryImport.Build(tables, template!, armory, "Importer");
            string path = Path.Combine(scratch, "Importer.d2s");
            File.WriteAllBytes(path, bytes);

            var (built, result) = SaveVerifier.Load(path);
            Assert.Equal(SaveFormat.Pd2, result.Format);
            Assert.NotEqual(0u, built.Mercenary.Id);
            Assert.Equal(armory.MercenaryItems.Count, built.MercenaryItemList.ItemList.Items.Count);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
