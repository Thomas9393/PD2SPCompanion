using System.Collections;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>A list row whose bin asks twice: armed by the first press, drawn as the red tick by BinButton.</summary>
public interface IAsksTwice
{
    bool IsArmed { get; set; }
}

/// <summary>
/// The first press of a row's bin only asks, as every bin that deletes for good does (every preset delete,
/// then backups and map seeds - a preset went on one
/// click). Staged deletes are not asked about: UNDO takes those back.
/// </summary>
public static class AskTwice
{
    /// <summary>
    /// True where this press only asked - the caller deletes nothing yet; any other row asking is taken back.
    /// False on the second press, the row disarmed: delete it.
    /// </summary>
    public static bool First(IAsksTwice row, IEnumerable rows)
    {
        if (row.IsArmed)
        {
            row.IsArmed = false;
            return false;
        }

        foreach (var other in rows.OfType<IAsksTwice>())
            other.IsArmed = false;
        row.IsArmed = true;
        return true;
    }
}
