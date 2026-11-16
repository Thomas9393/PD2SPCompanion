using Avalonia;
using Avalonia.Controls;

namespace PD2SPCompanion.Shell.Views;

/// <summary>An item summed up - art, name, facts, lines - off a <see cref="ViewModels.ItemTileViewModel"/>.</summary>
public partial class ItemSummary : UserControl
{
    public ItemSummary()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Whether the item's id stands beside its code and rarity: on the Edit page, where the item is in a
    /// file. Not on the Forge, where it is in none and its id is whatever it lands with.
    /// </summary>
    public static readonly StyledProperty<bool> ShowsIdProperty =
        AvaloniaProperty.Register<ItemSummary, bool>(nameof(ShowsId));

    public bool ShowsId
    {
        get => GetValue(ShowsIdProperty);
        set => SetValue(ShowsIdProperty, value);
    }

    /// <summary>
    /// How much of the summary's height is the art's, from the top, where its host makes the summary taller
    /// than that for the words' sake - the Edit page, whose tools stand under the art while the item's lines
    /// run down beside them. Unset, the art is centred in the whole height.
    /// </summary>
    public static readonly StyledProperty<double> ArtHeightProperty =
        AvaloniaProperty.Register<ItemSummary, double>(nameof(ArtHeight), double.NaN);

    public double ArtHeight
    {
        get => GetValue(ArtHeightProperty);
        set => SetValue(ArtHeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ArtHeightProperty)
            return;

        ArtRoom.Height = ArtHeight;
        ArtRoom.VerticalAlignment = double.IsNaN(ArtHeight) ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Top;
    }

    /// <summary>What the page has to warn of about the item as it stands - why a change cannot be kept - or nothing.</summary>
    public static readonly StyledProperty<string?> WarningProperty =
        AvaloniaProperty.Register<ItemSummary, string?>(nameof(Warning));

    public string? Warning
    {
        get => GetValue(WarningProperty);
        set => SetValue(WarningProperty, value);
    }
}
