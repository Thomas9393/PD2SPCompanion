using Avalonia;
using Avalonia.Media;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The colour the game writes an item's name in. Shared, because a socketed gem is named
/// on the detail page the same way the item holding it is named on its tile.
/// </summary>
public static class RarityPalette
{
    /// <summary>
    /// The colour to wash a cell with. The same as the name colour unless the palette
    /// carries a wash of its own for that quality - a colour chosen to read as a
    /// background rather than as text.
    /// </summary>
    public static IBrush WashOf(Item? item)
    {
        // Washed by what the item is, not by what it is called. The game tints a Nadir cap
        // in the grid the way it tints any cap - the gold is the name's alone - so the
        // runeword is deliberately not asked about here.
        string quality = QualityKeyOf(item);

        // Found once a quality and theme: every tile asked it at every paint - a string made and the theme's
        // dictionaries searched twice.
        var key = (quality, Application.Current?.ActualThemeVariant);
        if (Washes.TryGetValue(key, out var kept))
            return kept;

        return Washes[key] = Resolve(quality.Replace("Rarity - ", "Rarity Wash - "))
                             ?? Resolve(quality)
                             ?? Brushes.Gainsboro;
    }

    private static readonly System.Collections.Generic.Dictionary<(string, Avalonia.Styling.ThemeVariant?), IBrush> Washes = [];

    public static IBrush Of(Item? item) => Resolve(KeyOf(item)) ?? Brushes.Gainsboro;

    /// <summary>
    /// The brush a palette key names, or null for one it does not carry.
    /// <para>
    /// Asked of the application with the variant it is actually running, not with null. The
    /// palette lives under ResourceDictionary.ThemeDictionaries, keyed by variant, so a null
    /// variant matches nothing and every item comes back the same fallback colour.
    /// </para>
    /// </summary>
    public static IBrush? Resolve(string key)
    {
        var app = Application.Current;

        return app is not null
               && app.TryGetResource(key, app.ActualThemeVariant, out object? found)
               && found is IBrush brush
            ? brush
            : null;
    }

    /// <summary>
    /// The colour the item's <b>name</b> is printed in.
    /// <para>
    /// A runeword keeps the quality of the base it was made in - a Nadir cap is a normal cap
    /// - and the game pays no attention to that when it prints the name: it uses the gold it
    /// prints a unique's in. The cell behind the item keeps the base's own colour, which is
    /// why the wash does not come through here.
    /// </para>
    /// </summary>
    public static string KeyOf(Item? item) =>
        item is { IsCompact: false, IsRuneword: true }
            ? "[Brush] Rarity - Unique items"
            : QualityKeyOf(item);

    /// <summary>
    /// The colour of a quality by the game's number for it, as a vault row keeps it: for what is drawn of an
    /// item that is not read - a block of the vault's small map.
    /// </summary>
    public static IBrush OfQuality(int quality) => Resolve(KeyOfQuality((ItemQuality)quality)) ?? Brushes.Gainsboro;

    private static string QualityKeyOf(Item? item) =>
        item is null || item.IsCompact ? "[Brush] Rarity - Normal items" : KeyOfQuality(item.Quality);

    /// <summary>
    /// Everything that is not magic or better reads as a plain item - crude, damaged,
    /// superior and plain all share the one colour, as they do in game, since the difference
    /// is in the numbers rather than in what kind of item it is.
    /// </summary>
    private static string KeyOfQuality(ItemQuality quality) => quality switch
        {
            ItemQuality.Magic => "[Brush] Rarity - Magic items",
            ItemQuality.Rare => "[Brush] Rarity - Rare items",
            ItemQuality.Set => "[Brush] Rarity - Set items",
            ItemQuality.Unique => "[Brush] Rarity - Unique items",
            ItemQuality.Craft or ItemQuality.Tempered => "[Brush] Rarity - Crafted items",
            _ => "[Brush] Rarity - Normal items",
        };
}
