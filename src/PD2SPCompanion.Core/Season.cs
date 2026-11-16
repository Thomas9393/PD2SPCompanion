namespace PD2SPCompanion.Core;

/// <summary>
/// The game data one season of PD2 is read with: the archive chain, and every table and cache
/// installed from it. The one way in -
/// where "open the archives, then install the tables" used to be written out at every command.
/// <para>
/// <b>The first step of several.</b> Today the tables it installs still live in static classes
/// (<see cref="SaveTables.Install"/> fills 26 of them) and <see cref="Current"/> is the one
/// installed. They move into the season one domain at a time, until two seasons can be
/// open at once and a rule can be tested without an install.
/// </para>
/// </summary>
public sealed class Season : IDisposable
{
    private Season(GameData game, string root, string? looseFirst)
    {
        Game = game;
        Root = root;
        LooseTables = looseFirst;
        Build = PatchGuard.Fingerprint(root);
        Name = ModVersion.Of(root).Name;
    }

    /// <summary>The season installed last - what the static tables hold now.</summary>
    public static Season? Current { get; private set; }

    /// <summary>The archive chain the tables come from.</summary>
    public GameData Game { get; }

    /// <summary>The game folder.</summary>
    public string Root { get; }

    /// <summary>A folder of loose .txt tables read ahead of the archives - an older season's - or null.</summary>
    public string? LooseTables { get; }

    /// <summary>The build's fingerprint (<see cref="PatchGuard.Fingerprint"/>): what the patch guard compares.</summary>
    public string Build { get; }

    /// <summary>The mod version the launcher reports, "12.0.6b"; empty where none did. Shown, never compared.</summary>
    public string Name { get; }

    /// <summary>
    /// <c>PD2SPC_TABLES</c>, in a Debug build only: a folder of tables laid over the installed archives where the
    /// caller names none - a season's snapshot (<c>Data\Seasons\S13</c>) puts the season the test corpus was taken
    /// in under the program the Shell tests run, while another is installed (the S14 open beta).
    /// </summary>
    private static string? TestTables =>
#if DEBUG
        Environment.GetEnvironmentVariable("PD2SPC_TABLES") is { Length: > 0 } tables ? tables :
#endif
        null;

    /// <summary>
    /// Opens the archives under <paramref name="gameRoot"/> - with <paramref name="looseFirst"/>'s
    /// tables ahead of them, where given - and installs every table from them. <paramref name="announce"/>
    /// off, the install is said to no one until the caller says it (InstalledTables.Announce): how the
    /// program opens its season on a worker at start-up.
    /// </summary>
    public static Season Open(string gameRoot, string? looseFirst = null, bool announce = true)
    {
        looseFirst ??= TestTables;
        var game = GameData.Open(gameRoot, looseFirst);
        try
        {
            SaveTables.Install(game, announce);
        }
        catch (Exception problem)
        {
            Log.Error("season", $"Tables not installed from {gameRoot}", problem);
            game.Dispose();
            throw;
        }

        Current = new Season(game, gameRoot, looseFirst);

        // The item stamps this season and the one before write, from their snapshots, for the reader
        // that tells a last-season file from this season's (SeasonReading).
        SeasonSnapshot.ApplyStamps(Current);
        SeasonSnapshot.ApplyEarlierRules(Current);
        Log.Info("season", $"Opened {gameRoot}: build {Current.Build}, mod version {(Current.Name.Length > 0 ? Current.Name : "unknown")}"
                           + (looseFirst is null ? "" : $", loose tables from {looseFirst}"));
        return Current;
    }

    /// <summary>
    /// Installs this season's tables again, after another season was opened in turn - the season migration
    /// reads an old season and then puts the caller's own back. What was built from the other season's tables follows (InstalledTables).
    /// </summary>
    public void Reinstall()
    {
        SaveTables.Install(Game);
        Current = this;
        SeasonSnapshot.ApplyStamps(this);
        SeasonSnapshot.ApplyEarlierRules(this);
        Log.Info("season", $"Tables of {Root} installed again: build {Build}");
    }

    public void Dispose()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        Game.Dispose();
    }
}
