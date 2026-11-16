using System.Text.RegularExpressions;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// The mercenary, edited - within its type: level, name and kind may change, but swapping it to another type, an
/// act 1 mercenary to an act 5 one, is overkill and error prone. What a mercenary may wear is its type's, so a type changed under worn gear would leave items
/// in slots it cannot have; nothing here reaches across acts.
/// <para>
/// <b>Within a type:</b> the variant - its attack or aura; the name, from the type's own list; the level,
/// which the save keeps as experience (<see cref="MercenaryProfile.NeededFor"/>); and whether it is dead.
/// </para>
/// <para>
/// <b>A variant is offered once, not once a difficulty</b> (see
/// <see cref="MercenaryProfile.VariantText"/>): the table has a row id for the variant hired in Normal, in
/// Nightmare and in Hell, and they are the same mercenary at the same level. Choosing a variant writes the
/// Normal id - the one that needs the least experience a level. A mercenary hired in Nightmare or Hell keeps
/// its id until its variant is changed: nothing is rewritten by itself.
/// </para>
/// <para>
/// <b>Unverified, to be seen in game:</b> that <c>NameId</c> counts from the type's first name key
/// (<c>NameFirst</c>) - the save has only the number, and the editor did not show a name before this - and
/// that the game accepts a level under the row's own hiring level.
/// </para>
/// </summary>
public static class MercenaryEdit
{
    /// <summary>The highest level a mercenary reaches.</summary>
    public const int MostLevel = 98;

    /// <summary>A variant the mercenary may be made: its row's id, and how the editor names it.</summary>
    public sealed record Variant(int TypeId, string Label);

    private static bool Hired(D2S save) => save.Mercenary is { Id: not 0 };

    /// <summary>
    /// The variants of the mercenary's own type, each once by its name and skill, with the id of the one hired
    /// in Normal (the first of the name, the table's rows taken by difficulty); none with no mercenary.
    /// </summary>
    public static IReadOnlyList<Variant> VariantsOf(D2S save, GameData? game)
    {
        if (!Hired(save) || game is null)
            return [];

        bool expansion = save.Status.IsExpansion;
        return [.. Hirelings.SameAct(save.Mercenary.TypeId, expansion)
            .Select(id => new Variant(id, MercenaryProfile.Read(WithType(save, id), game).VariantText))
            .GroupBy(variant => variant.Label, StringComparer.OrdinalIgnoreCase)
            .Select(named => named.First())];
    }

    /// <summary>Which of <see cref="VariantsOf"/> the mercenary is - by its name, whichever difficulty's id it has; -1 with none.</summary>
    public static int VariantIndex(D2S save, GameData? game)
    {
        if (!Hired(save) || game is null)
            return -1;

        string mine = MercenaryProfile.Read(save, game).VariantText;
        var variants = VariantsOf(save, game);
        for (int at = 0; at < variants.Count; at++)
        {
            if (string.Equals(variants[at].Label, mine, StringComparison.OrdinalIgnoreCase))
                return at;
        }

        return -1;
    }

    /// <summary>The save's mercenary as if it were of another row: for naming a variant, nothing is written.</summary>
    private static D2S WithType(D2S save, int typeId) => new()
    {
        Status = save.Status,
        Mercenary = new Mercenary
        {
            Id = save.Mercenary.Id, NameId = save.Mercenary.NameId, TypeId = (ushort)typeId,
            Experience = save.Mercenary.Experience, IsDead = save.Mercenary.IsDead,
        },
    };

    /// <summary>
    /// The names the mercenary's type draws from, in the order the save numbers them: the string keys from the
    /// row's <c>NameFirst</c> to its <c>NameLast</c> ("merc01" .. "merc41"). A key the tables have no text for is
    /// given by its key. Empty with no mercenary or no such row.
    /// </summary>
    public static IReadOnlyList<string> NamesOf(D2S save, GameData? game)
    {
        if (!Hired(save) || game is null
            || Hirelings.NameKeys(save.Mercenary.TypeId, save.Status.IsExpansion) is not { } keys)
            return [];

        var first = Regex.Match(keys.First, @"^(.*?)(\d+)$");
        var last = Regex.Match(keys.Last, @"^(.*?)(\d+)$");
        if (!first.Success || !last.Success
            || !first.Groups[1].Value.Equals(last.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            return [];

        var strings = StringTable.Current ?? StringTable.Load(game);
        string stem = first.Groups[1].Value;
        int width = first.Groups[2].Value.Length;
        int from = int.Parse(first.Groups[2].Value), to = int.Parse(last.Groups[2].Value);

        var names = new List<string>();
        for (int n = from; n <= to && names.Count < 512; n++)
        {
            string key = stem + n.ToString().PadLeft(width, '0');
            names.Add(strings.Get(key) ?? key);
        }

        return names;
    }

    /// <summary>The mercenary's name, where its number is within its type's list; "" otherwise.</summary>
    public static string NameOf(D2S save, GameData? game) =>
        NamesOf(save, game) is { Count: > 0 } names && save.Mercenary.NameId < names.Count ? names[save.Mercenary.NameId] : "";

    /// <summary>
    /// Makes the mercenary another variant of its own type, at the level it has: the new row counts a level
    /// in its own experience (<c>Exp/Lvl</c> differs by difficulty), so the experience is set to what that
    /// level needs there. Null when done, else why not.
    /// </summary>
    public static string? SetVariant(D2S save, GameData? game, int typeId)
    {
        if (!Hired(save))
            return L("No mercenary.");

        bool expansion = save.Status.IsExpansion;
        if (!Hirelings.SameAct(save.Mercenary.TypeId, expansion).Contains(typeId))
            return L("Not a variant of this mercenary.");

        int level = Math.Max(1, MercenaryProfile.Read(save, game).Level);
        save.Mercenary.TypeId = (ushort)typeId;
        return SetLevel(save, game, level);
    }

    /// <summary>Sets the mercenary's level - its experience, to what the level needs. Null when done, else why not.</summary>
    public static string? SetLevel(D2S save, GameData? game, int level)
    {
        if (!Hired(save))
            return L("No mercenary.");

        if (Hirelings.KindOf(save.Mercenary.TypeId, save.Status.IsExpansion) is not { ExperiencePerLevel: > 0 } kind)
            return L("Mercenary kind unknown.");

        level = Math.Clamp(level, 1, MostLevel);
        save.Mercenary.Experience = (uint)Math.Min(uint.MaxValue, MercenaryProfile.NeededFor(kind.ExperiencePerLevel, level));
        return null;
    }

    /// <summary>Gives the mercenary another name of its type's list, by its place in it. Null when done, else why not.</summary>
    public static string? SetName(D2S save, GameData? game, int index)
    {
        if (!Hired(save))
            return L("No mercenary.");

        if (index < 0 || index >= NamesOf(save, game).Count)
            return L("No such name.");

        save.Mercenary.NameId = (ushort)index;
        return null;
    }

    /// <summary>Brings a dead mercenary back. Null when done, else why not.</summary>
    public static string? Revive(D2S save)
    {
        if (!Hired(save))
            return L("No mercenary.");

        if (save.Mercenary.IsDead == 0)
            return L("Mercenary is alive.");

        save.Mercenary.IsDead = 0;
        return null;
    }
}
