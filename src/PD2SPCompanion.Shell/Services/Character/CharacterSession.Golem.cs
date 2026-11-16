using D2SLib.Model.Save;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Character;

// The Iron Golem: the save keeps it standing, with the item it was made of.
public sealed partial class CharacterSession
{
    /// <summary>
    /// The item a standing Iron Golem was made of, as a tile: shown beside the mercenary,
    /// chosen and edited as any item is. Null where none stands. It is in neither item
    /// list - the save keeps it with the golem - so it is not lifted, stored or deleted: dismissing
    /// the golem (<see cref="DismissGolem"/>) is what hands it back.
    /// </summary>
    public ItemTileViewModel? GolemTile { get; private set; }

    public bool HasGolem => Skills?.GolemItem is not null;

    /// <summary>Whether this very item is the one the Iron Golem was made of.</summary>
    public bool IsGolemItem(Item item) => ReferenceEquals(Skills?.GolemItem, item);

    /// <summary>What a gesture that would take the golem's item away is answered with.</summary>
    public static string GolemKeepsItem => L("Require Iron Golem dismissed.");

    /// <summary>
    /// Why the Iron Golem cannot be dismissed, or null: its item has nowhere to go on the character.
    /// Null too where no golem stands. Asked before the points come out of its skill.
    /// </summary>
    public string? GolemHasNoRoom() =>
        Skills?.GolemItem is { } item && RoomFor(item, []).Page == 0
            ? L($"Require room for the Iron Golem's {ItemPresenter.Describe(item).Name}.")
            : null;

    /// <summary>
    /// Dismisses the Iron Golem on the player's word (the mercenary section's button): the golem gone
    /// from the save, the item it was made of on the character - the inventory, the cube, the stash,
    /// the first with room. Refused where none has room. Returns why not, or null.
    /// </summary>
    public string? DismissGolem()
    {
        if (Skills is not { GolemItem: not null })
            return null;

        if (GolemHasNoRoom() is { } refused)
            return Status = refused;

        string said = SendGolemHome();
        MarkEdited();
        Rebuild();
        Status = said;
        return null;
    }

    /// <summary>
    /// After skill points came out: an Iron Golem left without its skill is dismissed, and the item
    /// it was made of put on the character. The game refuses a save whose golem has no skill ("bad
    /// hireables": a Necromancer respecced here with its golem up). An item worn
    /// that gives the skill keeps it (SkillEditor.HasGolemSkill). Says what it did, or nothing.
    /// </summary>
    public string DismissUnskilledGolem()
    {
        // A golem that opened without a skill this reckoning can see is the game's, and left alone (SettleGolem).
        if (!_golemHadSkill || Skills is not { GolemStandsUnskilled: true })
            return "";

        string said = SendGolemHome();
        Rebuild();
        return said;
    }

    /// <summary>
    /// Whether the golem had its skill when last looked at: when the character was opened, and after
    /// each edit. An edit that takes the skill away is what dismisses it - a golem that opens without
    /// one this reckoning can see is left alone: the game made it, and knows a source this does not.
    /// </summary>
    private bool _golemHadSkill;

    /// <summary>Where the skills are read anew - a character opened, an undo: how the golem stands now.</summary>
    private void LookAtGolem() => _golemHadSkill = Skills?.HasGolemSkill() == true;

    /// <summary>A golem that lost its skill to an edit and has not been dismissed: its item has no room yet.</summary>
    private bool GolemAwaitsRoom => _golemHadSkill && Skills is { GolemItem: not null } skills && !skills.HasGolemSkill();

    /// <summary>
    /// After every edit (MarkEdited): an Iron Golem whose skill the edit took away is dismissed and its
    /// item put on the character - the item that gave the skill came off, PD2's
    /// Metalgrid say, with no point in the skill itself. In game the golem falls as the item comes off.
    /// Without room for its item it stands until an edit makes some, and the character is not saved
    /// meanwhile (Prepare): the game refuses a golem with no skill.
    /// </summary>
    private void SettleGolem()
    {
        if (Skills is not { } skills)
            return;

        bool has = skills.HasGolemSkill();
        if (skills.GolemItem is null || has || !_golemHadSkill)
        {
            _golemHadSkill = has;
            return;
        }

        if (GolemHasNoRoom() is { } refused)
        {
            Status = refused;
            return;
        }

        string said = SendGolemHome();
        _golemHadSkill = false;
        Rebuild();
        Status = said;
    }

    /// <summary>
    /// Why a worn item cannot come off, or null: it is what gives the Iron Golem its skill, the golem
    /// goes with it, and the golem's item has nowhere to go.
    /// </summary>
    private string? GolemKeepsOn(Item worn) => GolemGoesWith(worn) ? GolemHasNoRoom() : null;

    /// <summary>Whether a worn item is what gives the standing Iron Golem its skill: the golem goes when it comes off.</summary>
    public bool GolemGoesWith(Item worn) =>
        Skills is { GolemItem: not null } skills && skills.HasGolemSkill() && !skills.HasGolemSkill(without: worn);

    /// <summary>
    /// Whether the items this save takes away leave the standing Iron Golem without its skill. A store is
    /// settled only by the save, after <see cref="SettleGolem"/> has had its say - written, the file held a
    /// golem with no skill, which the game refuses.
    /// </summary>
    private bool GolemLeavesWith(System.Collections.Generic.IReadOnlyCollection<Item> leaving) =>
        leaving.Count > 0 && Skills is { GolemItem: not null } skills && skills.HasGolemSkill() && !skills.HasGolemSkill(leaving);

    /// <summary>The golem out of the save and its item into the first container with room. No rebuild.</summary>
    private string SendGolemHome()
    {
        if (Skills is not { } skills || _save?.PlayerItemList is not { } list)
            return "";

        var item = skills.GolemItem;
        (byte page, (int X, int Y) cell) = item is null ? ((byte)0, (0, 0)) : RoomFor(item, []);

        // Never without room for its item: dismissed then, the item was gone with the golem (a golem
        // left unskilled by a respec, the character's containers full).
        if (item is not null && page == 0)
            return GolemHasNoRoom() ?? "";

        skills.DismissGolem();
        if (item is null)
            return L("Iron Golem dismissed.");

        ItemMover.Place(item, page, cell.X, cell.Y);
        list.Items.Add(item);
        list.Count = (ushort)list.Items.Count;
        return L($"Iron Golem dismissed: {ItemPresenter.Describe(item).Name} in {NameOfPage(page).ToLowerInvariant()}.");
    }
}
