namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// Which install of the tables is in force: a number that moves every time tables are installed, and
/// an event for those who would rather be told.
/// <para>
/// The tables are installed into static classes, and anything that builds something from them and
/// keeps it - a grail catalogue, a property list, decoded pictures - keeps the season it was built in.
/// After S12 then S13 in one process the grail catalogue still held S12's 674 entries, names re-read
/// through S13's strings (measured). A cache keeps the <see cref="Generation"/> it was built
/// at and builds again when it has moved; nothing has to remember to clear it.
/// </para>
/// </summary>
public static class InstalledTables
{
    /// <summary>Moves on every install; a cache built at another number is stale.</summary>
    public static int Generation { get; private set; }

    /// <summary>Raised after every install, for caches that clear rather than check.</summary>
    public static event Action? Changed;

    /// <summary>Said once the tables are in: by <c>SaveTables.Install</c>, and nothing else.</summary>
    public static void Installed()
    {
        Generation++;
        Changed?.Invoke();
    }

    /// <summary>
    /// The tables are in and nobody is told yet: the number moves - what is built from them from here on is
    /// this install's - and <see cref="Changed"/> waits for <see cref="Announce"/>. For an install made off the
    /// UI thread, ahead of those who listen (the program's start-up reads the game files beside its splash):
    /// they are told on their own thread, at the moment they always were.
    /// </summary>
    public static void InstalledQuietly() => Generation++;

    /// <summary>What <see cref="InstalledQuietly"/> left unsaid: <see cref="Changed"/>, raised on the caller's thread.</summary>
    public static void Announce() => Changed?.Invoke();
}
