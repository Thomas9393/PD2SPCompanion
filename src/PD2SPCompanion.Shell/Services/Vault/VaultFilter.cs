using CommunityToolkit.Mvvm.ComponentModel;

using PD2SPCompanion.Shell.Services.Settings;


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// One line of a search picker: what it says, and what it asks of the store. "Any" asks nothing.
/// </summary>
public sealed record FilterChoice(string Label, int? From = null, int? To = null, string? Code = null, bool? Flag = null)
{
    public bool IsAny => From is null && Code is null && Flag is null;

    /// <summary>The label as shown: in the language in use (Label stays English - presets store it).</summary>
    public string Shown => L(Label);

    public override string ToString() => Shown;
}

/// <summary>
/// One property row of the search: the property, the skill beside it where it picks one, and
/// the bounds. Empty bounds still ask something - that the item has the property at all - as a
/// row on the trade site does.
/// </summary>
public sealed partial class PropertyFilterRow : ObservableObject
{
    public PropertyFilterRow(PropertyOption option)
    {
        Option = option;
        SkillChoices = option.Picks == SkillPick.None ? [] : SkillsFor(option.Picks);
    }

    public PropertyOption Option { get; }

    public string Label => Option.Label;

    public bool PicksSkill => Option.Picks != SkillPick.None;

    /// <summary>The skills a row of this kind can name, "Any" first.</summary>
    public IReadOnlyList<FilterChoice> SkillChoices { get; }

    [ObservableProperty] private FilterChoice _skill = VaultFilter.Any;

    /// <summary>Typed into number boxes, so text; empty is no bound.</summary>
    [ObservableProperty] private string _min = "";
    [ObservableProperty] private string _max = "";

    /// <summary>
    /// The number an item has, where the row was filled from one - the trading page's sale - and
    /// empty in the vault's own search.
    /// </summary>
    public string Roll { get; init; } = "";

    public bool HasRoll => Roll.Length > 0;

    /// <summary>The row as the store asks it.</summary>
    public PropertyWant Want()
    {
        double? low = Number(Min), high = Number(Max);

        if (!PicksSkill)
            return new PropertyWant(Option.Key, false, low, high);

        return Skill.From is { } skill
            ? new PropertyWant(PropertyIndex.KeyOf(Option.Key, skill), false, low, high)
            : new PropertyWant(PropertyIndex.FamilyOf(Option.Key), true, low, high);
    }

    public PresetProperty Snapshot() => new(
        Option.Key, Skill.From,
        Min.Trim() is { Length: > 0 } low ? low : null,
        Max.Trim() is { Length: > 0 } high ? high : null);

    private static double? Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    /// <summary>Built once per kind: several hundred skills, the same list for every row.</summary>
    private static readonly Dictionary<SkillPick, IReadOnlyList<FilterChoice>> _skills = [];

    private static IReadOnlyList<FilterChoice> SkillsFor(SkillPick picks)
    {
        if (!_skills.TryGetValue(picks, out var list))
        {
            list = [VaultFilter.Any, .. PropertyIndex.SkillsFor(picks)
                .Select(skill => new FilterChoice(
                    skill.ClassName.Length > 0 ? $"{skill.Name} ({skill.ClassName})" : skill.Name,
                    From: skill.Id, To: skill.Id))];
            _skills[picks] = list;
        }

        return list;
    }
}

/// <summary>
/// What the vault is being searched for: the search panel on the right edits it, the list on the
/// left reads it. One object on <see cref="VaultService"/> so both pages see the same search,
/// the way they already see the same vault.
/// <para>
/// The Basics of the trade site's panel, less what a single-player library has no use for -
/// price, who is online, ladder. Properties are the next step. See the design note's "Advanced
/// search" section for the site's panel as read off its code.
/// </para>
/// </summary>
public sealed partial class VaultFilter : ObservableObject
{
    public static readonly FilterChoice Any = new(N("Any"));

