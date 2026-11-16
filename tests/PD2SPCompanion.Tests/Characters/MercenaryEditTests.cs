using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// The mercenary, edited within its type (MercenaryEdit: level, name and kind within the
/// type - changing an A1 mercenary to an A5 is overkill and error prone). On the corpus's own character, whose
/// mercenary is an Ascendant, Dark, hired in Hell, level 86.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MercenaryEditTests(Game game)
{
    private static D2S Own()
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        return save;
    }

    [CorpusFact]
    public void The_variants_offered_are_its_own_type_s_each_once_and_no_other()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Own();
        var variants = MercenaryEdit.VariantsOf(save, data);

        // An Ascendant: Dark and Light. The table has each three times - hired in Normal, Nightmare, Hell,
        // ids 30 to 35 - and they are the same mercenary at the same level, so each is offered once, by its
        // name and skill, with the id of the one hired in Normal.
        Assert.Equal([30, 31], variants.Select(variant => variant.TypeId).Order());
        Assert.Equal(["Dark (Amplify Damage)", "Light (Sanctuary)"], variants.Select(variant => variant.Label).Order());

        // The corpus's own is the Dark one hired in Hell, id 34: it is the first of the two all the same.
        Assert.Equal(34, save.Mercenary.TypeId);
        Assert.Equal("Dark (Amplify Damage)", variants[MercenaryEdit.VariantIndex(save, data)].Label);

        // Its lines: what it is called by is not the difficulty it was hired in.
        var profile = MercenaryProfile.Read(save, data);
        Assert.Equal("Dark (Amplify Damage)", profile.VariantText);
        Assert.Equal("Level 86 Ascendant", profile.LevelLine);

        // Dead, the line is the same: a badge says it, as a dead character's does.
        save.Mercenary.IsDead = 1;
        Assert.True(MercenaryProfile.Read(save, data).IsDead);
        Assert.Equal("Level 86 Ascendant", MercenaryProfile.Read(save, data).LevelLine);
        save.Mercenary.IsDead = 0;

        // A Barbarian's row is not among them, and is refused: another type.
        string? refused = MercenaryEdit.SetVariant(save, data, 28);
        Assert.NotNull(refused);
        Assert.Equal(34, save.Mercenary.TypeId);
    }

    [CorpusFact]
    public void Another_variant_keeps_the_level_in_its_own_experience()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Own();
        int level = MercenaryProfile.Read(save, data).Level;
        ushort name = save.Mercenary.NameId;
        Assert.True(level > 1);

        // Light, hired in Normal: 100 experience to the level's unit where Hell's row has 120.
        Assert.Null(MercenaryEdit.SetVariant(save, data, 31));

        var after = MercenaryProfile.Read(save, data);
        Assert.Equal("Light", after.Variant);
        Assert.Equal("Normal", after.Difficulty);
        Assert.Equal(1, MercenaryEdit.VariantIndex(save, data));
        Assert.Equal(level, after.Level);
        Assert.Equal((uint)MercenaryProfile.NeededFor(after.ExperiencePerLevel, level), save.Mercenary.Experience);
        Assert.Equal(name, save.Mercenary.NameId);
    }

    [CorpusTheory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(98)]
    public void A_level_set_is_the_level_read(int level)
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Own();

        Assert.Null(MercenaryEdit.SetLevel(save, data, level));
        Assert.Equal(level, MercenaryProfile.Read(save, data).Level);
    }

    [CorpusFact]
    public void A_level_is_held_to_what_there_is()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Own();

        Assert.Null(MercenaryEdit.SetLevel(save, data, 150));
        Assert.Equal(MercenaryEdit.MostLevel, MercenaryProfile.Read(save, data).Level);
        Assert.Null(MercenaryEdit.SetLevel(save, data, 0));
        Assert.Equal(1, MercenaryProfile.Read(save, data).Level);
    }

    [CorpusFact]
    public void Its_name_is_one_of_its_type_s_list_and_another_can_be_given()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Own();
        var names = MercenaryEdit.NamesOf(save, data);

        // The Ascendant's keys run MercX401 to MercX420: twenty names, each with words of its own.
        Assert.Equal(20, names.Count);
        Assert.All(names, name => Assert.False(name.StartsWith("MercX", StringComparison.OrdinalIgnoreCase), name));
        Assert.Equal(names[save.Mercenary.NameId], MercenaryEdit.NameOf(save, data));

        Assert.Null(MercenaryEdit.SetName(save, data, 3));
        Assert.Equal(names[3], MercenaryEdit.NameOf(save, data));

        Assert.NotNull(MercenaryEdit.SetName(save, data, 20));
        Assert.NotNull(MercenaryEdit.SetName(save, data, -1));
        Assert.Equal(3, save.Mercenary.NameId);
    }

    [CorpusFact]
    public void A_dead_one_is_revived_and_a_live_one_is_not()
    {
        var save = Own();

        Assert.NotNull(MercenaryEdit.Revive(save));

        save.Mercenary.IsDead = 1;
        Assert.Null(MercenaryEdit.Revive(save));
        Assert.Equal(0, save.Mercenary.IsDead);
    }

    [CorpusFact]
    public void With_none_hired_nothing_is_offered_and_nothing_is_done()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Own();
        save.Mercenary.Id = 0;

        Assert.Empty(MercenaryEdit.VariantsOf(save, data));
        Assert.Empty(MercenaryEdit.NamesOf(save, data));
        Assert.NotNull(MercenaryEdit.SetLevel(save, data, 10));
        Assert.NotNull(MercenaryEdit.SetName(save, data, 0));
        Assert.NotNull(MercenaryEdit.SetVariant(save, data, 30));
        Assert.NotNull(MercenaryEdit.Revive(save));
    }

    [CorpusFact]
    public void An_edited_mercenary_is_written_and_read_back()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Own();
        Assert.Null(MercenaryEdit.SetVariant(save, data, 33));
        Assert.Null(MercenaryEdit.SetLevel(save, data, 70));
        Assert.Null(MercenaryEdit.SetName(save, data, 5));

        var again = D2SLib.Core.ReadD2S(D2SLib.Core.WriteD2S(save));
        var profile = MercenaryProfile.Read(again, data);

        Assert.Equal(33, again.Mercenary.TypeId);
        Assert.Equal(5, again.Mercenary.NameId);
        Assert.Equal(70, profile.Level);
        Assert.Equal("Nightmare", profile.Difficulty);
    }
}
