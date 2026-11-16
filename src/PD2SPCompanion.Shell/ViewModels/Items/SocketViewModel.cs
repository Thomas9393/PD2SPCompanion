using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Core.Graphics;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One socket of an item: a gem, rune or jewel in it, or an empty hole.
/// <para>
/// Everything that goes in a socket is a single cell, so the sprite is drawn at half a cell
/// and reads as a gem sitting on the item rather than as an item of its own.
/// </para>
/// </summary>
public sealed partial class SocketViewModel : ObservableObject
{
    /// <summary>
    /// A whole inventory cell, which is what a gem, rune or jewel occupies and the size the
    /// game draws one at over the item holding it.
    /// </summary>
    public const double FullSize = ItemTileViewModel.CellSize;

    /// <summary>
    /// How big to draw this one. A whole cell, except on an item with more sockets than
    /// cells to put them in - see <see cref="ItemTileViewModel"/>, which lays them out and
    /// sets this.
    /// </summary>
    public double Size { get; set; } = FullSize;

    /// <summary>Where it sits over the item, from the item's top left corner (SocketLayout).</summary>
    public double Left { get; set; }

    public double Top { get; set; }

    /// <summary><see cref="Left"/> and <see cref="Top"/> as the margin the layout pushes it by.</summary>
    public Avalonia.Thickness Place => new(Left, Top, 0, 0);

    /// <summary>The ring that stands for an empty socket, half the cell it sits in.</summary>
    public double HoleSize => Size / 2;

    public SocketViewModel(Item? gem, Item? host = null)
    {
        Gem = gem;
        _host = host;

        if (gem is null)
            return;

        var view = ItemPresenter.Describe(gem);
        Sprite = SpriteBitmap.From(SpriteLoader.Current?.Load(view.SpriteName, view.SpriteTint));

        // A jewel is named the way any magic or rare item is named, so it reads as the
        // thing it is - Crimson Jewel of Fortune - rather than as "Jewel". The presenter
        // already builds that.
        Name = view.Name;
    }

    private readonly Item? _host;

    /// <summary>What is in the socket, or null for an empty one.</summary>
    public Item? Gem { get; }

    /// <summary>
    /// Whether a jewel is in it: an item with affixes of its own, which the panel opens to edit where it lies.
    /// A gem or a rune is its base and nothing more.
    /// </summary>
    public bool IsJewel => Gem is { } gem && ItemTypes.LineageOfBase(gem.Code).Contains("jewl");

    public string Name { get; } = L("Empty socket");
    public Bitmap? Sprite { get; }

    /// <summary>
    /// The colour of the gem's own name. A jewel rolls affixes like anything else, so a
    /// magic one is named in blue - in plain text it read as a plain item.
    /// </summary>
    public IBrush NameBrush => RarityPalette.Of(Gem);

    /// <summary>
    /// What the gem gives the item it sits in, a line each. Worked out when first read - a card or
    /// the sockets section showing it: every socketed gem built a whole tile for it, and a load
    /// built a socket for each one, some 16 ms of it (measured).
    /// </summary>
    public IReadOnlyList<string> Effect => _effect ??= Gem is not { } gem ? [] :
    [
        // A jewel carries its own properties, worded as its own tooltip words them - four
        // resistances alike are "All Resistances +13%", as the game and the trade site say,
        // where stat by stat they were four lines. A gem or a rune carries none, and what it
        // grants depends on what it is in, so that is looked up instead.
        .. gem.StatLists.Any(list => list.Stats.Count > 0)
            ? new ItemTileViewModel(gem, 0).Summed.Where(line => line.IsMagic).Select(line => line.Text)
            : [],
        .. SocketEffects.On(gem, _host),
    ];

    private IReadOnlyList<string>? _effect;

    public bool HasEffect => Effect.Count > 0;

    /// <summary>The same, a line to each: the caption under the row of sockets.</summary>
    public string EffectLines => string.Join(Environment.NewLine, Effect);

    /// <summary>
    /// Its tooltip: its name, and what it gives the item under it. Every socket's but the one the caption under
    /// the row is of (<see cref="IsCaptioned"/>) - that one is said there already, and a socket not selected still
    /// shows its tooltip. It was none at all while any was selected.
    /// </summary>
    public string Tip => HasEffect ? Name + Environment.NewLine + EffectLines : Name;

    /// <summary>
    /// The tooltip the cell shows: <see cref="Tip"/>, none while the caption is this one's. None rather than the
    /// tooltip service turned off - which left a tooltip open when its socket was clicked and so captioned, and nothing
    /// closed it after.
    /// </summary>
    public string? ShownTip => IsCaptioned ? null : Tip;

    /// <summary>The caption under the row is this one's: it is the one selected alone. Of several selected the caption names none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownTip))]
    private bool _isCaptioned;

    /// <summary>The colour of a line that names no item: "3 sockets selected".</summary>
    public static IBrush PlainBrush => RarityPalette.Of(null);

    /// <summary>
    /// Struck through and red: this socket, or what is in it, goes when the item is kept.
    /// Nothing has happened yet - undoing puts it back.
    /// </summary>
    [ObservableProperty] private bool _isGoing;

    /// <summary>
    /// The one of the Edit page's strip that is selected: framed, said under the strip,
    /// and what the section's tools act on. It had a fold of its own before, its effect listed under its row.
    /// </summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Cut (ItemClipboard): faint in the row until it is pasted or let go. Asked of the clipboard, so a row built anew says the same.</summary>
    public bool IsCut => PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.IsCutSocketed(Gem);

    public void SayCut() => OnPropertyChanged(nameof(IsCut));

    public bool IsFilled => Sprite is not null;
    public bool IsEmpty => Sprite is null;
}
