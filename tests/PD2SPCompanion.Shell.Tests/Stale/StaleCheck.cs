using System.Collections;
using System.ComponentModel;
using System.Reflection;

using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.Tests.Stale;

/// <summary>
/// Finds what a kept page says differently after a change with nobody told: a getter that reads something the change
/// moved, whose name was not raised, so a view bound to it goes on showing the old answer until some later rebuild. The
/// tests before it checked hand-kept lists of names against hand-kept lists of names, which cannot find a name nobody
/// thought of (the mercenary's pencil, one such bug, was found by reading).
/// <para>
/// Made over the pages it starts from: <see cref="Began"/> walks each and the view models it holds, to
/// <see cref="Deepest"/>, takes every public answer of a plain type, and listens. <see cref="Ended"/> walks again and says
/// what changed where neither its name nor "" was heard. Only what is reached both times is compared: a row rebuilt is
/// another object, and the page that holds it has said so or its own list is what is wrong.
/// </para>
/// <para>
/// Work a hidden page owes (<see cref="WhenShown"/>) is done first both times, as showing the page would do it: left
/// owed, it reads as stale, and it is not. Between the steps the program has to be left its turn, so these are asked one
/// by one on its thread, not as one call.
/// </para>
/// </summary>
internal sealed class StaleCheck(IReadOnlyList<(string Name, object Page)> pages, IReadOnlyDictionary<string, string>? excluded = null)
{
    /// <summary>The page itself is 0: its editors are 1, their rows 2, what a row holds 3.</summary>
    public const int Deepest = 3;

    /// <summary>
    /// How much of one list is looked at. Rows of one kind ask the same questions, and the vault's cards and a page's tiles
    /// run to thousands.
    /// </summary>
    private const int MostOfAList = 64;

    private sealed class Reached(string path)
    {
        public string Path { get; } = path;
        public Dictionary<PropertyInfo, object?> Answers { get; } = [];
        public HashSet<string> Heard { get; } = [];
        public bool HeardAll { get; set; }
        public PropertyChangedEventHandler? Listener { get; set; }
    }

    private readonly Dictionary<object, Reached> _before = new(ReferenceEqualityComparer.Instance);

    private static readonly Dictionary<Type, PropertyInfo[]> Answering = [];
    private static readonly Dictionary<Type, PropertyInfo[]> Holding = [];

