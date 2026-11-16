namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// The towns' backdrops (<see cref="TownScene.Backdrop"/>), drawn from the installed game: installed beside
/// the tables, as the figures' loader is. It keeps none - the Shell keeps the bitmap it makes of each
/// (TownBitmaps), and a second copy here was 1.4 MB an act held for nothing.
/// </summary>
public sealed class TownBackdrops(GameData game)
{

    public static TownBackdrops? Current { get; private set; }

    public static TownBackdrops Install(GameData game) => Current = new TownBackdrops(game);

    /// <summary>The backdrop of an act, 1 to 5, drawn anew; null where the install gives no town for it. Safe on a worker thread.</summary>
    public Picture? Of(int act) => TownScene.Open(game, act)?.Backdrop();
}
