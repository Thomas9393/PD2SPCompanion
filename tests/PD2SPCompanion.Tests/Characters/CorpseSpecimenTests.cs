using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// The corpse list of a save, on a specimen made for it:
/// LightFury, a level 86 Amazon who died and left the game without taking her body up -
/// TestCorpus, specimens/corpse-twelve-worn-items-s13.d2s - and the same character a minute before the death,
/// corpse-before-the-death-s13.d2s. The parser reads the corpse as the file has it and writes it back byte for
/// byte; what the program then does with it is the Shell's to prove (Inventory/CorpseTests).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CorpseSpecimenTests(Game game)
{
    private static string Specimen(string name) => Path.Combine(Game.Corpus, "specimens", name);

    private const string Dead = "corpse-twelve-worn-items-s13.d2s", Before = "corpse-before-the-death-s13.d2s";

    [CorpusFact]
    public void The_death_left_one_corpse_holding_the_twelve_items_that_were_worn()
    {
        if (!game.Available || !File.Exists(Specimen(Dead)))
            return;

        var (before, _) = SaveVerifier.Load(Specimen(Before));
        var (dead, _) = SaveVerifier.Load(Specimen(Dead));

        // Before: twelve worn, no corpse.
        var worn = before.PlayerItemList.Items.Where(item => item.Mode == ItemMode.Equipped).ToList();
        Assert.Equal(12, worn.Count);
        Assert.Equal(0, before.PlayerCorpses.Count);

        // After: nothing worn in the character's own list, and one corpse with those twelve - each still saying
        // it is worn, and in which slot.
        Assert.DoesNotContain(dead.PlayerItemList.Items, item => item.Mode == ItemMode.Equipped);
        Assert.Equal(1, dead.PlayerCorpses.Count);
        var lying = Assert.Single(dead.PlayerCorpses.Corpses).ItemList.Items;
        Assert.Equal(12, lying.Count);
        Assert.All(lying, item => Assert.Equal(ItemMode.Equipped, item.Mode));
        Assert.Equal(worn.Select(item => item.Location).Order(), lying.Select(item => item.Location).Order());
        Assert.Equal(worn.Select(item => item.Id).Order(), lying.Select(item => item.Id).Order());
    }

    [CorpusFact]
    public void A_save_with_a_corpse_is_written_back_as_it_was_read()
    {
        if (!game.Available || !File.Exists(Specimen(Dead)))
            return;

        byte[] file = File.ReadAllBytes(Specimen(Dead));
        var (dead, _) = SaveVerifier.Load(Specimen(Dead));

        // The corpse's part of the file: its header, its three numbers and its items, where the file has them.
        byte[] corpses = CorpseList.Write(dead.PlayerCorpses, dead.Header.Version);
        Assert.Equal([0x4A, 0x4D, 0x01, 0x00], corpses[..4]);
        Assert.True(file.AsSpan().IndexOf(corpses) > 0, "the corpse list written is not the file's");
    }
}
