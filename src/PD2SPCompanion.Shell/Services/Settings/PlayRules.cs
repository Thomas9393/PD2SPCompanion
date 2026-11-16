using CommunityToolkit.Mvvm.ComponentModel;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The play mode as the pages ask it: "does this part show?". One object for every page, so a
/// view binds <c>Rules.EditsCharacter</c> rather than each view model reading the policy and
/// keeping its own copy - and every answer changes the moment the mode does.
/// </summary>
public sealed partial class PlayRules : ObservableObject
{
    private readonly EditorSettings _settings;

    public PlayRules(EditorSettings settings)
    {
        _settings = settings;

        // Every answer at once: a mode changes many of them together.
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditorSettings.Policy) or nameof(EditorSettings.PlayMode))
                OnPropertyChanged(string.Empty);
            else if (e.PropertyName == nameof(EditorSettings.SocketGuard))
                OnPropertyChanged(nameof(SocketGuard));
        };
    }

    // ---- sockets, on every page alike: the socket toggle and the socket guard in sync with the
    // inventory, for no frustrating inconsistency. The Items page's own until then; the
    // vault's page had neither, and drew its sockets opaque whatever the Items page showed. Here, where both
    // pages already look: each has the two buttons, and a press on either page is seen on both.

    /// <summary>
    /// The sockets over an item's art drawn faint, so the item itself is seen (the button, and X). A look of the
    /// session's: opaque as the program starts.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SocketOpacity))]
    private bool _socketsFaded;

    public double SocketOpacity => SocketsFaded ? 0.18 : 1.0;

    /// <summary>The socket guard: nothing let go of on an item is socketed while it is on. Kept in the settings.</summary>
    public bool SocketGuard
    {
        get => _settings.SocketGuard;
        set => _settings.SocketGuard = value;
    }

    public PlayPolicy Policy => _settings.Policy;

    /// <summary>The settings behind the rules, for a page's own preference kept beside them.</summary>
    public EditorSettings Settings => _settings;

    // ---- the character

    /// <summary>Attributes, respec, skills, level and experience can be changed.</summary>
    public bool EditsCharacter => Policy.Character != Reach.Locked;

    /// <summary>Unrestricted: unspent attribute and skill points can be added out of nowhere.</summary>
    public bool AddsCharacterPoints => Policy.Character == Reach.Any;

    public bool TypesGold => Policy.TypeGold;

    public bool EditsWaypoints => Policy.Waypoints;

    public bool EditsMapSeed => Policy.MapSeed;

    // ---- items

    public bool EditsProperties => Policy.Properties != Reach.Locked;

    /// <summary>Past the ranges and past what the sources grant: free editing.</summary>
    public bool EditsPropertiesFreely => Policy.Properties == Reach.Any;

    /// <summary>The property picker lists every stat once, by id and engine name - Custom's alone.</summary>
    public bool ListsRawProperties => Policy.RawProperties;

    /// <summary>The Forge's bookmark is on the Items page: items made from nothing - Free's.</summary>
    public bool Forges => Policy.Forge;

    public bool EditsDetails => Policy.Details != Reach.Locked;

    public bool EditsDetailsFreely => Policy.Details == Reach.Any;

    public bool EditsItemLevel => Policy.ItemLevel;

    public bool EditsEthereal => Policy.Ethereal;

    public bool EditsSockets => Policy.Sockets != Reach.Locked;


    public bool EditsAffixes => Policy.Affixes;

    public bool Corrupts => Policy.Corruption;

    public bool Crafts => Policy.Craft;

    /// <summary>The runeword builder: the Zod button on a base that can hold a word.</summary>
    public bool BuildsRunewords => Policy.Runewords;

    public bool Duplicates => Policy.Duplicate;

    public bool TypesMaterials => Policy.TypeMaterials;

    // ---- vault, trading

    public bool BuysTwice => Policy.RepeatBuys;

    /// <summary>An item whose requirements are not met can be put on anyway, with a warning - not in Legit.</summary>
    public bool ForcesEquip => Policy.ForceEquip;

    /// <summary>Items sharing an id are marked, and search can show only them - Legit's.</summary>
    public bool DetectsDuplicates => Policy.DetectDuplicates;
}
