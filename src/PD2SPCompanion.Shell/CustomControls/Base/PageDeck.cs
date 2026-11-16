using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A page that does something as it is shown or put away - what it did on being attached to and
/// detached from the window, when the page host swapped pages in and out (<see cref="PageDeck"/>).
/// </summary>
public interface IShownPage
{
    void OnShown();
    void OnHidden();
}

/// <summary>
/// The main view's two pages, each page's view kept once it has been shown and only hidden while another
/// is in front - not swapped out of the window as TransitioningContentControl did.
/// <para>
/// Swapped out, a page left the window, and coming back every control on it was styled again and the
/// whole page measured again: returning to the Items page with an item open cost 180-350 ms each time,
/// about half of it restyling (measured with a dotnet-trace of ten page changes: the UI thread in
/// ContentPresenter.UpdateChild -> OnAttachedToLogicalTree -> ApplyStyles, then a full measure pass).
/// Kept and hidden, a page keeps its styles and its layout.
/// </para>
/// <para>
/// What a page did on attach and detach it does through <see cref="IShownPage"/>. A GIF or a figure on a
/// hidden page is paused, so the render loop does not tick for what is not seen. The cross-fade is the
/// same <see cref="PageTransition"/>, and Settings' Animations off makes it instant (Motion).
/// </para>
/// </summary>
public sealed class PageDeck : Panel
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<PageDeck, object?>(nameof(Content));

    public static readonly StyledProperty<IDataTemplate?> ContentTemplateProperty =
        AvaloniaProperty.Register<PageDeck, IDataTemplate?>(nameof(ContentTemplate));

    public static readonly StyledProperty<IPageTransition?> PageTransitionProperty =
        AvaloniaProperty.Register<PageDeck, IPageTransition?>(nameof(PageTransition));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public IDataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public IPageTransition? PageTransition
    {
        get => GetValue(PageTransitionProperty);
        set => SetValue(PageTransitionProperty, value);
    }

    /// <summary>The page in front, or fading in.</summary>
    public Control? Shown => _shown;

    /// <summary>
    /// Every page built so far, with what it shows - in front or kept behind: for what reads the pages
    /// themselves, the window's search listing their sections and settings.
    /// </summary>
    public IEnumerable<(object Content, Control View)> Kept => System.Linq.Enumerable.Select(_pages, page => (page.Key, page.Value));

    private readonly Dictionary<object, Control> _pages = new(ReferenceEqualityComparer.Instance);
    private Control? _shown;
    private CancellationTokenSource? _fading;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty && VisualRoot is not null)
            _ = ShowAsync(change.NewValue);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_shown is null && Content is not null)
            _ = ShowAsync(Content);
    }

    /// <summary>
    /// Pages laid out ahead, in the deck's own size, and left hidden: built and styled the first time,
    /// laid out again where already built (a new character's content). Opening a character does this for
    /// the pages the character is mostly seen on (MainView.PrepareCharacterPages) - the wait goes into the
    /// open, where it is expected, and not into each page's first visit. A page is
    /// at no opacity only while it is laid out and hidden after: hidden takes no clicks.
    /// </summary>
    public void Prebuild(IEnumerable<object> contents)
    {
        var room = Bounds.Size;
        if (room.Width <= 0 || room.Height <= 0)
            return;
        foreach (var content in contents)
        {
            if (PageFor(content) is not { } page || ReferenceEquals(page, _shown))
                continue;
            page.Opacity = 0;
            page.IsVisible = true;
            if (!Children.Contains(page))
                Children.Add(page);
            Reclaim(page);
            page.Measure(room);
            page.Arrange(new Rect(room));
            page.IsVisible = false;
            page.Opacity = 1;
            // It may have attached as it was added, and a page acts on attaching as on being shown.
            (page as IShownPage)?.OnHidden();
        }
    }

    /// <summary>
    /// What a page's layout missed while it was hidden, asked for again as it comes into sight.
    /// <para>
    /// <b>A change inside a hidden page is lost to the layout.</b> A control that changes asks to be measured
    /// again; the layout pass that comes for it finds the page above it hidden and lets it go - still marked as
    /// waiting, and waiting for nothing: nobody will ask again, the mark being already set, and the controls above
    /// it were never told, a parent hearing of a child's new size only when the child is measured. So the page
    /// shown again measures from the top, finds every child "as it was", and stops.
    /// </para>
    /// <para>
    /// Seen as the Items page with no scrollbar over a mercenary's section running off its foot: the character closed for
    /// the game, a mercenary hired there, the character loaded again - the
    /// section had grown under a scroll area that still held the old height. Nothing but a change of the
    /// window's size put it right, which is how a minimized window hid it from the first tries at it.
    /// </para>
    /// <para>
    /// So, a page made visible: every control in it still marked as waiting has the way up to the page marked
    /// too, and the measure from the top goes down to it. Hidden parts are passed by - they wait rightly. A walk
    /// of the page's tree reading two flags a control; what is in order is not measured again.
    /// </para>
    /// </summary>
    private static void Reclaim(Control page)
    {
        void Walk(Visual node)
        {
            foreach (var child in node.GetVisualChildren())
            {
                if (child is Layoutable part)
                {
                    if (!part.IsVisible)
                        continue;

                    if (!part.IsMeasureValid || !part.IsArrangeValid)
                    {
                        for (Visual? up = node; up is not null; up = up.GetVisualParent())
                        {
                            (up as Layoutable)?.InvalidateMeasure();
                            if (ReferenceEquals(up, page))
                                break;
                        }
                    }
                }

                Walk(child);
            }
        }

        Walk(page);
    }

    /// <summary>The page built for a content, or null where none has been (a control is its own page).</summary>
    public Control? Built(object? content) =>
        content is Control control ? control : content is not null && _pages.TryGetValue(content, out var kept) ? kept : null;

    private Control? PageFor(object? content)
    {
        if (content is null)
            return null;
        if (content is Control control)
            return control;
        if (_pages.TryGetValue(content, out var kept))
            return kept;

        var template = ContentTemplate ?? this.FindDataTemplate(content);
        var built = template?.Build(content) ?? new TextBlock { Text = content.ToString() };
        built.DataContext = content;
        _pages[content] = built;
        return built;
    }

    private async Task ShowAsync(object? content)
    {
        var page = PageFor(content);
        if (ReferenceEquals(page, _shown))
            return;

        // A fade still running ends where it was going: only the page leaving now stays drawn.
        _fading?.Cancel();
        var leaving = _shown;
        foreach (var child in Children)
        {
            if (!ReferenceEquals(child, leaving) && !ReferenceEquals(child, page))
            {
                child.IsVisible = false;
                child.Opacity = 1;
                child.IsHitTestVisible = true;
            }
        }
        // The page leaving takes no clicks from now: through the fade both are drawn, and a page laid out
        // ahead (Prebuild) sits under the ones added after it - a click on the coming page's button went to
        // the page going (property-lines' Custom).
        if (leaving is not null)
        {
            leaving.Opacity = 1;
            leaving.IsHitTestVisible = false;
        }
        if (page is not null)
            page.IsHitTestVisible = true;

        _shown = page;
        (leaving as IShownPage)?.OnHidden();
        if (page is not null)
        {
            if (!Children.Contains(page))
            {
                page.IsVisible = false;
                Children.Add(page);
            }
            (page as IShownPage)?.OnShown();
        }

        var fading = _fading = new CancellationTokenSource();
        if (PageTransition is { } transition && leaving is not null && page is not null)
        {
            page.IsVisible = true;
            Reclaim(page);
            Paused();
            try
            {
                await transition.Start(leaving, page, forward: true, fading.Token);
            }
            catch (OperationCanceledException)
            {
            }
            if (fading.IsCancellationRequested)
                return;
        }

        if (leaving is not null)
        {
            leaving.IsVisible = false;
            leaving.Opacity = 1;
            leaving.IsHitTestVisible = true;
        }
        if (page is not null)
        {
            page.IsVisible = true;
            Reclaim(page);
            page.Opacity = 1;
        }
        Paused();
    }

    /// <summary>
    /// Only the pages shown, to UI Automation: a hidden page's controls stayed in the tree, off screen but
    /// found - a screen reader would read them, and a test took a hidden page's box for the shown one's
    /// (quest-complete). Told again at every change of page.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => _peer = new ShownPagesPeer(this);

    private ShownPagesPeer? _peer;

    private sealed class ShownPagesPeer(PageDeck owner) : ControlAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
        {
            var shown = new List<AutomationPeer>();
            foreach (var child in owner.Children)
            {
                if (child.IsVisible)
                    shown.Add(GetOrCreate(child));
            }
            return shown;
        }

        public void Changed() => InvalidateChildren();
    }

    /// <summary>Every figure out of sight paused, every one in sight playing; the tree told.</summary>
    private void Paused()
    {
        _peer?.Changed();
        AnimatedFrames.PauseHidden();
    }
}
