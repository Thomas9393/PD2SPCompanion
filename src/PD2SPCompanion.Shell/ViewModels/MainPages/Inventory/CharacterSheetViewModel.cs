using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Shell.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A piece of a sheet line in one colour - "Fire Resist: " in gold, "85" in red - and, for a number, what it is
/// made of, said under the pointer: hovering over a stat tells the sources that contribute to it. Changed in
/// place when the sheet is worked out again, so a number that moved is
/// all that is drawn again (<see cref="CharacterSheetViewModel.Update"/>).
/// </summary>
public sealed partial class SheetSegment(string text, IBrush brush, string? tip = null) : ObservableObject
{
    [ObservableProperty] private string _text = text;
    [ObservableProperty] private IBrush _brush = brush;
    [ObservableProperty] private string? _tip = tip;

    internal void Take(SheetSegment from)
    {
        Text = from.Text;
        Brush = from.Brush;
        Tip = from.Tip;
    }
}

/// <summary>
/// One line of the sheet as the game's page lays it: a left part, and a right part - the second column (Cast Rate
/// | Block Rate). A gap line is the space between the page's groups. Its pieces changed in place.
/// </summary>
public sealed partial class SheetLine(IEnumerable<SheetSegment> left, IEnumerable<SheetSegment> right, bool isGap = false) : ObservableObject
{
    public ObservableCollection<SheetSegment> Left { get; } = [.. left];
    public ObservableCollection<SheetSegment> Right { get; } = [.. right];
    public bool IsGap { get; } = isGap;

    public bool HasRight => Right.Count > 0;
    public int LeftSpan => HasRight ? 1 : 2;

    internal void Take(SheetLine from)
    {
        bool hadRight = HasRight;
        CharacterSheetViewModel.Merge(Left, from.Left, (_, _) => true, (mine, theirs) => mine.Take(theirs));
        CharacterSheetViewModel.Merge(Right, from.Right, (_, _) => true, (mine, theirs) => mine.Take(theirs));
        if (hadRight != HasRight)
        {
            OnPropertyChanged(nameof(HasRight));
            OnPropertyChanged(nameof(LeftSpan));
        }
    }
}

/// <summary>
/// One thing in the buff row above the sheet:
/// its icon, and under the pointer what it is and where it is from. What is up for good is shown and not pressed; a
/// cast is pressed on and off, and stands faint while off. Changed in place while it is the same buff.
/// </summary>
public sealed partial class BuffChip(Buff buff, Bitmap? icon, string tip) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Toggles))]
    [NotifyPropertyChangedFor(nameof(IsFixed))]
    [NotifyPropertyChangedFor(nameof(IsOff))]
    [NotifyPropertyChangedFor(nameof(Initials))]
    private Buff _buff = buff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initials))]
    private Bitmap? _icon = icon;

    [ObservableProperty] private string _tip = tip;

    /// <summary>Pressed on and off - a cast, a form, an aura of the Paladin's own, a spirit.</summary>
    public bool Toggles => Buff.Toggles;

    public bool IsFixed => !Buff.Toggles;

    /// <summary>Faint: off, or waiting for the form it needs.</summary>
    public bool IsOff => !Buff.On;

    /// <summary>The name's first letters, where the game has no icon for it.</summary>
    public string Initials => Icon is null ? string.Concat(Buff.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0])) : "";

    internal void Take(BuffChip from)
    {
        Buff = from.Buff;
        Icon = from.Icon;
        Tip = from.Tip;
    }
}

