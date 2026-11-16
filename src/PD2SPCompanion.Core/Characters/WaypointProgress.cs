using System.Reflection;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// One waypoint, as the bit the save actually holds.
/// <para>
/// Reading and writing go straight through to the model object, so a toggle here is already
/// in the save by the time it returns - there is no copy to push back afterwards.
/// </para>
/// </summary>
public sealed class Waypoint
{
    private readonly object _act;
    private readonly PropertyInfo _bit;
    private readonly bool _actReached;

    internal Waypoint(object act, PropertyInfo bit, string name, bool isTown, bool actReached)
    {
        _act = act;
        _bit = bit;
        Name = name;
        IsTown = isTown;
        _actReached = actReached;
    }

    public string Name { get; }

    /// <summary>
    /// The act's own town, which is always the first waypoint the act stores.
    /// <para>
    /// The game hands this one over on arrival and every other waypoint in the act is
    /// reached through it, so it can be granted but never taken away.
    /// </para>
    /// </summary>
    public bool IsTown { get; }

    public bool IsFound
    {
        get => _bit.GetValue(_act) is true;
        set => _bit.SetValue(_act, value);
    }

    /// <summary>
    /// A main camp is kept only where the character has actually been. Somewhere it has
    /// never set foot the waypoint was granted rather than earned, so taking it back is
    /// undoing an edit, not breaking a character. See <see cref="WaypointProgress"/> for
    /// how that is decided.
    /// </summary>
    public bool CanDisable => !IsTown || !_actReached;

    /// <summary>Grants it. True when that changed something.</summary>
    public bool Enable()
    {
        if (IsFound)
            return false;

        IsFound = true;
        return true;
    }

    /// <summary>Takes it back, where that is allowed. True when that changed something.</summary>
    public bool Disable()
    {
        if (!IsFound || !CanDisable)
            return false;

        IsFound = false;
        return true;
    }
}

/// <summary>One act's waypoints on one difficulty.</summary>
public sealed class ActWaypoints(string act, IReadOnlyList<Waypoint> points, bool reached)
{
    public string Act { get; } = act;
    public IReadOnlyList<Waypoint> Points { get; } = points;

    /// <summary>Whether the character has actually been here on this difficulty.</summary>
    public bool Reached { get; } = reached;

    public int Found => Points.Count(point => point.IsFound);
    public int Total => Points.Count;
    public bool IsComplete => Total > 0 && Found == Total;
    public string Tally => $"{Found} / {Total}";

    /// <summary>Grants every waypoint in the act. Returns how many were not already held.</summary>
    public int EnableAll()
    {
        int granted = 0;
        foreach (var point in Points)
        {
            if (point.Enable())
                granted++;
        }
        return granted;
    }

    /// <summary>
    /// Takes back every waypoint in the act that may be taken back. Returns how many went.
    /// </summary>
    public int DisableAll()
    {
        int removed = 0;
        foreach (var point in Points)
        {
            if (point.Disable())
                removed++;
        }
        return removed;
    }
}

/// <summary>One difficulty's waypoints, act by act.</summary>
public sealed class DifficultyWaypoints(string difficulty, IReadOnlyList<ActWaypoints> acts)
{
    public string Difficulty { get; } = difficulty;
    public IReadOnlyList<ActWaypoints> Acts { get; } = acts;

    public int Found => Acts.Sum(act => act.Found);
    public int Total => Acts.Sum(act => act.Total);
    public bool IsComplete => Total > 0 && Found == Total;
    public string Tally => $"{Found} / {Total}";

    public int EnableAll()
    {
        int granted = 0;
        foreach (var act in Acts)
            granted += act.EnableAll();
        return granted;
    }

    public int DisableAll()
    {
        int removed = 0;
        foreach (var act in Acts)
            removed += act.DisableAll();
        return removed;
    }
}

/// <summary>
/// Which waypoints a character has, read from the save's <c>WS</c> section.
/// <para>
/// The section holds three difficulties, each a run of bits in act order: nine in Act I,
/// nine in II, nine in III, three in IV and nine in V, thirty-nine in all. The model exposes
/// them as a property per waypoint, so both the order and the names come from the properties
/// themselves rather than a list here that could drift out of step with them.
/// </para>
/// </summary>
public static class WaypointProgress
{
    public static IReadOnlyList<DifficultyWaypoints> Read(D2S save)
    {
        var section = save.Waypoints;
        if (section is null)
            return [];

        return
        [
            OneDifficulty(L("Normal"), section.Normal, save.Quests?.Normal, save.Location?.Normal),
            OneDifficulty(L("Nightmare"), section.Nightmare, save.Quests?.Nightmare, save.Location?.Nightmare),
            OneDifficulty(L("Hell"), section.Hell, save.Quests?.Hell, save.Location?.Hell),
        ];
    }

    /// <summary>Takes back every waypoint that may be taken back, on every difficulty.</summary>
    public static int DisableAll(IEnumerable<DifficultyWaypoints> difficulties)
    {
        int removed = 0;
        foreach (var difficulty in difficulties)
            removed += difficulty.DisableAll();
        return removed;
    }

