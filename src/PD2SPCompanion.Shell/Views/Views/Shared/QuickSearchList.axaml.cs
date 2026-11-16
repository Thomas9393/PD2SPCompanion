using Avalonia.Controls;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

/// <summary>The list under the window's search box (QuickSearchList.axaml).</summary>
public partial class QuickSearchList : UserControl
{
    public QuickSearchList()
    {
        InitializeComponent();

        // The row the arrows mark is kept in sight: the list is taller than its room once a few groups answer.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not QuickSearchViewModel search || ReferenceEquals(search, _wired))
                return;

            _wired = search;
            search.Marked += row =>
            {
                if (search.Rows.IndexOf(row) is >= 0 and var at)
                    List.ScrollIntoView(at);
            };
        };
    }

    private QuickSearchViewModel? _wired;
}
