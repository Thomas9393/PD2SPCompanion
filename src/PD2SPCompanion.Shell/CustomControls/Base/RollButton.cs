using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Shell.Imaging;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>What a roll button rolls - the one meaning each picture has across the editor.</summary>
public enum RollKind
{
    /// <summary>A new number for one stat, within its range: the flat die beside the steppers.</summary>
    Dice,

    /// <summary>New numbers for every stat, what is there kept: the Demonic Cube.</summary>
    Cube,

    /// <summary>A new random choice - an affix, a corruption, a skill, a bonus: the Flawless Skull.</summary>
    Flawless,

    /// <summary>Every choice made again at random: the Perfect Skull.</summary>
    Perfect,

    /// <summary>
    /// An item not yet anyone's made as the game would make it anew - the Forge's roll: the Horadrim Orb (the
    /// game's item that rerolls a rare map).
    /// </summary>
    Orb,
}

/// <summary>
/// Every button that rolls something at random, its picture said by what it rolls: a die is a
/// random roll of the given stat, the demonic cube rerolls all of the given stats, a flawless skull chooses a random
/// affix or stat, a perfect skull all random affixes or stats. Drawn by hand in each view, five came out as dice
/// that chose rather than numbered; this draws the picture itself, and PD2UI012 fails a roll command bound to
/// anything else.
/// <code>&lt;base:RollButton Kind="Flawless" Command="{Binding RollCommand}" ToolTip.Tip="{L 'Random corruption'}"/&gt;</code>
/// The die is the flat glyph, one of the stepper family it stands in; the other three are the game's own items,
/// read from the install as every picture is.
/// </summary>
public sealed class RollButton : Button
{
    public static readonly StyledProperty<RollKind> KindProperty =
        AvaloniaProperty.Register<RollButton, RollKind>(nameof(Kind));

    public RollKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Every Button style reaches it: spend, idle, the theme.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    public RollButton()
    {
        Classes.Add("spend");
        ToolTip.SetPlacement(this, PlacementMode.Bottom);
        ToolTip.SetVerticalOffset(this, 6);
        Draw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
            Draw();
    }

    /// <summary>Drawn again on being shown: the game's pictures can only be read once the install is.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Kind != RollKind.Dice && Content is Image { Source: null })
            Draw();
    }

    private void Draw()
    {
        Classes.Set("dice", Kind == RollKind.Dice);

        if (Kind == RollKind.Dice)
        {
            var die = new Path();
            die.Classes.Add("pin");
            die.Classes.Add("filled");
            if (Application.Current?.TryFindResource("[Glyph] Dice", out object? glyph) == true && glyph is Geometry geometry)
                die.Data = geometry;
            Content = die;
            return;
        }

        // The cube is drawn a little smaller, as it always was: its picture fills more of its square than a skull's.
        (string code, double size) = Kind switch
        {
            RollKind.Cube => ("invimrn", 15d),
            RollKind.Orb => ("invrera", 18d),
            RollKind.Flawless => ("invskl", 18d),
            _ => ("invskz", 18d),
        };

        Content = new Image
        {
            Source = SpriteBitmap.From(SpriteLoader.Current?.Load(code)),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
        };
    }
}