    /// <summary>Grants every waypoint on every difficulty. Returns how many were added.</summary>
    public static int EnableAll(IEnumerable<DifficultyWaypoints> difficulties)
    {
        int granted = 0;
        foreach (var difficulty in difficulties)
            granted += difficulty.EnableAll();
        return granted;
    }

    private static DifficultyWaypoints OneDifficulty(
        string name, WaypointsDifficulty? difficulty, QuestsDifficulty? quests, Location? where)
    {
        if (difficulty is null)
            return new DifficultyWaypoints(name, []);

        var acts = new List<ActWaypoints>();

        // Act by act, in the order the section stores them.
        var parts = new (string Label, object? Waypoints)[]
        {
            (L("Act I"), difficulty.ActI),
            (L("Act II"), difficulty.ActII),
            (L("Act III"), difficulty.ActIII),
            (L("Act IV"), difficulty.ActIV),
            (L("Act V"), difficulty.ActV),
        };

        for (int i = 0; i < parts.Length; i++)
            acts.Add(OneAct(parts[i].Label, parts[i].Waypoints, Reached(where, quests, i + 1)));

        return new DifficultyWaypoints(name, acts);
    }

    /// <summary>
    /// Whether the character has actually been to an act on a difficulty.
    /// <para>
    /// This is what decides whether a main camp may be taken back, so it errs towards
    /// saying yes: a wrong yes only leaves a waypoint locked that could have been removed,
    /// where a wrong no would offer to strip one the character is really using.
    /// </para>
    /// <para>
    /// A difficulty the character has never entered has reached nothing, even though the
    /// game sets Act I's waypoint on all three difficulties when the character is made.
    /// Beyond that: Act I is where every character starts; any act up to where it now
    /// stands was walked through to get there; and an act it has since left is remembered
    /// by that act's own quest flags.
    /// </para>
    /// </summary>
    private static bool Reached(Location? where, QuestsDifficulty? quests, int act)
    {
        if (where is not { Active: true })
            return false;

        if (act <= 1)
            return true;

        if (where.Act >= act)
            return true;

        return Touched(ActQuests(quests, act));
    }

    private static object? ActQuests(QuestsDifficulty? quests, int act) => act switch
    {
        1 => quests?.ActI,
        2 => quests?.ActII,
        3 => quests?.ActIII,
        4 => quests?.ActIV,
        5 => quests?.ActV,
        _ => null,
    };

    /// <summary>
    /// Any quest in the act showing any activity. The flags are read as a group rather
    /// than one being singled out: an act's Introduction word is empty even on a character
    /// standing in that act, so no single flag means "arrived".
    /// </summary>
    private static bool Touched(object? act)
    {
        if (act is null)
            return false;

        foreach (var property in act.GetType().GetProperties())
        {
            if (property.PropertyType != typeof(Quest))
                continue;

            if (property.GetValue(act) is Quest quest && AnyFlag(quest))
                return true;
        }

        return false;
    }

    private static bool AnyFlag(Quest quest)
    {
        foreach (var property in typeof(Quest).GetProperties())
        {
            if (property.PropertyType == typeof(bool) && property.GetValue(quest) is true)
                return true;
        }

        return false;
    }

    private static ActWaypoints OneAct(string label, object? waypoints, bool reached)
    {
        if (waypoints is null)
            return new ActWaypoints(label, [], reached);

        var points = new List<Waypoint>();

        foreach (var property in waypoints.GetType().GetProperties())
        {
            if (property.PropertyType != typeof(bool))
                continue;

            // The first bit of every act is its town.
            points.Add(new Waypoint(
                waypoints, property, Display(property.Name), points.Count == 0, reached));
        }

        return new ActWaypoints(label, points, reached);
    }

    /// <summary>Property names the model spells differently from the game.</summary>
    private static readonly Dictionary<string, string> Corrections = new()
    {
        ["RogueEncampement"] = "Rogue Encampment",
        ["DarkWoods"] = "Dark Wood",
    };

    /// <summary>Words the game leaves lowercase when they are not the first.</summary>
    private static readonly HashSet<string> Minor = ["Of", "The"];

    /// <summary>
    /// "HallsOfTheDeadLvl2" reads as "Halls of the Dead Level 2". The names are property
    /// names, so they arrive run together and abbreviated.
    /// </summary>
    private static string Display(string name)
    {
        if (Corrections.TryGetValue(name, out var corrected))
            return corrected;

        var words = Split(name);

        for (int i = 0; i < words.Count; i++)
        {
            if (words[i] == "Lvl")
                words[i] = "Level";
            else if (i > 0 && Minor.Contains(words[i]))
                words[i] = words[i].ToLowerInvariant();
        }

        return string.Join(" ", words);
    }

    /// <summary>Breaks a run-together name where a capital or a number starts.</summary>
    private static List<string> Split(string name)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();

        for (int i = 0; i < name.Length; i++)
        {
            bool boundary = i > 0
                && (char.IsUpper(name[i]) || (char.IsDigit(name[i]) && !char.IsDigit(name[i - 1])));

            if (boundary && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }

            word.Append(name[i]);
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return words;
    }
}
