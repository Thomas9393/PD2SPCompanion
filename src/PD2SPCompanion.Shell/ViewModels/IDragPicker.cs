namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A page whose items can be picked with a drag rectangle (DragPick): the vault's list, and the
/// inventory page's sections. What the rectangle does to the page goes through here, so each page
/// keeps its own picking rules - which items take a tick, and what a tick records.
/// <para>
/// There is no picking mode:
/// "picking" is two or more selected, and a Ctrl-click or a rectangle starts it on its own.
/// </para>
/// </summary>
public interface IDragPicker
{
    /// <summary>
    /// A drag, or a Ctrl- or Shift-click, has started: switch picking on, as Explorer selects
    /// without a mode. False refuses it. With
    /// <paramref name="keepSelected"/>, the item selected the ordinary way is ticked as picking
    /// comes on, as Explorer's Ctrl+click adds to the selection there is.
    /// </summary>
    bool BeginDragPick(bool keepSelected);

    /// <summary>Whether an item is picked, or null for one that takes no tick.</summary>
    bool? IsPicked(object item);

    void SetPicked(object item, bool picked);

    /// <summary>Lets go of every pick on the page - a plain drag replaces them, Ctrl adds.</summary>
    void ClearPicks();

    /// <summary>
    /// A click on empty space: clear the selection, whatever its size, as a click on Explorer's
    /// empty space does.
    /// </summary>
    void ClickedEmpty();

    /// <summary>
    /// A Ctrl- or Shift-click, or a rectangle, has finished. What it left is the selection: one
    /// pick becomes the single selection and none clears it.
    /// </summary>
    void EndPick() { }

    /// <summary>
    /// A Ctrl- or Shift-click on an item that takes no pick: one the inventory lock keeps, a
    /// materials count, a folder being named. The press is kept from the item; the page says what it means.
    /// </summary>
    void RefusedPick(object item) { }

    /// <summary>
    /// A press on this item has moved past the threshold, with no modifier: carry it - and the
    /// rest of the picks, where it is one - rather than draw the rectangle. True when the page took the drag: carrying it, which
    /// it puts down on the release it hears itself, or refusing it outright. False leaves the
    /// drag to be a rectangle. A page that carries nothing says no.
    /// </summary>
    bool BeginCarry(object item) => false;

    /// <summary>
    /// A Shift+click's run, where the page knows it better than the lists drawn: every item from one to the other
    /// and the rest of their stretch, in reading order - the vault's whole folder, stretches not yet drawn among it
    /// (a run stopped at the edge of one 15-row stretch). Null leaves the run to the list the
    /// item is drawn in.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<object>? ReadingOrder(object from, object to) => null;
}
