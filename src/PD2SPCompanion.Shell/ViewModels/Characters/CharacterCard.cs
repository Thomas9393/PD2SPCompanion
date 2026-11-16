using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One character on disk, as the landing and character pages show it.</summary>
public sealed partial class CharacterCard : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public CharacterCard(SaveSummary save)
    {
        Save = save;
        // After whoever made the card has said whether a figure is coming (AwaitsFigure, Figure).
        Dispatcher.UIThread.Post(AskBareFigure, DispatcherPriority.Background);
    }

    public SaveSummary Save { get; }

    /// <summary>
    /// The character in its gear, as the game's select screen draws it - set once it has been composed.
    /// Until then, or where it cannot be, the class picture shows.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(HasFigure), nameof(ShowsClassPicture), nameof(Standing))]
    private PD2SPCompanion.Shell.Imaging.FigureFrames? _figure;

    public bool HasFigure => Figure is not null;

    /// <summary>
    /// A figure is being drawn for this card: nothing shows meanwhile, rather than the class picture
    /// for a moment before the figure replaces it (the gearless figure flashed
    /// before the geared one). Cleared once the figure is ready or known not to come.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(ShowsClassPicture), nameof(Standing))]
    private bool _awaitsFigure;

    /// <summary>The class picture: where there is no figure and none is coming.</summary>
    public bool ShowsClassPicture => Figure is null && !AwaitsFigure;

    public string Name => Save.Name;
    public string Path => Save.Path;
    public string Description => Save.Description;

    /// <summary>The header's own timestamp, which is truer than the file's write time.</summary>
    public string LastSaved => L($"Played {(Save.LastPlayed ?? Save.LastSaved):yyyy-MM-dd HH:mm}");

    /// <summary>
    /// The format records no play-time duration, so the creation date is the nearest
    /// thing to "how long has this character been around".
    /// </summary>
    public string CreatedOn => Save.Created is { } created ? L($"Created {created:yyyy-MM-dd}") : "";

    public bool HasCreated => Save.Created is not null;

    public string Mode => Save.Mode;
    public string Ruleset => Save.Ruleset;
    /// <summary>
    /// True only for a hardcore character that has died - see <see cref="SaveSummary"/>,
    /// where the flag's real meaning is set out. The badge is bound to this, so a softcore
    /// character that has died shows nothing.
    /// </summary>
    public bool IsDead => Save.IsDead;

    /// <summary>
    /// Hardcore is worth flagging in the colour of a broken item, softcore in green (it was magic
    /// blue). Resolved here rather than through a converter, the way
    /// ItemTileViewModel resolves a rarity colour.
    /// </summary>
    public IBrush ModeBrush => Brush(
        Save.IsHardcore ? "[Brush] Rarity - Broken items" : "[Brush] Rarity - Set items");

    private static IBrush Brush(string key)
    {
        var resources = Application.Current?.Resources;
        return resources is not null
               && resources.TryGetResource(key, null, out object? found)
               && found is IBrush brush
            ? brush
            : Brushes.Gainsboro;
    }

    /// <summary>
    /// The class with nothing on, drawn from the game's own sprites (CharacterFigure.BareLookOf): what shows
    /// where no figure in gear is or will be - an armory card, a save that will not read. Drawn only then,
    /// once. The editor ships no game art, so no class animation either.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(Standing))]
    private PD2SPCompanion.Shell.Imaging.FigureFrames? _bareFigure;

    /// <summary>
    /// The figure on the card now - in its gear, or bare where the class picture shows; null while none is.
    /// What the town under it is laid to: its feet on the place to stand, its shadow under them.
    /// </summary>
    public PD2SPCompanion.Shell.Imaging.FigureFrames? Standing => Figure ?? (ShowsClassPicture ? BareFigure : null);

    private bool _bareAsked;

    partial void OnFigureChanged(PD2SPCompanion.Shell.Imaging.FigureFrames? value) => AskBareFigure();

    partial void OnAwaitsFigureChanged(bool value) => AskBareFigure();

    private void AskBareFigure()
    {
        if (!ShowsClassPicture || _bareAsked)
            return;
        _bareAsked = true;
        _ = DrawBareFigureAsync();
    }

    private async System.Threading.Tasks.Task DrawBareFigureAsync()
    {
        try
        {
            BareFigure = await PD2SPCompanion.Shell.Imaging.FigureBitmaps.ComposeAsync(
                PD2SPCompanion.Core.Graphics.CharacterFigure.BareLookOf(Save.ClassId), smooth: 2);
        }
        catch (Exception problem)
        {
            // No figure at all: the card shows its words alone.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }

    /// <summary>
    /// The town the character is in, around its stash (Core's TownScene): what the opened card's figure stands
    /// on. Drawn from the install the first time it is asked for - by the card's view,
    /// once it shows this card - one picture an act, shared by every card in that act; null until then, and
    /// where the install gives no town.
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap? Backdrop
    {
        get
        {
            if (!_backdropAsked)
            {
                _backdropAsked = true;
                _ = DrawBackdropAsync();
            }
            return _backdrop;
        }
    }

    private Avalonia.Media.Imaging.Bitmap? _backdrop;
    private bool _backdropAsked;

    private async System.Threading.Tasks.Task DrawBackdropAsync()
    {
        try
        {
            if (await PD2SPCompanion.Shell.Imaging.TownBitmaps.OfAsync(Save.Act) is { } drawn)
            {
                _backdrop = drawn;
                OnPropertyChanged(nameof(Backdrop));
            }
        }
        catch (Exception problem)
        {
            // No backdrop: the figure stands on black, as it did.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }

    /// <summary>
    /// What the standing search finds on this character, a line an item - its name and where it lies - while
    /// the home page's list is asked (find across characters). Empty with no search, and
    /// where it has none.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(HasFinds), nameof(FoundText), nameof(FoundLines))]
    private System.Collections.Generic.IReadOnlyList<string> _finds = [];

    public bool HasFinds => Finds.Count > 0;

    /// <summary>
    /// A search stands and this character has nothing of it: the card is dimmed where it lies, as the Items
    /// page dims an item the search leaves out.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isFindMiss;

    /// <summary>The card's mark: how many.</summary>
    public string FoundText => L($"{Finds.Count} found");

    /// <summary>The most lines the mark's tooltip lists; the rest are counted under them.</summary>
    public const int MostFoundLines = 14;

    /// <summary>The mark's tooltip: the finds by name, and how many more where they are many.</summary>
    public string FoundLines => Finds.Count <= MostFoundLines
        ? string.Join("\n", Finds)
        : string.Join("\n", System.Linq.Enumerable.Take(Finds, MostFoundLines)) + "\n" + L($"and {Finds.Count - MostFoundLines} more");

    /// <summary>Name and date on one line, for the list row.</summary>
    public string Summary => $"{Save.Description}  -  {Save.LastSaved:yyyy-MM-dd HH:mm}";
}
