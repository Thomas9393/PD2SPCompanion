using System;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A box that narrows a list in front of the player, the same wherever one stands (the
/// pickers keep their boxes, but with one wording): the magnifier at its left, the word
/// "Search" while it is empty, and a cross at its right that empties it, there while something is typed.
/// The grail's, the Forge's, the property, affix, rare name and skill pickers', the vaults list's in
/// Settings - and the window's own search box. Before, five said "Search", one "Search name or base", one
/// whatever its caller handed it, and none had the cross.
/// <para>
/// A text box in every other way - it takes the text box's styles and classes - so a page sets its size and
/// binds its text as it would one.
/// </para>
/// <para>
/// The cross is a bare glyph, no frame (a framed one stood taller than the box). What a
/// page puts in <see cref="Trailing"/> stands inside the box at its right end, after a rule: the window's
/// search box has the button to its panel there.
/// </para>
/// </summary>
public class SearchBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <summary>What stands inside the box at its right end, after the cross and a rule: a button of the box's own.</summary>
    public static readonly StyledProperty<object?> TrailingProperty =
        AvaloniaProperty.Register<SearchBox, object?>(nameof(Trailing));

    public object? Trailing
    {
        get => GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    /// <summary>
    /// What the cross does in place of emptying the box: the window's search box, standing in for a search
    /// that stands, ends that search with it. None, and the cross empties the box and leaves the caret in it.
    /// </summary>
    public static readonly StyledProperty<System.Windows.Input.ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<SearchBox, System.Windows.Input.ICommand?>(nameof(ClearCommand));

    public System.Windows.Input.ICommand? ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    /// <summary>The cross itself, for a page that names it for its tests.</summary>
    public Button ClearButton => _clear;

    private readonly Button _clear;
    private readonly Border _rule;
    private readonly ContentControl _trailing;

    public SearchBox()
    {
        PlaceholderText = L("Search");
        TextAlignment = TextAlignment.Left;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;

        var magnifier = new Path();
        magnifier.Classes.Add("magnifier");
        magnifier.Data = Glyph("[Glyph] Magnifier");
        InnerLeftContent = magnifier;

        var cross = new Path();
        cross.Classes.Add("pin");
        cross.Data = Glyph("[Glyph] Close");

        // Never the focus: a click on it leaves the pointer in the box, to type the next word.
        // Its room is kept while the box is empty - unseen and not to be clicked, not gone: coming and going
        // it moved what stands after it by a pixel (the separator too), and nothing moves when something appears.
        _clear = new Button { Content = cross, Margin = new Thickness(0, 0, 2, 0), Opacity = 0, IsHitTestVisible = false, Focusable = false,
                              VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        _clear.Classes.Add("bareicon");
        ToolTip.SetTip(_clear, L("Clear search"));
        AutomationProperties.SetAutomationId(_clear, "Box_Clear");
        _clear.Click += (_, _) =>
        {
            if (ClearCommand is { } other)
            {
                if (other.CanExecute(null))
                    other.Execute(null);
                return;
            }

            Text = "";
            Focus();
        };

        // The rule runs from the box's frame to its frame, in the frame's own colour: a part of it, parting the
        // box in two (it stood 4 short of each and paler).
        // Drawn as a border's edge, as the frame is, so it is the frame's thickness on any screen scale ("the
        // bar width should be the same as frame": a filled strip came out two pixels wide beside the frame's one).
        _rule = new Border { Width = 1, BorderThickness = new Thickness(1, 0, 0, 0), Margin = new Thickness(2, 0, 4, 0), IsVisible = false,
                             VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
        _rule[!Border.BorderBrushProperty] = this[!BorderBrushProperty];
        _trailing = new ContentControl { IsVisible = false, Margin = new Thickness(0, 0, 3, 0),
                                         VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        var end = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
                                   VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
        end.Children.Add(_clear);
        end.Children.Add(_rule);
        end.Children.Add(_trailing);
        InnerRightContent = end;
    }

    private static Geometry? Glyph(string key) =>
        Application.Current is { } app && app.TryFindResource(key, out object? found) ? found as Geometry : null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            bool typed = !string.IsNullOrEmpty(Text);
            _clear.Opacity = typed ? 1 : 0;
            _clear.IsHitTestVisible = typed;
        }

        // As tall as the box is inside its frame: what stands at the box's end is laid out no taller than its
        // tallest button, and the rule stopped short of the frame with it.
        if (change.Property == BoundsProperty || change.Property == BorderThicknessProperty || change.Property == PaddingProperty)
            _rule.Height = Math.Max(0, Bounds.Height - BorderThickness.Top - BorderThickness.Bottom - Padding.Top - Padding.Bottom);

        if (change.Property == TrailingProperty)
        {
            _trailing.Content = Trailing;
            _trailing.IsVisible = _rule.IsVisible = Trailing is not null;
        }
    }
}
