namespace PD2SPCompanion.Core.Graphics;

/// <summary>A decoded inventory sprite, ready to draw.</summary>
public sealed record Sprite(string InvFile, int Width, int Height, uint[] Bgra)
{
    /// <summary>What tells this drawing from another of the same file - the file and its tint.</summary>
    public string Key { get; init; } = InvFile;
}

/// <summary>
/// Loads inventory graphics out of the installed archives, decoded and palettised.
/// Sprites are cached because a stash page asks for the same graphic repeatedly.
/// <para>
/// Reading art from the install rather than shipping a PNG dump is the same rule the
/// tables follow: PD2 adds and changes item art every season, and a bundled copy would
/// quietly show last season's graphic — or nothing at all for a new item.
/// </para>
/// </summary>
public sealed class SpriteLoader(GameData game, Palette palette)
{
    private readonly Dictionary<string, Sprite?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static SpriteLoader? Current { get; private set; }

    public static SpriteLoader Install(GameData game, Palette palette) =>
        Current = new SpriteLoader(game, palette);

    /// <summary>
    /// Whether a graphic of that name is in any archive. Asked before composing a name
    /// from parts, since a name that resolves to nothing draws nothing and says nothing
    /// about why.
    /// </summary>
    public bool Has(string? invFile) =>
        !string.IsNullOrWhiteSpace(invFile)
        && (_cache.ContainsKey(invFile) || game.Read(GameData.ItemSpritePath(invFile)) is not null);

    /// <summary>The sprite for an "invfile" value, or null when the archives have no such graphic.</summary>
    public Sprite? Load(string? invFile) => Load(invFile, null);

    /// <summary>The sprite drawn through a tint - see <see cref="ColourMaps"/>. Null draws it plain.</summary>
    public Sprite? Load(string? invFile, Tint? tint)
    {
        if (string.IsNullOrWhiteSpace(invFile))
            return null;

        var remap = tint is null ? null : ColourMaps.For(tint);
        string key = remap is null ? invFile : $"{invFile}|{tint!.Table}|{tint.Colour}";

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        Sprite? sprite = null;
        var result = game.Read(GameData.ItemSpritePath(invFile));
        if (result is not null)
        {
            try
            {
                // Inventory graphics are single-frame; take the first and ignore the rest.
                var frame = Dc6.Read(result.Value.Data)[0];
                sprite = new Sprite(invFile, frame.Width, frame.Height, frame.ToBgra(palette, remap)) { Key = key };
            }
            catch (Exception)
            {
                // A graphic that will not decode should not stop a character from opening.
                sprite = null;
            }
        }

        _cache[key] = sprite;
        return sprite;
    }
}
