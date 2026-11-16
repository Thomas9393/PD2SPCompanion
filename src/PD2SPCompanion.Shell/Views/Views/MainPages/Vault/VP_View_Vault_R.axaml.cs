using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Vault_R : UserControl
{
    public VP_View_Vault_R()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.VP_ViewModel_Vault_R page || ReferenceEquals(page, _statesOf))
                return;

            _statesOf = page;
            page.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName == nameof(ViewModels.VP_ViewModel_Vault_R.Page))
                    Front();
            };
            Front();
        };
    }

    private ViewModels.VP_ViewModel_Vault_R? _statesOf;

    /// <summary>
    /// The page's four states - Edit, Grail, the Forge, Search - laid out ahead in its own deck, as the main
    /// view's decks lay their pages out (PageDeck.Prebuild): each bookmark's first press found its state not
    /// yet built (the Forge's was 100 ms, measured). Once this page itself has been laid out.
    /// </summary>
    public void PrebuildStates() => States.Prebuild([EditState, GrailState, ForgeState, SearchState]);

    /// <summary>
    /// The state its bookmark shows put in front of the page's own deck, which fades from the one that was
    /// there as the main view's deck fades from page to page.
    /// </summary>
    private void Front()
    {
        if (_statesOf is not { } page)
            return;

        States.Content = page.IsGrail ? GrailState : page.IsForge ? ForgeState : page.IsSearch ? SearchState : EditState;
    }
}