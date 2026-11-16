using Avalonia;
using Avalonia.Controls;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

/// <summary>
/// A character's card, as the Character page shows the open one (see the .axaml). The page sets
/// what it shows; the row under the rule is the page's own, through <see cref="Footer"/>.
/// </summary>
public partial class CharacterCardPanel : UserControl
{
    public static readonly StyledProperty<CharacterCard?> CardProperty =
        AvaloniaProperty.Register<CharacterCardPanel, CharacterCard?>(nameof(Card));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<CharacterCardPanel, string?>(nameof(Description));

    public static readonly StyledProperty<string?> LifeProperty =
        AvaloniaProperty.Register<CharacterCardPanel, string?>(nameof(Life));

    public static readonly StyledProperty<string?> ManaProperty =
        AvaloniaProperty.Register<CharacterCardPanel, string?>(nameof(Mana));

    public static readonly StyledProperty<string?> StaminaProperty =
        AvaloniaProperty.Register<CharacterCardPanel, string?>(nameof(Stamina));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<CharacterCardPanel, object?>(nameof(Footer));

    /// <summary>Who: the name, the figure, the badges and when it was played.</summary>
    public CharacterCard? Card { get => GetValue(CardProperty); set => SetValue(CardProperty, value); }

    /// <summary>"Level 86 Amazon" - the page's, since the Character page's level can move.</summary>
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public string? Life { get => GetValue(LifeProperty); set => SetValue(LifeProperty, value); }

    public string? Mana { get => GetValue(ManaProperty); set => SetValue(ManaProperty, value); }

    public string? Stamina { get => GetValue(StaminaProperty); set => SetValue(StaminaProperty, value); }

    /// <summary>The row under the rule; none, and there is no rule.</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    public CharacterCardPanel()
    {
        InitializeComponent();
    }
}
