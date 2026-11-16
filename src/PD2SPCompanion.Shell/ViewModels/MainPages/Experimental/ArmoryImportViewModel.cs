using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;


using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The extras page's Armory Import panel, under Character Creation: a character from the PD2
/// armory made into a save. See <see cref="ArmoryImport"/>. Casual and Free - the setting that
/// opens the character page's editing.
/// </summary>
public partial class ArmoryImportViewModel : ObservableObject
{
    public CharacterSession Session { get; }

    public PlayRules Rules { get; }

    private readonly ISFXManager? _sfxManager;
    private readonly GameSync? _sync;

    public ArmoryImportViewModel(CharacterSession session, ISFXManager? sfxManager, PlayRules rules, GameSync? sync = null)
    {
        Session = session;
        _sfxManager = sfxManager;
        Rules = rules;
        _sync = sync;
        rules.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanImport));
    }

    public bool CanImport => Rules.EditsCharacter;

    /// <summary>What was pasted: a link to a character's armory page.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(HasLookup))]
    private string _lookup = "";

    public bool HasLookup => Lookup.Length > 0;

    /// <summary>The armory's answer, once fetched; null before, and after a new name is typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFetched))]
    [NotifyPropertyChangedFor(nameof(FetchedDetail))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private ArmoryImport.Armory? _fetched;

    public bool HasFetched => Fetched is not null;

    /// <summary>
    /// The fetched character on the Character page's card, set with the
    /// answer: who, the badges, and when the server last wrote its file as "played".
    /// </summary>
    [ObservableProperty] private CharacterCard? _fetchedCard;

    [ObservableProperty] private string _fetchedDescription = "";

    /// <summary>Life, mana and stamina as the import writes them - the armory's own are the current ones.</summary>
    [ObservableProperty] private string _fetchedLife = "";
    [ObservableProperty] private string _fetchedMana = "";
    [ObservableProperty] private string _fetchedStamina = "";

    [ObservableProperty] private string _fetchedLevel = "";
    [ObservableProperty] private string _fetchedExperience = "";

    partial void OnFetchedChanged(ArmoryImport.Armory? value)
    {
        FetchedCard = value is null ? null : new CharacterCard(new SaveSummary(
            "", "", value.Name, value.ClassId, value.ClassName, value.Level, value.IsHardcore, value.IsExpansion,
            value.IsDead, value.UpdatedAt ?? DateTime.Now, null, value.UpdatedAt, 0));
        FetchedDescription = value is null ? "" : L($"Level {value.Level} {value.ClassName}");
        FetchedLevel = value is null ? "" : $"{value.Level}";
        FetchedExperience = value is null ? "" : $"{value.Experience:N0}";

        var vitals = value is not null && Session.EnsureGame() is { } game ? ArmoryImport.BaseVitals(game, value) : default;
        FetchedLife = value is null ? "" : $"{vitals.Life}";
        FetchedMana = value is null ? "" : $"{vitals.Mana}";
        FetchedStamina = value is null ? "" : $"{vitals.Stamina}";
    }

    /// <summary>What comes with it, and what the armory does not have.</summary>
    public string FetchedDetail => Fetched is { } armory
        ? L($"{armory.Items.Count} items, mercenary {armory.MercenaryItems.Count}. Stash, cube and belt not in armory.")
        : "";

    /// <summary>
    /// What is wrong with what was typed, or with the armory's answer: a link not the PD2 site's, one
    /// with no character, a name the armory does not know, or no answer at all. Shown in the panel,
    /// under the box.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string? _warning;

    public bool HasWarning => Warning is not null;

    /// <summary>Counts fetches, so an answer to something no longer in the box is dropped.</summary>
    private int _asked;

    /// <summary>The armory's ladder, in the browser - the way the trade page opens the market.</summary>
    [RelayCommand]
    private void OpenArmory() => HyperText.Avalonia.Extensions.OpenUrlExtension.OpenUrl("https://www.projectdiablo2.com/armory/ladder");

    /// <summary>
    /// Starts again: the link, the card, the warning and the name go. The link box's own cross, shown while
    /// it holds something.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Lookup = "";
        NewName = "";
        Warning = null;
    }

    private bool CanClear() => Lookup.Length > 0 || HasFetched || HasWarning;

    /// <summary>The new character's name - the armory's by default.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _newName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    /// <summary>
    /// A new lookup: what was fetched goes. A link that is not a PD2 armory page says so at once, and one
    /// that is is fetched at once - pasting it is the ask. Links only: a name alone is a warning.
    /// </summary>
    partial void OnLookupChanged(string value)
    {
        _asked++;
        IsBusy = false;
        Fetched = null;

        var (name, isLink, problem) = ArmoryImport.Recognise(value);
        Warning = isLink ? problem : value.Trim().Length > 0 ? L("Not an armory link.") : null;
        if (isLink && name is not null)
            _ = Get(name);
    }

    private async Task Get(string name)
    {
        int asked = ++_asked;
        IsBusy = true;
        Warning = null;
        try
        {
            var armory = await ArmoryImport.Fetch(name);
            if (asked != _asked)
                return;
            Fetched = armory;
            NewName = armory.Name;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            if (asked != _asked)
                return;
            Fetched = null;
            Warning = problem is ArmoryImport.ArmoryException ? problem.Message : L("Armory not reachable.");
        }
        finally
        {
            if (asked == _asked)
                IsBusy = false;
        }
    }

    private bool CanRunImport() => !IsBusy && Fetched is not null && NewName.Trim().Length > 0;

    /// <summary>
    /// Writes the character into the save folder, built on the open character's save - or any in the
    /// folder - which only lends what the armory has no say in. A new file: nothing is overwritten,
    /// and nothing while the game runs (<see cref="NewCharacterFile"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private void Import()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (!CanImport || Fetched is not { } armory)
            return;

        string name = NewName.Trim();
        if (NewCharacterFile.Prepare(Session, _sync, name, "import") is not { } to)
            return;

        try
        {
            var (bytes, notes) = ArmoryImport.Build(to.Game, to.Template, armory, name);
            NewCharacterFile.Write(to.Path, bytes);
            Session.Messages.Post(notes.Count == 0
                ? L($"{name} imported. Open it from Characters.")
                : L($"{name} imported, {notes.Count} left out. Open it from Characters."));
            Fetched = null;
            Lookup = "";
            NewName = "";
        }
        catch (Exception failure)
        {
            Log.Caught(failure);
            Session.Messages.Post(L($"{name} not imported: {failure.Message}"));
        }
    }
}