    private static readonly IReadOnlyList<FilterChoice> YesNo =
        [Any, new(N("Yes"), Flag: true), new(N("No"), Flag: false)];

    /// <summary>Raised after anything in the search changes, for the list to read again.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when the whole search is cleared, for the list to let go of what it had selected
    /// too - a selection made among the results is not one to carry into the full vault.
    /// </summary>
    public event Action? Cleared;

    /// <summary>Part of an item's name, ignoring case. Empty is any name.</summary>
    [ObservableProperty] private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPickFamily))]
    private FilterChoice _rarity = Any;

    /// <summary>
    /// The craft recipe family a crafted item was made by - "Blood", "Caster" - or empty for any. Picked in
    /// the form's Family box (<see cref="Family"/>), which waits for the rarity to be Crafted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Family))]
    private string _craftFamily = "";

    /// <summary>The craft families, as the wiki's chart heads its columns.</summary>
    public IReadOnlyList<FilterChoice> FamilyChoices { get; } =
        [Any, .. GrailCatalog.FamilyOrder.Select(family => new FilterChoice(char.ToUpperInvariant(family[0]) + family[1..], Code: family))];

    /// <summary>The Family box's choice: <see cref="CraftFamily"/> as one of <see cref="FamilyChoices"/>.</summary>
    public FilterChoice Family
    {
        get => FamilyChoices.FirstOrDefault(choice => string.Equals(choice.Code, CraftFamily, StringComparison.OrdinalIgnoreCase)) ?? Any;
        set => CraftFamily = value is { IsAny: false } ? value.Label : "";
    }

    /// <summary>A family is a crafted item's, so its box waits for that rarity - as a base waits for a type.</summary>
    public bool CanPickFamily => Rarity.From == ForgeSearch.Crafted;

    /// <summary>Another rarity makes the family chosen under Crafted meaningless.</summary>
    partial void OnRarityChanged(FilterChoice value)
    {
        if (!_clearing && !CanPickFamily)
            CraftFamily = "";
    }

    // A search looks through the whole vault. "Search in" - all folders, this one and below, this one only -
    // stood here until the finds were listed place by place, which made it obsolete: the list says
    // which folder each is in, the folder shown first, and a scope that meant "the folder shown" narrowed the
    // search the moment a find in another folder was gone to.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaseChoices))]
    [NotifyPropertyChangedFor(nameof(CanPickBase))]
    private FilterChoice _type = Any;

    [ObservableProperty] private FilterChoice _base = Any;

    [ObservableProperty] private FilterChoice _ethereal = Any;
    [ObservableProperty] private FilterChoice _corrupted = Any;
    [ObservableProperty] private FilterChoice _desecrated = Any;

    /// <summary>Typed into number boxes, so text; empty is no bound.</summary>
    [ObservableProperty] private string _socketsMin = "";
    [ObservableProperty] private string _socketsMax = "";

    /// <summary>The property rows. An item must satisfy every one, as on the trade site.</summary>
    public ObservableCollection<PropertyFilterRow> Properties { get; } = [];

    public bool HasProperties => Properties.Count > 0;

    /// <summary>A row for the property, and beside it, where the search is filled from an item, that item's number.</summary>
    public PropertyFilterRow AddProperty(PropertyOption option, string roll = "")
    {
        var row = new PropertyFilterRow(option) { Roll = roll };
        row.PropertyChanged += (_, _) =>
        {
            if (!_clearing)
                SayChanged();
        };
        Properties.Add(row);

        OnPropertyChanged(nameof(HasProperties));
        if (!_clearing)
            SayChanged();
        return row;
    }

    public void RemoveProperty(PropertyFilterRow row)
    {
        if (!Properties.Remove(row))
            return;

        OnPropertyChanged(nameof(HasProperties));
        SayChanged();
    }

    public IReadOnlyList<FilterChoice> YesNoChoices => YesNo;

    public IReadOnlyList<FilterChoice> RarityChoices { get; } =
        [Any, .. ItemOrder.Rarities.Select((label, rank) => new FilterChoice(label, From: rank, To: rank))];

    /// <summary>The Type order's groups, and under Weapons each kind - see <see cref="ItemOrder.TypeSpans"/>.</summary>
    public IReadOnlyList<FilterChoice> TypeChoices => TypeSpanChoices;

    /// <summary>
    /// The same list for anything else that filters by type - the grail's quick filter - so the
    /// two read alike and are made one way.
    /// </summary>
    public static IReadOnlyList<FilterChoice> TypeSpanChoices { get; } =
        [Any, .. ItemOrder.TypeSpans().Select(span => new FilterChoice(span.Label, span.From, span.To))];

    /// <summary>
    /// The bases of the type chosen, from the tables, by name. Only once a type is chosen, as the
    /// trade site does it: every base at once is several hundred lines of one list.
    /// </summary>
    public IReadOnlyList<FilterChoice> BaseChoices =>
        Type.From is not { } from || Type.To is not { } to
            ? [Any]
            : [Any, .. BasesIn(from, to)];

    /// <summary>
    /// The bases whose type ranks within a span, by name: a Base picker's lines, here and on the Forge.
    /// </summary>
    public static IEnumerable<FilterChoice> BasesIn(int from, int to) =>
        ItemBases.Codes
            .Where(code => ItemOrder.TypeRankOf(code) is var rank && rank >= from && rank <= to)
            .Select(code => new FilterChoice(ItemPresenter.BaseName(code), Code: code))
            .Where(choice => choice.Label.Length > 0
                             && !choice.Label.Equals("Not used", StringComparison.OrdinalIgnoreCase))
            .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase);

    public bool CanPickBase => !Type.IsAny;

    /// <summary>A new type makes the base chosen under the old one meaningless.</summary>
    partial void OnTypeChanged(FilterChoice value) => Base = Any;

    /// <summary>Whether anything at all is being searched for.</summary>
    public bool IsFiltering => Query().IsFiltering;

    // ---- duplicates ----------------------------------------------------------------------

    /// <summary>The copies of one item, by its id - what a duplicate badge's click searches for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSameId))]
    private uint? _sameId;

    public bool HasSameId => SameId is not null;

    public void ClearSameId() => SameId = null;

    public static readonly IReadOnlyList<FilterChoice> DuplicateChoices = [Any, new FilterChoice(N("Only duplicates"), Flag: true)];

    /// <summary>Only items held more than once - offered while the play rule marks them.</summary>
    [ObservableProperty] private FilterChoice _duplicates = Any;

    /// <summary>Whether the play rule marks duplicates, so the search offers them. Set by <see cref="DuplicateIndex"/>.</summary>
    [ObservableProperty] private bool _offersDuplicates;

    partial void OnOffersDuplicatesChanged(bool value)
    {
        if (!value)
            Duplicates = Any;
    }

    /// <summary>The ids held more than once, asked when the search runs. Set by <see cref="DuplicateIndex"/>.</summary>
    public Func<IReadOnlyCollection<uint>>? DuplicateIds { get; set; }

    private IReadOnlyList<uint>? ItemIds() =>
        SameId is { } one ? [one]
        : Duplicates.Flag == true && OffersDuplicates && DuplicateIds is { } ids ? [.. ids()]
        : null;

    /// <summary>The search as the store asks it.</summary>
    public VaultQuery Query() => new(
        Name: Name.Trim() is { Length: > 0 } typed ? typed : null,
        Rarity: Rarity.From,
        TypeFrom: Type.From, TypeTo: Type.To,
        Base: Base.Code,
        Ethereal: Ethereal.Flag,
        Corrupted: Corrupted.Flag, Desecrated: Desecrated.Flag,
        SocketsMin: int.TryParse(SocketsMin, out int low) ? low : null,
        SocketsMax: int.TryParse(SocketsMax, out int high) ? high : null,
        Properties: [.. Properties.Select(row => row.Want())],
        CraftFamily: CraftFamily.Length > 0 ? CraftFamily : null,
        ItemIds: ItemIds());

    /// <summary>The search as it stands, to keep as a preset. The name is the caller's.</summary>
    public SearchPreset Snapshot() => new()
    {
        ItemName = Name.Trim() is { Length: > 0 } typed ? typed : null,
        Rarity = Rarity.From,
        TypeFrom = Type.From,
        TypeTo = Type.To,
        Base = Base.Code,
        Ethereal = Ethereal.Flag,
        Corrupted = Corrupted.Flag,
        Desecrated = Desecrated.Flag,
        SocketsMin = SocketsMin.Trim() is { Length: > 0 } low ? low : null,
        SocketsMax = SocketsMax.Trim() is { Length: > 0 } high ? high : null,
        Properties = Properties.Count > 0 ? [.. Properties.Select(row => row.Snapshot())] : null,
        CraftFamily = CraftFamily.Length > 0 ? CraftFamily : null,
        OnlyDuplicates = Duplicates.Flag == true ? true : null,
    };

    /// <summary>
    /// Puts a preset's search in the pickers, each value matched to the picker line that asks
    /// it - Any where a picker has no line for it any more. One change said, not ten. A duplicate badge's
    /// one id is no preset's: it ends here, or the preset would ask that one id too.
    /// </summary>
    public void Apply(SearchPreset preset)
    {
        _clearing = true;
        try
        {
            SameId = null;
            Duplicates = preset.OnlyDuplicates == true && OffersDuplicates ? DuplicateChoices[1] : Any;
            Name = preset.ItemName ?? "";
            Rarity = RarityChoices.FirstOrDefault(choice => choice.From == preset.Rarity) ?? Any;
            CraftFamily = preset.CraftFamily ?? "";
            Type = TypeChoices.FirstOrDefault(choice => choice.From == preset.TypeFrom && choice.To == preset.TypeTo) ?? Any;

            // After the type, which empties the base and decides what bases there are.
            Base = BaseChoices.FirstOrDefault(choice => string.Equals(choice.Code, preset.Base, StringComparison.OrdinalIgnoreCase)) ?? Any;

            Ethereal = YesNo.FirstOrDefault(choice => choice.Flag == preset.Ethereal) ?? Any;
            Corrupted = YesNo.FirstOrDefault(choice => choice.Flag == preset.Corrupted) ?? Any;
            Desecrated = YesNo.FirstOrDefault(choice => choice.Flag == preset.Desecrated) ?? Any;
            SocketsMin = preset.SocketsMin ?? "";
            SocketsMax = preset.SocketsMax ?? "";
            // Rows whose property the tables no longer offer are left out, not guessed at.
            Properties.Clear();
            foreach (var kept in preset.Properties ?? [])
            {
                if (PropertyIndex.Options.FirstOrDefault(option => option.Key == kept.Key) is not { } option)
                    continue;

                var row = AddProperty(option);
                row.Skill = row.SkillChoices.FirstOrDefault(choice => choice.From == kept.Skill) ?? Any;
                row.Min = kept.Min ?? "";
                row.Max = kept.Max ?? "";
            }

            OnPropertyChanged(nameof(HasProperties));
        }
        finally
        {
            _clearing = false;
        }

        SayChanged();
    }

    /// <summary>
    /// A search in a line, for a preset under its name: "Unique · Helms · Ethereal". What is
    /// set, in the panel's order; "Any item" when nothing is.
    /// </summary>
    public string Describe() => Describe(Snapshot());

    public string Describe(SearchPreset preset)
    {
        var parts = new List<string>();

        if (preset.ItemName is { } typed)
            parts.Add($"\u201C{typed}\u201D");

        string? LabelOf(IEnumerable<FilterChoice> choices, Func<FilterChoice, bool> match) =>
            choices.FirstOrDefault(match)?.Shown;

        if (preset.TypeFrom is not null)
            parts.Add(LabelOf(TypeChoices, choice => choice.From == preset.TypeFrom && choice.To == preset.TypeTo) ?? L("Type"));
        if (preset.Base is { } code)
            parts.Add(ItemPresenter.BaseName(code));
        if (preset.Rarity is not null)
            parts.Add(LabelOf(RarityChoices, choice => choice.From == preset.Rarity) ?? L("Rarity"));
        if (preset.CraftFamily is { Length: > 0 } family)
            parts.Add(L($"{family} craft"));
        if (preset.Ethereal is { } ethereal)
            parts.Add(ethereal ? L("Ethereal") : L("Not ethereal"));
        if (preset.Corrupted is { } corrupted)
            parts.Add(corrupted ? L("Corrupted") : L("Not corrupted"));
        if (preset.Desecrated is { } desecrated)
            parts.Add(desecrated ? L("Desecrated") : L("Not desecrated"));
        if (preset.OnlyDuplicates == true)
            parts.Add(DuplicateChoices[1].Shown);

        if (preset.SocketsMin is { } low && preset.SocketsMax is { } high)
            parts.Add(low == high ? L($"{low} sockets") : L($"{low}-{high} sockets"));
        else if (preset.SocketsMin is { } least)
            parts.Add(L($"{least}+ sockets"));
        else if (preset.SocketsMax is { } most)
            parts.Add(L($"Up to {most} sockets"));

        foreach (var kept in preset.Properties ?? [])
        {
            if (PropertyIndex.Options.FirstOrDefault(option => option.Key == kept.Key) is not { } option)
                continue;

            string label = option.Label;
            if (kept.Skill is { } skill && SkillNames.NameOf(skill) is { } named)
                label = label.Replace("(skill)", named).Replace("(aura)", named);

            string range = (kept.Min, kept.Max) switch
            {
                ({ } from, { } to) => from == to ? from : $"{from}-{to}",
                ({ } from, null) => $"{from}+",
                (null, { } to) => L($"up to {to}"),
                _ => "",
            };

            parts.Add(range.Length > 0 ? $"{label} ({range})" : label);
        }

        return parts.Count == 0 ? L("Any item") : string.Join(" \u00B7 ", parts);
    }

    /// <summary>Everything back to Any, with one change said rather than twelve.</summary>
    public void Clear()
    {
        Empty();
        Cleared?.Invoke();
    }

    /// <summary>
    /// Everything back to Any without the search being ended: the Search bookmark left for another, which
    /// keeps what it asked and asks it again when it is come back to (SearchWindowViewModel.Leave). What was
    /// selected among the finds stays selected - <see cref="Cleared"/> is not raised - so a find gone to is
    /// still the item on the Edit page.
    /// </summary>
    public void Empty()
    {
        _clearing = true;
        try
        {
            Name = "";
            Rarity = Any;
            CraftFamily = "";
            Type = Any;
            Base = Any;
            Ethereal = Any;
            Corrupted = Any;
            Desecrated = Any;
            SocketsMin = "";
            SocketsMax = "";
            SameId = null;
            Duplicates = Any;
            Properties.Clear();
            OnPropertyChanged(nameof(HasProperties));
        }
        finally
        {
            _clearing = false;
        }

        SayChanged();
    }

    private bool _clearing;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Only what the search is made of; the derived lists and flags follow from those.
        if (!_clearing && e.PropertyName is not (nameof(BaseChoices) or nameof(CanPickBase) or nameof(IsFiltering)
                or nameof(Family) or nameof(CanPickFamily)
                or nameof(HasProperties)))
            SayChanged();
    }

    private void SayChanged()
    {
        OnPropertyChanged(nameof(IsFiltering));
        Changed?.Invoke();
    }
}