/// <summary>
/// A skill one of the two skill buttons may hold: its icon and its name, which hand's list it is in, and whether that
/// hand holds it now (lit in the list, as a picker's current row).
/// </summary>
public sealed record SkillChoice(int Id, string Name, Bitmap? Icon, bool Right, bool IsHeld)
{
    /// <summary>The name, which a box's typing finds it by and a screen reader says.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// The character's sheet laid out as the game's own Advanced Stats page - its lines, its order, its colours - so
/// anyone who knows the game's reads it at a glance. The numbers are
/// <see cref="CharacterSheet"/>'s, every one matched against the game's page; each says what it is made of under
/// the pointer.
/// <para>
/// One a section, kept: a sheet worked out again is merged into it (<see cref="Update"/>) - each line, piece and
/// buff changed in place - and not made anew. Made anew at every change, the section was taken down and laid out
/// whole: 60-75 ms of a buff pressed, 40-140 ms added to every edit of the character (measured).
/// </para>
/// </summary>
public sealed partial class CharacterSheetViewModel : ObservableObject
{
    // The page's colours: the names and the values in the body's colour (white - the game's
    // gold names read as a wall of gold here), grey for the small words inside a line, and
    // each element in its own as the game writes its text - fire red, cold blue, lightning yellow, poison green,
    // magic orange. The item palette's reds and blues are washes, too dark to read as text on the section.
    private static IBrush Brush(string key, IBrush fallback) => RarityPalette.Resolve(key) ?? fallback;

