using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

using PD2SPCompanion.Core.Characters;

namespace PD2SPCompanion.Shell.Helper;

/// <summary>
/// A box a character's name is typed into takes what the game's own box takes, as it is typed: letters, and one
/// <c>-</c> or <c>_</c> (<see cref="CharacterRename.Typable"/>). A digit or a space pressed is simply not there, a
/// paste keeps only its letters. Set on the box: <c>helper:CharacterNameInput.IsOn="True"</c>.
/// <para>
/// On the tunnel: a box inserts what is typed in its own class handler, before any handler on the bubble.
/// </para>
/// </summary>
public static class CharacterNameInput
{
    public static readonly AttachedProperty<bool> IsOnProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsOn", typeof(CharacterNameInput));

    public static bool GetIsOn(TextBox box) => box.GetValue(IsOnProperty);

    public static void SetIsOn(TextBox box, bool value) => box.SetValue(IsOnProperty, value);

    static CharacterNameInput()
    {
        IsOnProperty.Changed.AddClassHandler<TextBox>((box, change) =>
        {
            if (change.NewValue is true)
            {
                box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
                box.AddHandler(TextBox.PastingFromClipboardEvent, OnPasting, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            }
            else
            {
                box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
                box.RemoveHandler(TextBox.PastingFromClipboardEvent, OnPasting);
            }
        });
    }

    /// <summary>The box's text less what the typing replaces: the selection, where there is one.</summary>
    private static string Kept(TextBox box)
    {
        string text = box.Text ?? "";
        int start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        return text.Remove(start, end - start);
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not TextBox box || e.Text is not { Length: > 0 } typed)
            return;

        string taken = CharacterRename.Typable(Kept(box), typed);
        if (taken.Length == 0)
            e.Handled = true;
        else if (taken != typed)
            e.Text = taken;
    }

    private static void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || e.Handled)
            return;

        e.Handled = true;
        _ = PasteTaken(box);
    }

    /// <summary>The clipboard's text as the box takes it, in place of the selection; held to the box's length.</summary>
    private static async Task PasteTaken(TextBox box)
    {
        if (TopLevel.GetTopLevel(box)?.Clipboard is not { } clipboard)
            return;

        string? pasted = await clipboard.TryGetTextAsync();
        if (pasted is not { Length: > 0 })
            return;

        string text = box.Text ?? "";
        int start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        string kept = text.Remove(start, end - start);

        string taken = CharacterRename.Typable(kept, pasted);
        if (box.MaxLength > 0)
            taken = taken[..Math.Min(taken.Length, Math.Max(0, box.MaxLength - kept.Length))];
        if (taken.Length == 0)
            return;

        box.Text = kept.Insert(start, taken);
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = start + taken.Length;
    }
}
