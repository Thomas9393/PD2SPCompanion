using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using PD2SPCompanion.Shell.Helper;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class MainWindow : Window
{
    /// <summary>Set once the unsaved-changes question has been answered with yes.</summary>
    private bool _confirmedQuit;

    public MainWindow()
    {
        InitializeComponent();

        // A run on test copies says so in the taskbar too (PD2SPCompanion.Core.Saves.TestCopies).
        if (PD2SPCompanion.Core.Saves.TestCopies.IsOn)
            Title = L($"{Title} - TEST COPY");

        // The platform frame is kept for native dragging and animations, so its border is
        // recoloured rather than removed.
        Opened += (_, _) => Win11Frame.Apply(this);

        // Where it was left, as large as it was, maximized if it was (EditorSettings.Window). Not its page: a new
        // start opens on the home page.
        GiveBackPlace();

        // Coming back from the game: what it saved meanwhile is noticed now, whether or not
        // the folder watcher caught it. See GameSync.
        Activated += (_, _) => (DataContext as MainViewModel)?.Sync?.Check();

        // Out of use - the game in front, or minimised - nothing plays; back in front, it plays again.
        Activated += (_, _) => PD2SPCompanion.Shell.Services.Settings.Motion.SetAsleep(false);
        Deactivated += (_, _) => PD2SPCompanion.Shell.Services.Settings.Motion.SetAsleep(true);

        // Never larger than the screen it is on: asked when it opens, when it is carried to another
        // screen, and when that screen's scaling changes.
        _leastWidth = MinWidth;
        _leastHeight = MinHeight;
        // Once more when it has been shown: a height asked while it opens is the platform's to overrule.
        Opened += (_, _) =>
        {
            HoldToScreen();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HoldToScreen(again: true),
                                                        Avalonia.Threading.DispatcherPriority.Background);
        };
        PositionChanged += (_, _) => HoldToScreen();
        ScalingChanged += (_, _) => HoldToScreen();

        // The scale chosen in Settings: the same screen, asked again.
        PD2SPCompanion.Shell.Services.Settings.UiScale.Changed += () => HoldToScreen(again: true);

        // The window's search (QuickSearchViewModel): its list under its box, and out of the way of a click
        // anywhere else.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel { QuickSearch: { } search })
            {
                search.PropertyChanged += (_, changed) =>
                {
                    if (changed.PropertyName == nameof(QuickSearchViewModel.IsListOpen) && search.IsListOpen)
                        PlaceQuickList();
                };
            }
        };
        QuickBar.LayoutUpdated += (_, _) =>
        {
            PlaceQuickBar();
            PlaceQuickList();
        };
        AddHandler(PointerPressedEvent, (_, pressed) =>
        {
            // Something typed, its list up or still on its way: a click elsewhere ends that search - the list
            // put away and the box emptied (QuickSearchViewModel.Close).
            if (DataContext is not MainViewModel { QuickSearch: { } search } || !(search.IsListOpen || search.HasText)
                || pressed.Source is not Avalonia.Visual from)
            {
                return;
            }

            // Not for a click in the list, or in the box: those are the search's own.
            foreach (var each in Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(from))
            {
                if (ReferenceEquals(each, QuickList) || ReferenceEquals(each, QuickBar))
                    return;
            }

            search.Close();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private MainView? _book;

    /// <summary>
    /// The search box centred on the spine between the two pages, a strong visual indicator. The window's own middle lies left
    /// of the spine by half the side bar, and the box was centred there. Wherever the spine is: read off the
    /// two pages' inner edges.
    /// </summary>
    private void PlaceQuickBar()
    {
        _book ??= Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<MainView>().FirstOrDefault();
        if (_book?.FindControl<Control>("ViewPage_Left") is not { Bounds.Width: > 0 } left
            || _book.FindControl<Control>("ViewPage_Right") is not { Bounds.Width: > 0 } right
            || QuickBar.Parent is not Visual row
            || QuickBar.FindControl<Control>("Box") is not { Bounds.Width: > 0 } box
            || left.TranslatePoint(new Point(left.Bounds.Width, 0), row) is not { } leftEdge
            || right.TranslatePoint(new Point(0, 0), row) is not { } rightEdge
            || box.TranslatePoint(new Point(box.Bounds.Width / 2, 0), QuickBar) is not { } boxMiddle)
        {
            return;
        }

        double spine = (leftEdge.X + rightEdge.X) / 2;
        double at = Math.Max(0, spine - boxMiddle.X);
        if (QuickBar.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left && Math.Abs(QuickBar.Margin.Left - at) < 0.5)
            return;

        QuickBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        QuickBar.Margin = new Thickness(at, QuickBar.Margin.Top, 0, QuickBar.Margin.Bottom);
    }

    /// <summary>The search's list put under its box, their left edges in line, whatever the window's width.</summary>
    private void PlaceQuickList()
    {
        if (!QuickList.IsVisible
            || QuickBar.FindControl<Control>("Box") is not { } box
            || box.TranslatePoint(new Point(0, box.Bounds.Height + 6), QuickLayer) is not { } under)
        {
            return;
        }

        if (Math.Abs(Canvas.GetLeft(QuickList) - under.X) > 0.5 || double.IsNaN(Canvas.GetLeft(QuickList)))
            Canvas.SetLeft(QuickList, under.X);
        if (Math.Abs(Canvas.GetTop(QuickList) - under.Y) > 0.5 || double.IsNaN(Canvas.GetTop(QuickList)))
            Canvas.SetTop(QuickList, under.Y);
    }

    /// <summary>The scale the content is drawn at (Services.Settings.UiScale): 1 on the reference monitor.</summary>
    private double _scale = 1;

    /// <summary>
    /// The window's content scaled for the screen it is on: the
    /// step chosen in Settings, or Automatic's for this screen's room and Windows' scale there. The layout
    /// then has the window's size over the scale to lay itself out in.
    /// </summary>
    private void ApplyScale(Size room)
    {
        double scaling = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1;
        double scale = PD2SPCompanion.Shell.Services.Settings.UiScale.For(PD2SPCompanion.Shell.Services.Settings.UiScale.Percent, room, scaling);
        if (Math.Abs(scale - _scale) < 0.0005)
            return;

        _scale = scale;
        PD2SPCompanion.Shell.Services.Settings.UiScale.Current = scale;
        Scaler.LayoutTransform = Math.Abs(scale - 1) < 0.0005 ? null : new Avalonia.Media.ScaleTransform(scale, scale);
        PD2SPCompanion.Core.Diagnostics.Log.Info("Window", $"UI scale {scale:0.###} (setting {PD2SPCompanion.Shell.Services.Settings.UiScale.Percent}, room {room.Width:0} x {room.Height:0}, Windows {scaling:0.##})");
    }

    /// <summary>The least size the layout was drawn for (<c>[UI] Windows MinWidth</c> and <c>MinHeight</c>).</summary>
    private readonly double _leastWidth, _leastHeight;

    /// <summary>The room last held to: a drag across one screen asks many times and is answered once.</summary>
    private Size _heldTo;

    /// <summary>
    /// The room the screen under the window leaves it, in the window's own units: its working area over its
    /// scaling. <c>PD2SPC_SCREEN</c> (<c>1280x672</c>) says it instead, for a test on a screen that has more.
    /// </summary>
    private Size? Room()
    {
        if (Environment.GetEnvironmentVariable("PD2SPC_SCREEN")?.Split('x') is [var wide, var tall]
            && double.TryParse(wide, System.Globalization.CultureInfo.InvariantCulture, out double w)
            && double.TryParse(tall, System.Globalization.CultureInfo.InvariantCulture, out double h))
            return new Size(w, h);

        return (Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen
            ? new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling)
            : null;
    }

    /// <summary>
    /// The window's least size held to the screen's room: at
    /// 1920 x 1080 and 150% the room is 1280 x 672, the least size was 1280 x 720 and more with the frame, and
    /// the window's foot - the message strips - lay under the taskbar with no way to bring it up. A window
    /// already larger than the room is brought down to it. The layout under its least size scrolls or is cut
    /// inside the window, where it can be seen to be.
    /// </summary>
    private void HoldToScreen(bool again = false)
    {
        if (Room() is not { } room || (room == _heldTo && !again) || room.Width <= 0 || room.Height <= 0)
            return;

        _heldTo = room;
        ApplyScale(room);

        // The window stands taller than the height asked of it by the platform's caption (Win11Frame), and
        // a least height as much: both are asked for less by that.
        double caption = Win11Frame.CaptionHeight(this);
        double most = room.Height - caption;

        // The least layout as it is drawn: scaled, it needs that much less - or more - of the screen.
        MinWidth = Math.Min(_leastWidth * _scale, room.Width);
        MinHeight = Math.Min(_leastHeight * _scale, most);

        if (WindowState != WindowState.Normal)
            return;

        // The height it stands at is its client's, the caption in it; the height asked is without.
        if (ClientSize.Width > room.Width || Width > room.Width)
            Width = room.Width;
        if (ClientSize.Height > room.Height || Height > most)
            Height = most;

        // Its head in sight: centred as it opened, a window brought down in height would begin above the screen.
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen
            && (Position.Y < screen.WorkingArea.Y || Position.X < screen.WorkingArea.X - 16))
            Position = new PixelPoint(Math.Max(Position.X, screen.WorkingArea.X), Math.Max(Position.Y, screen.WorkingArea.Y));
    }

    /// <summary>
    /// Quitting with edits in memory would lose them silently, so the close is cancelled
    /// and the question asked instead. The overlay lives in the window, so it can only be
    /// shown by letting this close attempt fail and closing again once answered.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Anything on the cursor goes back first. Nothing is at stake - a held item has
        // not moved in the save - but it keeps the question below honest: what it asks
        // about is what is actually different on disk.
        if (DataContext is MainViewModel holding && holding.Session.IsHolding)
            holding.Session.CancelHold();

        // What is waiting to save is saved (the autosave): quitting then loses nothing, and the
        // question below is only for what could not be saved.
        if (!_confirmedQuit && DataContext is MainViewModel saving)
            saving.AutoSave?.Flush();

        if (!_confirmedQuit
            && DataContext is MainViewModel main
            && (main.Sync?.HasEdits ?? main.Session.HasUnsavedChanges))
        {
            e.Cancel = true;

            main.Confirm.Ask(
                L("Unsaved changes"),
                L($"{main.Session.CharacterName} has unsaved changes. Quit discards them."),
                L("QUIT ANYWAY"),
                () =>
                {
                    _confirmedQuit = true;
                    Close();
                });

            return;
        }

        KeepPlace();
        base.OnClosing(e);
    }

    // ---- the window's place, kept -----------------------------------------------------------

    private static PD2SPCompanion.Shell.Services.Settings.EditorSettings? Settings =>
        (Avalonia.Application.Current as App)?.Services?.GetService(typeof(PD2SPCompanion.Shell.Services.Settings.EditorSettings))
            as PD2SPCompanion.Shell.Services.Settings.EditorSettings;

    /// <summary>The window's place while it was last neither maximized nor minimized: what a maximized one comes back to.</summary>
    private PD2SPCompanion.Shell.Services.Settings.WindowPlace? _normal;

    private void GiveBackPlace()
    {
        if (!PD2SPCompanion.Shell.Services.Settings.EditorSettings.KeepsPlace || Settings is not { } settings)
            return;

        if (settings.Window is { } kept)
        {
            // A place that fills its screen is none to come back to from maximized: kept so by a version that took
            // the maximized window's own size for it (see NoteNormal), it is put right here.
            var place = Fills(kept) ? Roomy(kept) : kept;
            _normal = place with { Maximized = false };

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(place.X, place.Y);
            Width = Math.Max(MinWidth, place.Width);
            Height = Math.Max(MinHeight, place.Height);

            // Off every screen now - another monitor gone - it opens in the middle of the one there is.
            Opened += (_, _) =>
            {
                var middle = new PixelPoint(Position.X + (int)(Bounds.Width / 2), Position.Y + (int)(Bounds.Height / 2));
                if (Screens.ScreenFromPoint(middle) is null && Screens.Primary is { } screen)
                    Position = new PixelPoint(screen.WorkingArea.X + (screen.WorkingArea.Width - (int)Bounds.Width) / 2,
                                              screen.WorkingArea.Y + (screen.WorkingArea.Height - (int)Bounds.Height) / 2);
                if (place.Maximized)
                    WindowState = WindowState.Maximized;
            };
        }

        PositionChanged += (_, _) => NoteNormalSoon();
        Resized += (_, _) => NoteNormalSoon();
    }

    /// <summary>
    /// The window moved or changed its size: where it stands is noted as the place a maximized one comes back to -
    /// a turn later, and only if it is then a window of its own. As a window is maximized its size changes before
    /// its state says so, and asked at once the screen's whole size was kept as that place: maximized and closed,
    /// it opened next "a similar large resolution" with its edges off the screen, and "restore does nothing" - it
    /// restored to the size it had (one set of settings held 2048 x 1073.6 at 0,0, the screen's own).
    /// </summary>
    private void NoteNormalSoon() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(NoteNormal, Avalonia.Threading.DispatcherPriority.Background);

    private void NoteNormal()
    {
        if (WindowState != WindowState.Normal || !IsVisible)
            return;

        var now = new PD2SPCompanion.Shell.Services.Settings.WindowPlace(Position.X, Position.Y, Width, Height, false);
        if (!Fills(now))
            _normal = now;
    }

    /// <summary>The screen a place lies on, by its middle - or the first there is.</summary>
    private Avalonia.Platform.Screen? ScreenOf(PD2SPCompanion.Shell.Services.Settings.WindowPlace place)
    {
        try
        {
            double scale = Screens.Primary?.Scaling ?? 1;
            var middle = new PixelPoint(place.X + (int)(place.Width * scale / 2), place.Y + (int)(place.Height * scale / 2));
            return Screens.ScreenFromPoint(middle) ?? Screens.Primary;
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            return null;
        }
    }

    /// <summary>Whether a place is as large as its screen has room: no window of its own, but a maximized one's size.</summary>
    private bool Fills(PD2SPCompanion.Shell.Services.Settings.WindowPlace place)
    {
        if (ScreenOf(place) is not { } screen || screen.Scaling <= 0)
            return false;

        double wide = screen.WorkingArea.Width / screen.Scaling, tall = screen.WorkingArea.Height / screen.Scaling;
        return place.Width >= wide * 0.97 && place.Height >= tall * 0.9;
    }

    /// <summary>A window of its own on a place's screen, where the place itself is none: four fifths of the room, in its middle.</summary>
    private PD2SPCompanion.Shell.Services.Settings.WindowPlace Roomy(PD2SPCompanion.Shell.Services.Settings.WindowPlace place)
    {
        if (ScreenOf(place) is not { } screen || screen.Scaling <= 0)
            return place;

        var room = screen.WorkingArea;
        return new(room.X + room.Width / 10, room.Y + room.Height / 10,
                   room.Width * 0.8 / screen.Scaling, room.Height * 0.8 / screen.Scaling, place.Maximized);
    }

    private void KeepPlace()
    {
        if (!PD2SPCompanion.Shell.Services.Settings.EditorSettings.KeepsPlace || Settings is not { } settings)
            return;

        NoteNormal();
        if (_normal is { } normal)
            settings.Window = normal with { Maximized = WindowState == WindowState.Maximized };
        else if (WindowState == WindowState.Maximized)
            settings.Window = Roomy(new(Position.X, Position.Y, Width, Height, true));

        settings.Save();
    }
}
