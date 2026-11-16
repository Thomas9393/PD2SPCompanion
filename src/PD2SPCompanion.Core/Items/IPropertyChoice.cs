namespace PD2SPCompanion.Core.Items;

/// <summary>
/// One line of a property picker: what it reads as, the marks beside it, and the id where the list
/// has one. The search's composites (<see cref="PropertyOption"/>) and the save's own properties
/// (<see cref="StatChoice"/>) both answer it, so one picker draws either list.
/// </summary>
public interface IPropertyChoice
{
    /// <summary>What the property does, in the game's words where it has any.</summary>
    string Text { get; }

    /// <summary>A map's alone - see <see cref="StatCatalogue.IsMapOnly"/>.</summary>
    bool IsMapOnly { get; }

    /// <summary>What the list says about it beyond its name - obsolete, UI, engine - or nothing.</summary>
    string Note { get; }

    bool HasNote { get; }

    /// <summary>The id, as the right-hand column shows it ("stat 39"), or nothing where the list has none.</summary>
    string IdText { get; }
}
