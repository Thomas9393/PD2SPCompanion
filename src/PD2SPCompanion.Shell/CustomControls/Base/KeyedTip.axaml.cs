using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Controls;

using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A tooltip's words, the lines that say what a key does drawn as caps (KeyedTip.axaml).
/// <para>
/// <b>Which lines are a key's.</b> The app's tooltips have said it one way since their first: a line of its own,
/// the keys, a colon, what they do - "Ctrl + LMB: 5 points", "Shift + Drag: select area". Such a line is found by
/// its keys - one or more of the names below, joined by " + " - and nothing else is touched: a tooltip with none
/// reads as it always did, a shortcut in brackets after a name ("Search (Ctrl + F)") stays in its sentence, and
/// a line in a language whose keys are named otherwise is left as words. So no tooltip was reworded for this, and
/// what UI Automation reads of a control - its tooltip's text - is the same text.
/// </para>
/// </summary>
public partial class KeyedTip : UserControl
{
    /// <summary>The tooltip's whole text, line by line.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<KeyedTip, string?>(nameof(Text));

    /// <summary>Words over <see cref="Text"/>, where a page has them apart: a name that is bound, over lines that are not.</summary>
    public static readonly StyledProperty<string?> LeadTextProperty =
        AvaloniaProperty.Register<KeyedTip, string?>(nameof(LeadText));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? LeadText
    {
        get => GetValue(LeadTextProperty);
        set => SetValue(LeadTextProperty, value);
    }

    /// <summary>The keys side by side on one line (KeyHints.Across).</summary>
    public static readonly StyledProperty<bool> AcrossProperty =
        AvaloniaProperty.Register<KeyedTip, bool>(nameof(Across));

    public bool Across
    {
        get => GetValue(AcrossProperty);
        set => SetValue(AcrossProperty, value);
    }

    public KeyedTip()
    {
        InitializeComponent();
        Show();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == LeadTextProperty)
            Show();

        if (change.Property == AcrossProperty)
            Keys.Across = Across;
    }

    private const string Key = @"(?:Ctrl|Shift|Alt|LMB|RMB|MMB|Drag|Tab|Enter|Esc|Space|Del|Wheel|F\d{1,2})";

    /// <summary>A line that says what keys do: keys joined by " + " - a letter or a digit may end them - then ": " and the rest.</summary>
    private static readonly Regex KeyLine = new($@"^({Key}(?: \+ {Key})*(?: \+ [A-Z0-9])?): (.+)$", RegexOptions.Compiled);

    /// <summary>Whether a tooltip's text has a line a key's: it is then drawn by this control, else as plain words.</summary>
    public static bool HasKeys(string? text) =>
        text is { Length: > 0 } && Lines(text).Any(line => KeyLine.IsMatch(line));

    private static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd());

    private void Show()
    {
        var lead = new List<string>();
        var keys = new List<TipHint>();
        var foot = new List<string>();

        if (LeadText is { Length: > 0 } over)
            lead.Add(over);

        foreach (string line in Lines(Text ?? ""))
        {
            if (KeyLine.Match(line) is { Success: true } found)
                keys.Add(new TipHint(found.Groups[1].Value, found.Groups[2].Value));
            else if (keys.Count == 0)
                lead.Add(line);
            else
                foot.Add(line);
        }

        Lead.Text = string.Join(Environment.NewLine, lead);
        Lead.IsVisible = lead.Count > 0;
        Keys.Hints = keys;
        Keys.IsVisible = keys.Count > 0;
        Keys.Margin = new Thickness(0, lead.Count > 0 ? 3 : 0, 0, foot.Count > 0 ? 3 : 0);
        Foot.Text = string.Join(Environment.NewLine, foot);
        Foot.IsVisible = foot.Count > 0;
    }
}
