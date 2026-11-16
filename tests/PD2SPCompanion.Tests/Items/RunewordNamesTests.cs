namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A runeword is named as the game names it - the string table's text for its key - and not by runes.txt's note
/// column (some names were not the game's: Exile was exile's path,
/// Doom was doomsayer). The Grail groups words by the listed name and keeps a find by the group's first row,
/// so the names stay as many as the notes were.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class RunewordNamesTests(Game game)
{
    [GameFact]
    public void A_runeword_is_called_what_the_game_calls_it_and_the_grail_keeps_its_groups()
    {
        if (!game.Available)
            return;

        string Named(string note) => Runewords.GameName(Runewords.All.First(word => word.Name == note));

        Assert.Equal("Exile", Named("Exile's Path"));
        Assert.Equal("Doom", Named("Doomsayer"));
        Assert.Equal("Grief", Named("Widowmaker"));
        Assert.Equal("Chains of Honor", Named("Bound by Duty"));

        // Two rows the game names alike stay two in a list: told apart by their version.
        var echoes = Runewords.All.Where(word => word.Name.StartsWith("Echo ")).Select(Runewords.DisplayName).Order().ToList();
        Assert.Equal(["Echo (Quiver)", "Echo (Weapon)"], echoes);

        Assert.Equal(Runewords.All.Select(word => word.Name).Distinct().Count(),
                     Runewords.All.Select(Runewords.DisplayName).Distinct().Count());
    }
}
