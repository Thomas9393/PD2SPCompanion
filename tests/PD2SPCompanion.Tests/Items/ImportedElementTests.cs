using System.Text.Json;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// An elemental skill bonus read from a listing keeps its line: the armory and the trade site list only the words
/// (<c>item_elemskill_magic</c>), never the grant beside them, and the sync that takes ungranted words off lost every
/// imported "+1 to Magic Skills" - Kadala's Heirloom then lost the line that tells its variant and was
/// judged against all six. The ring as a level 99 Necromancer of the ladder wears it.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ImportedElementTests(Game game)
{
    private const string Kadala = """
        {"is_identified": true, "is_socketed": false, "is_new": false, "is_ear": false, "is_starter": false, "is_simple": false,
         "is_ethereal": false, "is_personalized": false, "is_runeword": false, "format_version": 103,
         "base": {"id": "rin", "category": "misc", "codes": {"normal": "rin", "exceptional": "", "elite": ""}, "name": "Ring",
                  "stackable": false, "type": "Ring", "type_code": "ring", "size": {"height": 1, "width": 1}, "requirements": {}},
         "socketed_count": 0, "id": 310102412104, "item_level": 96, "quality": {"id": 7, "name": "Unique"}, "graphic_id": 1,
         "class_specifics": false, "socket_count": 0,
         "modifiers": [
           {"name": "item_elemskill_magic", "values": [0, 1], "label": "+1 to Magic Skills", "priority": 157},
           {"name": "all_resist", "values": [-5], "label": "All Resistances +-5%", "priority": 36},
           {"name": "magic_damage_reduction", "values": [10], "label": "Magic Damage Taken Reduced by 10", "priority": 21},
           {"name": "item_goldbonus", "values": [72], "label": "72% Extra Gold from Monsters", "priority": 10},
           {"name": "item_magicbonus", "values": [30], "label": "30% Better Chance of Getting Magic Items", "priority": 8},
           {"name": "item_reducedprices", "values": [6], "label": "Reduces all Vendor Prices 6%", "priority": 8},
           {"name": "item_levelreq", "values": [75], "label": "undefined", "priority": 0}],
         "name": "Kadala's Heirloom", "category": "misc", "base_code": "rin",
         "requirements": {"level": "", "strength": 0, "dexterity": 0}, "corrupted": false, "desecrated": false}
        """;

    [GameFact]
    public void A_listed_elemental_bonus_keeps_its_words_and_gains_its_grant()
    {
        if (!game.Available)
            return;

        using var listing = JsonDocument.Parse(Kadala);
        var item = TradeItems.Build(listing.RootElement).Item;
        var stats = ItemStats.Every(item).ToList();

        Assert.Contains(stats, stat => stat.Stat == "item_elemskill_magic" && stat.Value == 1);
        Assert.Contains(stats, stat => stat.Stat == ElementSkills.Grant && stat.Param == 3 && stat.Value == 1);
        Assert.Empty(ElementSkills.Apart(item));

        // Its variant told, it is judged against the magic one alone: silent.
        Assert.True(KadalasHeirloom.Is(item));
        Assert.Empty(Legitimacy.Check(item));
    }
}
