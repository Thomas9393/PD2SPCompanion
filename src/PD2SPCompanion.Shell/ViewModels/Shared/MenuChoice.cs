using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// What the <c>SortMenu</c> button opens a menu of: some lines, a bullet beside the ones in force,
/// and a pick. A sort order is one of these (<see cref="SortChoice"/>); a filter is another.
/// <para>
/// One base rather than a second control for filters: the menu, its frame, its rows and its
/// "keep the button lit while open" are the same thing whatever the lines mean.
/// </para>
/// </summary>
public abstract class MenuChoice : ObservableObject
{
    /// <summary>The lines of the menu. A line with <see cref="SortOption.StartsGroup"/> has a rule above it.</summary>
    public abstract IReadOnlyList<SortOption> Options { get; }

    /// <summary>The button's tooltip: what it is, then what is in force.</summary>
    public abstract string Hint { get; }

    /// <summary>One line picked from the menu.</summary>
    public abstract void Pick(SortOption option);
}
