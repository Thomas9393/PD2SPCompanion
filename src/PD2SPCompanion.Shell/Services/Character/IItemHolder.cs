using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// A file an item can leave and come into: the character, or the shared stash. What <see cref="ItemLift"/> asks of each,
/// so a move across the two reads the same whichever way it goes.
/// </summary>
public interface IItemHolder
{
    /// <summary>Where an item of this file lies now; null where it holds none such.</summary>
    ItemOrigin? OriginOf(Item item);

    /// <summary>The item out of this file and handed back; null where the file will not let go of it.</summary>
    Item? Release(Item item, bool rebuild = true);

    /// <summary>An item from elsewhere taken in at a cell. Null when it came, else why not.</summary>
    string? Adopt(Item item, int page, int x, int y, bool rebuild = true, bool keepId = false);

    /// <summary>An item this file let go of, back as it lay, nothing asked. No rebuild.</summary>
    void PutBack(Item item, ItemOrigin origin);
}