    private static readonly IBrush Plain = Brush("[Text] Body", Brushes.White);
    private static readonly IBrush Dim = Brush("[Text] Muted", Brushes.Gray);
    private static readonly IBrush Fire = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x5C));
    private static readonly IBrush Cold = new SolidColorBrush(Color.FromRgb(0x84, 0x84, 0xFF));
    private static readonly IBrush Lightning = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x64));
    private static readonly IBrush Poison = new SolidColorBrush(Color.FromRgb(0x40, 0xD8, 0x40));
    private static readonly IBrush Magic = new SolidColorBrush(Color.FromRgb(0xFF, 0xA8, 0x00));

    public CharacterSheetViewModel(Action<bool, int>? chooseSkill = null)
    {
        PickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<SkillChoice>(choice =>
        {
            if (choice is { IsHeld: false })
                chooseSkill?.Invoke(choice.Right, choice.Id);
        });
    }

    /// <summary>The sheet the section shows; its two skill lists are made from it when one is opened.</summary>
    private CharacterSheet? _sheet;

    private bool _rightStale = true, _leftStale = true;

    /// <summary>
    /// A sheet worked out again, merged into what the section shows: a line, a piece, a buff that is the same is
    /// changed in place, so what did not move is not drawn again. The skill lists are left until one is opened.
    /// </summary>
    public void Update(CharacterSheet sheet)
    {
        _sheet = sheet;
        Merge(Lines, LinesOf(sheet), (mine, theirs) => mine.IsGap == theirs.IsGap, (mine, theirs) => mine.Take(theirs));

        bool hadBuffs = HasBuffs;
        Merge(Buffs, [.. sheet.Buffs.Select(buff => new BuffChip(buff, IconOf(buff.Face, sheet.ClassId), TipOf(buff, sheet)))],
              (mine, theirs) => mine.Buff.Key == theirs.Buff.Key, (mine, theirs) => mine.Take(theirs));
        if (hadBuffs != HasBuffs)
            OnPropertyChanged(nameof(HasBuffs));

        HasSkillBoxes = sheet.RightChoices.Count > 0;
        RightChosen = Chosen(sheet, right: true, RightChosen);
        LeftChosen = Chosen(sheet, right: false, LeftChosen);
        _rightStale = _leftStale = true;
    }

    /// <summary>What a hand holds, as its button shows it - the one it showed, while it is the same skill.</summary>
    private static SkillChoice? Chosen(CharacterSheet sheet, bool right, SkillChoice? was)
    {
        var held = right ? sheet.RightSkill : sheet.LeftSkill;
        if (held is null)
            return null;
        if (was?.Id == held.Id)
            return was;

        var named = (right ? sheet.RightChoices : sheet.LeftChoices).FirstOrDefault(skill => skill.Id == held.Id);
        return new SkillChoice(held.Id, named?.Name ?? held.Name, IconOf(held, sheet.ClassId), right, IsHeld: true);
    }

    /// <summary>
    /// A hand's list made, as its button opens it: from the sheet it shows, once a
    /// sheet - every skill with its icon, the held one lit. Made at every change, the two lists were most of the icons
    /// the section drew for nobody.
    /// </summary>
    public void PrepareChoices(bool right)
    {
        if (_sheet is not { } sheet || !(right ? _rightStale : _leftStale))
            return;

        var held = right ? sheet.RightSkill : sheet.LeftSkill;
        var made = (right ? sheet.RightChoices : sheet.LeftChoices)
            .Select(skill => new SkillChoice(skill.Id, skill.Name, SkillRows.Of(skill.Id) is { } row ? IconOf(row, sheet.ClassId) : null,
                                             right, skill.Id == held?.Id))
            .ToList();
        Merge(right ? RightChoices : LeftChoices, made, (mine, theirs) => mine == theirs, (_, _) => { });
        if (right)
            _rightStale = false;
        else
            _leftStale = false;
    }

    /// <summary>
    /// What a list shows, brought to what it should: an item that is the same (<paramref name="same"/>) is kept and
    /// changed in place (<paramref name="take"/>); one that is not is put in its place; the rest added or taken off
    /// the end. A list's items are made again only where they differ.
    /// </summary>
    internal static void Merge<T>(IList<T> mine, IReadOnlyList<T> theirs, Func<T, T, bool> same, Action<T, T> take)
    {
        for (int at = 0; at < theirs.Count; at++)
        {
            if (at >= mine.Count)
                mine.Add(theirs[at]);
            else if (same(mine[at], theirs[at]))
                take(mine[at], theirs[at]);
            else
                mine[at] = theirs[at];
        }

        while (mine.Count > theirs.Count)
            mine.RemoveAt(mine.Count - 1);
    }

    /// <summary>The page's lines for a sheet, in the game's order.</summary>
    private static List<SheetLine> LinesOf(CharacterSheet sheet)
    {
        var lines = new List<SheetLine>();
        void Line(IReadOnlyList<SheetSegment> left, IReadOnlyList<SheetSegment>? right = null) => lines.Add(new SheetLine(left, right ?? []));
        void Gap() => lines.Add(new SheetLine([], [], isGap: true));

        // Every difficulty on the line, Normal, Nightmare and Hell, rather than three buttons for it.
        Line(Resist(L("Fire Resist: "), sheet, "fire", Fire, null));
        Line(Resist(L("Cold Resist: "), sheet, "cold", Cold, [Of(sheet.ColdLengthSources, Plain), Value("%")]));
        Line(Resist(L("Lightning Resist: "), sheet, "lightning", Lightning, null));
        Line(Resist(L("Poison Resist: "), sheet, "poison", Poison,
                    [Of(sheet.PoisonLengthSources(0), Plain), Small(" / "), Of(sheet.PoisonLengthSources(1), Plain), Small(" / "), Of(sheet.PoisonLengthSources(2), Plain), Value("%")]));
        Line([Name(L("Curse Resist: ")), Of(sheet.CurseResist, Magic, Math.Min(sheet.CurseResist.Value, 75)), Value(" / 75"),
              Name(L("  Length: ")), Of(sheet.CurseLengthSources, Plain), Value("%")]);
        Gap();

        Line([Name(L("Absorption: ")),
              .. Pair(sheet, "item_absorbfire", "item_absorbfire_percent", Fire), Value(" "),
              .. Pair(sheet, "item_absorbcold", "item_absorbcold_percent", Cold), Value(" "),
              .. Pair(sheet, "item_absorblight", "item_absorblight_percent", Lightning), Value(" "),
              .. Pair(sheet, "item_absorbmagic", "item_absorbmagic_percent", Magic)]);
        Line([Name(L("Damage Reduction: ")),
              .. Pair(sheet, "normal_damage_reduction", "damageresist", Plain), Value(" "),
              .. Pair(sheet, "magic_damage_reduction", "magicresist", Magic)]);
        Line([Name(L("Attacker Takes Damage: ")), Stat(sheet, "item_attackertakesdamage", Plain), Value("  "),
              Stat(sheet, "item_attackertakeslightdamage", Lightning)]);
        Gap();

        // Beside them, what the game's page leaves out: the enhanced damage that is not the
        // weapon's own - charms, jewels and the rest, which raise every weapon's damage. Physical pierce, beside
        // elemental pierce, the Season 14 page has on a line of its own.
        Line([Name(L("Elemental Mastery: ")), .. Elements(sheet, ["passive_fire_mastery", "passive_cold_mastery", "passive_ltng_mastery", "passive_pois_mastery", "passive_mag_mastery"])],
             [Name(L("Non-Weapon ED: ")), Stat(sheet, "item_maxdamage_percent", Plain), Value("%")]);
        // Pierce beside pierce. Magic pierce kept, as the game's page has it, though nothing in
        // Season 14 grants it.
        Line([Name(L("Elemental Pierce: ")), .. Elements(sheet, ["passive_fire_pierce", "passive_cold_pierce", "passive_ltng_pierce", "passive_pois_pierce", "passive_mag_pierce"])],
             [Name(L("Physical Pierce: ")), Stat(sheet, "passive_phys_pierce", Plain), Value("%")]);
        var gearAttack = sheet.Stat("tohit");
        Line([Name(L("Base AR: ")), Small(L("dex: ")), Of(sheet.AttackFromDexterity, Plain), Small(L(" equip: ")), Of(gearAttack, Plain),
              Small(L(" total: ")), Value($"{sheet.AttackFromDexterity.Value + gearAttack.Value}")]);
        var gearDefense = sheet.Stat("armorclass");
        Line([Name(L("Base Def: ")), Small(L("dex: ")), Of(sheet.DefenseFromDexterity, Plain), Small(L(" equip: ")), Of(gearDefense, Plain),
              Small(L(" total: ")), Value($"{sheet.DefenseFromDexterity.Value + gearDefense.Value}")]);
        Line([Name(L("Base Damage: ")), Small(L("1h: ")), Range(sheet, "mindamage", "maxdamage", Plain),
              Small(L(" 2h: ")), Range(sheet, "secondary_mindamage", "secondary_maxdamage", Plain)]);
        Line([Name(L("Chance to Block: ")), sheet.Blocking is { } blocking
            ? new SheetSegment($"{sheet.Block}%", Plain, Tip(blocking, L($"Most at dexterity {sheet.DexterityForMostBlock}")))
            : Value("0%")]);
        Gap();

        Line([Name(L("Cast Rate: ")), Stat(sheet, "item_fastercastrate", Plain)], [Name(L("Block Rate: ")), Stat(sheet, "item_fasterblockrate", Plain)]);
        Line([Name(L("Hit Recovery: ")), Stat(sheet, "item_fastergethitrate", Plain)], [Name(L("Run/Walk: ")), Stat(sheet, "item_fastermovevelocity", Plain)]);
        Line([Name(L("Attack Rate: ")), Of(sheet.AttackRate, Plain)], [Name(L("IAS: ")), Stat(sheet, "item_fasterattackrate", Plain)]);
        Gap();

        string heading = sheet.AttackName ?? (sheet.RightSkill is { } right && SkillNames.NameOf(right.Id) is { Length: > 0 } skill ? skill : L("Attack"));
        Line([Name(L($"Breakpoints ({heading}):"))]);
        // A mercenary that casts nothing: the page's own "0" (a Desert Guard).
        Line([Name(L("FCR: ")), .. sheet.IsMercenary && sheet.CastRateRow is null ? [Value("0")] : Row(sheet.CastRateRow, frames: false)]);
        Line([Name(L("FHR: ")), .. Row(sheet.HitRecoveryRow, frames: false)]);
        // The character's alone: the mercenary block can be skipped.
        if (!sheet.IsMercenary)
            Line([Name(L("FBR: ")), .. Row(sheet.BlockRateRow, frames: false)]);
        Line([Name(L("IAS (Frames): ")), .. Row(sheet.AttackRow, frames: true)]);
        Gap();

        Line([Name(L("Crushing Blow: ")), Stat(sheet, "item_crushingblow", Plain), Value(" / "), Of(sheet.CrushingBlowEfficiency, Plain), Value("%")],
             [Name(L("Open Wounds: ")), Stat(sheet, "item_openwounds", Plain), Value("%/+"), Of(sheet.OpenWoundsDamage, Plain)]);
        var deadly = sheet.Stat("item_deadlystrike");
        Line([Name(L("Deadly Strike: ")), Of(deadly, Plain, Math.Min(deadly.Value, sheet.DeadlyStrikeCap)), Value($" / {sheet.DeadlyStrikeCap} / "),
              Of(sheet.DeadlyMultiplier, Plain), Value("%")],
             [Name(L("Critical Strike: ")), Of(sheet.CriticalStrike, Plain, Math.Min(sheet.CriticalStrike.Value, 75)), Value(" / 75 / "),
              Of(sheet.CriticalMultiplier, Plain), Value("%")]);
        Line([Name(L("Life Leech: ")), Stat(sheet, "lifedrainmindam", Fire)], [Name(L("Mana Leech: ")), Stat(sheet, "manadrainmindam", Cold)]);
        Line([Name(L("Projectile Pierce: ")), Of(sheet.ProjectilePierce, Plain)],
             [Name(L("HP/MP per Kill: ")), Of(sheet.LifePerKill, Fire), Value(" / "), Stat(sheet, "item_manaafterkill", Cold)]);
        Gap();

        Line([Name(L("Added Damage: ")), Stat(sheet, "item_normaldamage", Plain)], [Added(sheet, "magicmindam", "magicmaxdam", "passive_mag_mastery", Magic)]);
        Line([Added(sheet, "firemindam", "firemaxdam", "passive_fire_mastery", Fire)], [Added(sheet, "coldmindam", "coldmaxdam", "passive_cold_mastery", Cold)]);
        var poison = sheet.Poison;
        Line([Added(sheet, "lightmindam", "lightmaxdam", "passive_ltng_mastery", Lightning)],
             [new SheetSegment(L($"{poison.Min}-{poison.Max} over {poison.Seconds.ToString("0.0", CultureInfo.CurrentCulture)}s"), Poison,
                               Tip(sheet.Stat("poisonmindam"), null))]);
        Line([Name(L("Magic Find: ")), Stat(sheet, "item_magicbonus", Cold)], [Name(L("Gold Find: ")), Stat(sheet, "item_goldbonus", Lightning)]);

        return lines;
    }

    public ObservableCollection<SheetLine> Lines { get; } = [];

    /// <summary>The buff row: everything that could be up, in the order the sheet found it.</summary>
    public ObservableCollection<BuffChip> Buffs { get; } = [];

    public bool HasBuffs => Buffs.Count > 0;

    /// <summary>The two skills beside the buff row - the character's; a mercenary's attack is its kind's.</summary>
    [ObservableProperty] private bool _hasSkillBoxes;

    /// <summary>What the two skill boxes may hold - made as a list is opened (<see cref="PrepareChoices"/>).</summary>
    public ObservableCollection<SkillChoice> RightChoices { get; } = [];
    public ObservableCollection<SkillChoice> LeftChoices { get; } = [];

    /// <summary>What they hold: the save's own two, and a choice written to it.</summary>
    [ObservableProperty] private SkillChoice? _rightChosen;
    [ObservableProperty] private SkillChoice? _leftChosen;

    /// <summary>A skill picked from a hand's list: written to the save (VP_ViewModel_Inv_L.ChooseSkill), the sheet made again.</summary>
    public System.Windows.Input.ICommand PickCommand { get; }

    /// <summary>
    /// The icons made, by the sheet and the cell they are cut from: made once, as the sheet is worked out again and
    /// again - and again for another season's files (<see cref="IconsFrom"/>).
    /// </summary>
    private static readonly Dictionary<(int Class, int Cel), Bitmap?> Icons = [];

    private static SkillIcons? IconsFrom;

    /// <summary>
    /// A skill's icon, from its class's sheet. A row of no class - an item's "SelfAura", a Lesser Fade - shows as
    /// the class skill it stands for; one that stands for none, from the character's own class's sheet.
    /// </summary>
    private static Bitmap? IconOf(SkillRow face, byte characterClass)
    {
        var row = face;
        int classId = CharacterClasses.Codes.ToList().IndexOf(face.CharClass);
        if (classId < 0)
        {
            string plain = face.Name.Replace(" SelfAura", "", StringComparison.OrdinalIgnoreCase).Replace("Lesser ", "", StringComparison.OrdinalIgnoreCase);
            if (SkillRows.Named(plain) is { CharClass.Length: > 0 } kin)
            {
                row = kin;
                classId = CharacterClasses.Codes.ToList().IndexOf(kin.CharClass);
            }
            else
            {
                // Attack, Throw, Unsummon and the left hand's two are on a sheet of their own.
                classId = face.Id <= 5 ? SkillIcons.NoClass : characterClass;
            }
        }

        if (row.IconCel < 0 || classId < 0)
            return null;
        if (!ReferenceEquals(IconsFrom, SkillIcons.Current))
        {
            Icons.Clear();
            IconsFrom = SkillIcons.Current;
        }
        if (!Icons.TryGetValue((classId, row.IconCel), out var icon))
            Icons[(classId, row.IconCel)] = icon = SpriteBitmap.From(SkillIcons.Current?.Load((byte)classId, row.IconCel));
        return icon;
    }

    /// <summary>What a buff is, under the pointer: its name and level, where it is from, and what a click does.</summary>
    private static string TipOf(Buff buff, CharacterSheet sheet)
    {
        var lines = new List<string> { L($"{buff.Name} {buff.Level}") };
        if (buff.From.Length > 0)
            lines.Add(L($"From {buff.From}"));

        // What no line of the page shows: the game keeps these for the character's and mercenary's own panels.
        foreach (var (stat, value) in sheet.GivenBy(buff))
        {
            if (stat == "maxhp")
                lines.Add(L($"Life +{value / 256}"));
            else if (stat == "maxmana")
                lines.Add(L($"Mana +{value / 256}"));
            else if (stat == "item_armor_percent")
                lines.Add(L($"Defense +{value}%"));
        }

        if (buff.Waits is { } form)
            lines.Add(L($"Needs the {form} form"));
        else if (buff.Needs is { } held)
            lines.Add(L($"Needs a {held} in hand"));
        else if (buff.On && !buff.Counted)
            lines.Add(L("A higher level of it is up"));
        else if (!buff.Skill.GivesTheCaster && buff.Kind is not (BuffKind.Spirit or BuffKind.OwnAura))
            lines.Add(L("Nothing on this page"));

        lines.Add(buff.Toggles ? (buff.On ? L("Click: off") : L("Click: on")) : L("Always up"));
        return string.Join('\n', lines);
    }

    private static SheetSegment Name(string text) => new(text, Plain);
    private static SheetSegment Value(string text) => new(text, Plain);
    private static SheetSegment Small(string text) => new(text, Dim);

    /// <summary>A number of the sheet in a colour, what it is made of under the pointer; <paramref name="shown"/> where the page shows less than the sum (a capped one).</summary>
    private static SheetSegment Of(SheetFigure figure, IBrush brush, int? shown = null) =>
        new($"{shown ?? figure.Value}", brush, Tip(figure, null));

    private static SheetSegment Stat(CharacterSheet sheet, string stat, IBrush brush) => Of(sheet.Stat(stat), brush);

    /// <summary>
    /// "Fire Resist: N 185 NM 145 H 85 / 75": the resistance in each difficulty in the element's colour, each saying
    /// under the pointer what it is made of, then the ceiling - and the length where the game shows one.
    /// </summary>
    private static List<SheetSegment> Resist(string name, CharacterSheet sheet, string element, IBrush colour, IEnumerable<SheetSegment>? length)
    {
        var parts = new List<SheetSegment> { Name(name) };
        string[] marks = [L("N "), L("  NM "), L("  H ")];
        for (int difficulty = 0; difficulty < 3; difficulty++)
            parts.AddRange([Small(marks[difficulty]), Of(sheet.Resist(element, difficulty).Value, colour)]);

        parts.AddRange([Value(" / "), Of(sheet.Resist(element, 0).Maximum, Plain)]);
        if (length is not null)
            parts.AddRange([Name(L("  Length: ")), .. length]);
        return parts;
    }

    /// <summary>"0/5%": a flat amount and a percent in one colour, each with its sources.</summary>
    private static IEnumerable<SheetSegment> Pair(CharacterSheet sheet, string flat, string percent, IBrush brush) =>
        [Stat(sheet, flat, brush), new("/", brush), Stat(sheet, percent, brush), new("%", brush)];

    private static readonly IBrush[] ElementBrushes = [Fire, Cold, Lightning, Poison, Magic];

    private static IEnumerable<SheetSegment> Elements(CharacterSheet sheet, string[] stats) =>
        stats.SelectMany((stat, at) => new[] { Stat(sheet, stat, ElementBrushes[at]), new SheetSegment("% ", ElementBrushes[at]) });

    private static SheetSegment Range(CharacterSheet sheet, string least, string most, IBrush brush)
    {
        var low = sheet.Stat(least);
        var high = sheet.Stat(most);
        return new SheetSegment($"{low.Value}-{high.Value}", brush, Tips((L("Minimum"), low), (L("Maximum"), high)));
    }

    /// <summary>An element's added damage as the page shows it, raised by its mastery, with what the two ends are made of.</summary>
    private static SheetSegment Added(CharacterSheet sheet, string least, string most, string mastery, IBrush brush)
    {
        var (low, high) = sheet.Added(least, most, mastery);
        return new SheetSegment($"{low}-{high}", brush, Tips((L("Minimum"), sheet.Stat(least)), (L("Maximum"), sheet.Stat(most)), (L("Mastery"), sheet.Stat(mastery))));
    }

    /// <summary>A breakpoint row, the step the character is at in orange with its frames, as the game's page marks it.</summary>
    private static IEnumerable<SheetSegment> Row(Breakpoints.Row? row, bool frames)
    {
        if (row is null || row.Points.Count == 0)
        {
            yield return Value(frames ? L("N/A") : "-");
            yield break;
        }

        // The cast and hit recovery rows start at the first step that buys a frame, as the game's do; the attack
        // row at 0.
        int first = frames ? 0 : 1;
        for (int at = first; at < row.Points.Count; at++)
        {
            var (value, taken) = row.Points[at];
            bool reached = at == row.Reached;
            string said = reached && frames ? $"{value} ({(row.Hits is { } hits ? string.Join("|", hits[at]) : taken)})" : $"{value}";
            yield return new SheetSegment(said, reached ? Magic : Plain);
            if (at < row.Points.Count - 1)
                yield return Value(" / ");
        }
    }

    /// <summary>
    /// What a number is made of, a line a source - "+20  Hellfire Torch" - the same source's parts as one with how
    /// many there are ("+50  Shimmering Small Charm of Inertia  x10"); null for a number that is no sum.
    /// </summary>
    private static string? Tip(SheetFigure figure, string? footer)
    {
        if (figure.Parts.Count == 0 && footer is null)
            return null;

        var text = new StringBuilder();
        foreach (var source in figure.Parts.GroupBy(part => part.Source))
        {
            int sum = source.Sum(part => part.Value);
            int count = source.Count();
            text.Append(sum >= 0 ? "+" : "").Append(sum.ToString(CultureInfo.CurrentCulture)).Append("  ").Append(source.Key.Length > 0 ? source.Key : L("Base"));
            if (count > 1)
                text.Append("  x").Append(count.ToString(CultureInfo.CurrentCulture));
            text.Append('\n');
        }

        if (footer is not null)
            text.Append(footer).Append('\n');
        return text.ToString().TrimEnd('\n');
    }

    private static string? Tips(params (string Title, SheetFigure Figure)[] figures)
    {
        var shown = figures.Where(each => each.Figure.Parts.Count > 0).Select(each => $"{each.Title}\n{Tip(each.Figure, null)}").ToList();
        return shown.Count == 0 ? null : string.Join("\n\n", shown);
    }
}