    private static readonly FieldInfo Owed = typeof(WhenShown).GetField("_owed", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Afresh = typeof(WhenShown).GetField("_afreshOnShow", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>What a page owes, done. To be followed by a turn of the program, before <see cref="Began"/> or <see cref="Ended"/>.</summary>
    public void Settle()
    {
        foreach (object model in Walk().Keys)
            SettleOf(model);
    }

    /// <summary>The answers as they are now, and listening from here.</summary>
    public void Began()
    {
        foreach (var (model, path) in Walk())
        {
            var reached = new Reached(path);
            foreach (var property in AnswersOf(model.GetType()))
                reached.Answers[property] = Ask(model, property);

            reached.Listener = (_, said) =>
            {
                if (string.IsNullOrEmpty(said.PropertyName))
                    reached.HeardAll = true;
                else
                    reached.Heard.Add(said.PropertyName);
            };
            ((INotifyPropertyChanged)model).PropertyChanged += reached.Listener;
            _before[model] = reached;
        }
    }

    /// <summary>What changed since <see cref="Began"/> with no word of it, one line each. Nobody is listened to after this.</summary>
    public List<Finding> Ended()
    {
        var found = new List<Finding>();
        var now = Walk();
        foreach (var (model, reached) in _before)
        {
            ((INotifyPropertyChanged)model).PropertyChanged -= reached.Listener;
            if (!now.ContainsKey(model) || reached.HeardAll)
                continue;

            foreach (var (property, was) in reached.Answers)
            {
                if (reached.Heard.Contains(property.Name) || excluded?.ContainsKey(Key(model, property)) == true)
                    continue;

                object? answer = Ask(model, property);
                if (!Equals(was, answer))
                    found.Add(new Finding(Key(model, property), reached.Path, Said(was), Said(answer)));
            }
        }

        _before.Clear();
        return found;
    }

    /// <summary>One answer that moved unheard: the key an exclusion names, how it was reached, and from what to what.</summary>
    public sealed record Finding(string Key, string Path, string Was, string Is);

    private static string Key(object model, PropertyInfo property) => $"{model.GetType().Name}.{property.Name}";

    private static string Said(object? answer) => answer switch
    {
        null => "null",
        string text => $"\"{(text.Length > 80 ? text[..80] + "..." : text)}\"".ReplaceLineEndings(" / "),
        _ => answer.ToString() ?? "null",
    };

    /// <summary>A getter that throws gives that as its answer: a change between throwing and not is a change.</summary>
    private static object? Ask(object model, PropertyInfo property)
    {
        try
        {
            return property.GetValue(model);
        }
        catch (TargetInvocationException thrown)
        {
            return $"threw {(thrown.InnerException ?? thrown).GetType().Name}";
        }
    }

    /// <summary>
    /// Everything reached from the pages, each once, with the shortest way to it. Level by level, not down each road in
    /// turn: a page met first at the end of another's road would have had nothing under it looked at.
    /// </summary>
    private Dictionary<object, string> Walk()
    {
        var reached = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        var next = new Queue<(object Model, string Path, int Depth)>();
        foreach (var (name, page) in pages)
        {
            if (reached.TryAdd(page, name))
                next.Enqueue((page, name, 0));
        }

        while (next.TryDequeue(out var at))
        {
            if (at.Depth == Deepest)
                continue;

            foreach (var (child, path) in Held(at.Model, at.Path))
            {
                if (reached.TryAdd(child, path))
                    next.Enqueue((child, path, at.Depth + 1));
            }
        }

        TypesReached = [.. reached.Keys.Select(model => model.GetType().Name).Distinct()];
        return reached;
    }

    /// <summary>The kinds of model the last walk reached, for a test to know it looked at what it meant to.</summary>
    public HashSet<string> TypesReached { get; private set; } = [];

    private static IEnumerable<(object Child, string Path)> Held(object model, string path)
    {
        foreach (var property in HoldsOf(model.GetType()))
        {
            switch (Ask(model, property))
            {
                case INotifyPropertyChanged child when IsOurs(child.GetType()):
                    yield return (child, $"{path}.{property.Name}");
                    break;

                case IEnumerable list:
                    int at = 0;
                    foreach (object? each in list)
                    {
                        if (at >= MostOfAList)
                            break;

                        if (each is INotifyPropertyChanged row && IsOurs(row.GetType()))
                            yield return (row, $"{path}.{property.Name}[{at}]");
                        at++;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// What a page owes is done now. One that reads everything again at each visit goes on owing it after: done here, it
    /// would be taken as done for a visit of some later test.
    /// </summary>
    private static void SettleOf(object model)
    {
        for (var type = model.GetType(); type is not null && IsOurs(type); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(WhenShown) || field.GetValue(model) is not WhenShown work)
                    continue;

                bool owed = (bool)Owed.GetValue(work)!;
                work.Settle();
                if (owed && (bool)Afresh.GetValue(work)!)
                    Owed.SetValue(work, true);
            }
        }
    }

    private static bool IsOurs(Type type) => type.Assembly == typeof(WhenShown).Assembly || type.Assembly == typeof(StaleCheck).Assembly;

    private static IEnumerable<PropertyInfo> Readable(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetMethod!.IsPublic && property.GetIndexParameters().Length == 0);

    /// <summary>The getters compared: a bool, a whole number, a double, a string, an enum, or one of them nullable.</summary>
    private static PropertyInfo[] AnswersOf(Type type)
    {
        if (!Answering.TryGetValue(type, out var found))
        {
            found = [.. Readable(type).Where(property => IsPlain(property.PropertyType))];
            Answering[type] = found;
        }

        return found;
    }

    private static bool IsPlain(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(bool) || type == typeof(int) || type == typeof(double) || type == typeof(string) || type.IsEnum;
    }

    /// <summary>The getters walked: another view model of the program's, or a list of them.</summary>
    private static PropertyInfo[] HoldsOf(Type type)
    {
        if (!Holding.TryGetValue(type, out var found))
        {
            found = [.. Readable(type).Where(property => !IsPlain(property.PropertyType) && Holds(property.PropertyType))];
            Holding[type] = found;
        }

        return found;
    }

    private static bool Holds(Type type)
    {
        if (typeof(INotifyPropertyChanged).IsAssignableFrom(type) && IsOurs(type))
            return true;

        return type.GetInterfaces().Append(type).Any(face =>
            face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            && typeof(INotifyPropertyChanged).IsAssignableFrom(face.GetGenericArguments()[0])
            && IsOurs(face.GetGenericArguments()[0]));
    }
}
