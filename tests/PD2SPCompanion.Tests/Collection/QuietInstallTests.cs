using PD2SPCompanion.Core.GameFiles;

using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The tables installed and said to no one (Season.Open, announce off): how the program reads the
/// game's files on a worker at its first lines, and tells those who listen later, on their own thread.
/// The install's number must move at once - what is
/// built from the tables on the worker is that install's and is kept - and the notice must wait for its call.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class QuietInstallTests(Game game)
{
    [GameFact]
    public void A_quiet_install_moves_the_number_and_tells_no_one_until_it_is_announced()
    {
        if (!game.Available)
            return;

        int told = 0;
        void Count() => told++;
        InstalledTables.Changed += Count;
        try
        {
            int before = InstalledTables.Generation;

            // Left open, as the fixture's tables are, for the tests after this one.
            var season = Season.Open(Game.Root, Game.Tables, announce: false);
            Assert.Same(season, Season.Current);
            Assert.Equal(before + 1, InstalledTables.Generation);
            Assert.Equal(0, told);

            // What is built now is this install's: asked again after the notice, it is the same one.
            var uniques = GrailCatalog.Uniques();
            Assert.NotEmpty(uniques);

            InstalledTables.Announce();
            Assert.Equal(1, told);
            Assert.Equal(before + 1, InstalledTables.Generation);
            Assert.Same(uniques, GrailCatalog.Uniques());

            // And an install that says itself says it once, as it always did.
            Season.Open(Game.Root, Game.Tables);
            Assert.Equal(2, told);
            Assert.Equal(before + 2, InstalledTables.Generation);
        }
        finally
        {
            InstalledTables.Changed -= Count;
        }
    }
}
