namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The Extra page's left side: the mode switch and the rune wizard's button, which
/// opens its panel on the right page. The waypoints and the
/// map seed moved to the Character page. What the buttons show and open is the right page's
/// (<see cref="Tools"/>) - one view model under two views.
/// </summary>
public partial class VP_ViewModel_Extra_L : ViewModelBase
{
    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Extra_L() : this(new VP_ViewModel_Extra_R()) { }

    public VP_ViewModel_Extra_L(VP_ViewModel_Extra_R tools) => Tools = tools;

    /// <summary>The right page, whose panels these buttons open.</summary>
    public VP_ViewModel_Extra_R Tools { get; }
}
