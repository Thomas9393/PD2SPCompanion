using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Shell.Imaging;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>The game's title, the season and the jeweled rule. See the XAML.</summary>
public partial class SeasonBanner : UserControl
{
    /// <summary>
    /// The jewel in the socket: one of the game's six jewel pictures (<c>invjw1</c> to <c>invjw6</c>), picked
    /// once a run, not once a page. Read from the player's install as every item is, not shipped as
    /// upscaled copies.
    /// </summary>
    private static readonly int Picked = Random.Shared.Next(1, 7);

    /// <summary>
    /// The 28 pixels enlarged four times with Scale2x, as the figures are (PixelArt): the socket draws it at
    /// 32, smoothed down, so it keeps its edges. Null until the game's tables are in - a first run's setup.
    /// </summary>
    private static IImage? _jewel;

    private static IImage? Load()
    {
        if (_jewel is not null)
            return _jewel;
        if (SpriteLoader.Current?.Load($"invjw{Picked}") is not { } sprite)
            return null;

        const int Times = 2;
        var enlarged = PixelArt.Scale2x(sprite.Bgra, sprite.Width, sprite.Height, Times);
        return _jewel = SpriteBitmap.FromPixels(sprite.Width << Times, sprite.Height << Times, enlarged);
    }

    public static readonly DirectProperty<SeasonBanner, IImage?> JewelProperty =
        AvaloniaProperty.RegisterDirect<SeasonBanner, IImage?>(nameof(Jewel), banner => banner.Jewel);

    public IImage? Jewel => Load();

    public static readonly DirectProperty<SeasonBanner, string> SeasonTitleProperty =
        AvaloniaProperty.RegisterDirect<SeasonBanner, string>(nameof(SeasonTitle), banner => banner.SeasonTitle);

    /// <summary>
    /// The installed season, as the game numbers it: "Season XIV". Read from the install, as every table is - a
    /// season written in here stood a season behind the game from its first patch. The tables name no season's
    /// title, so none is given. Empty until the tables are in.
    /// </summary>
    public string SeasonTitle =>
        PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out string name, out _)
        && name.Length > 1 && name[0] == 'S' && int.TryParse(name[1..], out int number) && number is > 0 and < 100
            ? L($"Season {Roman(number)}")
            : "";

    /// <summary>A number from 1 to 99 in Roman numerals: its tens, then its ones.</summary>
    private static string Roman(int number) =>
        Digit(number / 10, 'X', 'L', 'C') + Digit(number % 10, 'I', 'V', 'X');

    /// <summary>One decimal digit in the numerals of its place: one, five and ten of that place.</summary>
    private static string Digit(int digit, char one, char five, char ten) => digit switch
    {
        9 => $"{one}{ten}",
        >= 5 => five + new string(one, digit - 5),
        4 => $"{one}{five}",
        _ => new string(one, digit),
    };

    public SeasonBanner() => InitializeComponent();

    // The pages are built before the tables are opened behind the splash, and a first run opens them
    // later still: asked again on showing, and whenever the tables arrive.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InstalledTables.Changed += Refresh;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        InstalledTables.Changed -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }

    // Posted: the sprites are installed beside the tables, and may follow the notice.
    private void Refresh() =>
        Dispatcher.UIThread.Post(() =>
        {
            RaisePropertyChanged(JewelProperty, null, Jewel);
            RaisePropertyChanged(SeasonTitleProperty, "", SeasonTitle);
        });
}
