using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;


using System;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The extras page's Character Creation section: a name, a class, a level, the quests
/// - none, or every one done with Charsi's imbue, Larzuk's socket and Anya's personalization left
/// waiting - softcore or hardcore, and a name; a character ready to play, written as a new file in
/// the save folder. See <see cref="CharacterCreation"/>. Casual and Free, as the armory import.
/// </summary>
public partial class CharacterCreationViewModel : ObservableObject
{
    public CharacterSession Session { get; }

    public PlayRules Rules { get; }

    private readonly ISFXManager? _sfxManager;
    private readonly GameSync? _sync;

    public CharacterCreationViewModel(CharacterSession session, ISFXManager? sfxManager, PlayRules rules, GameSync? sync)
    {
        Session = session;
        _sfxManager = sfxManager;
        Rules = rules;
        _sync = sync;
        rules.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanCreate));
    }

    /// <summary>Casual and Free: the play modes that edit a character.</summary>
    public bool CanCreate => Rules.EditsCharacter;

    public IReadOnlyList<string> Classes => CharacterClasses.Names;

    [ObservableProperty]
    private int _classIndex;

    /// <summary>The level as typed, 1 to 99.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(LowerLevelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RaiseLevelCommand))]
    private string _levelText = "1";

    /// <summary>Every quest done, with Charsi's imbue, Larzuk's socket and Anya's personalization left waiting.</summary>
    [ObservableProperty]
    private bool _questsDone;

    /// <summary>
    /// Softcore (0) or hardcore (1): the Mode pair's choice (two choices are a pair).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHardcore))]
    private int _modeIndex;

    public bool IsHardcore => ModeIndex == 1;

    /// <summary>The Mode pair's two buttons: softcore (0) or hardcore (1).</summary>
    [RelayCommand]
    private void ChooseMode(string? index) => ModeIndex = index == "1" ? 1 : 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _newName = "";

    private int? Level => int.TryParse(LevelText, out int level) && level is >= 1 and <= 99 ? level : null;

    /// <summary>The level stepper, as the Character page's: one level a click, within 1 to 99.</summary>
    [RelayCommand(CanExecute = nameof(CanLowerLevel))]
    private void LowerLevel()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        LevelText = $"{Math.Clamp((Level ?? 2) - 1, 1, 99)}";
    }

    private bool CanLowerLevel() => Level is not { } level || level > 1;

    [RelayCommand(CanExecute = nameof(CanRaiseLevel))]
    private void RaiseLevel()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        LevelText = $"{Math.Clamp((Level ?? 0) + 1, 1, 99)}";
    }

    private bool CanRaiseLevel() => Level is not { } level || level < 99;

    /// <summary>Ctrl or Shift on the stepper: five levels or twenty at once, held within 1 to 99.</summary>
    public void StepLevel(int levels)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        LevelText = $"{Math.Clamp((Level ?? 1) + levels, 1, 99)}";
    }

    /// <summary>Ctrl and Shift on the stepper: straight to level 99, or back to 1.</summary>
    public void LevelToEnd(bool up)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        LevelText = up ? "99" : "1";
    }

    /// <summary>CREATE was pressed with no name: said under the box until one is typed.</summary>
    [ObservableProperty] private bool _nameMissing;

    partial void OnNewNameChanged(string value)
    {
        if (value.Trim().Length > 0)
            NameMissing = false;
    }

    /// <summary>Pressable whenever the level is; a missing name is said, not hidden behind a dead button.</summary>
    private bool CanRunCreate() => Level is not null;

    /// <summary>Writes the character into the save folder: a new file, nothing overwritten.</summary>
    [RelayCommand(CanExecute = nameof(CanRunCreate))]
    private void Create()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (!CanCreate || Level is not { } level)
            return;

        string name = NewName.Trim();
        if (name.Length == 0)
        {
            NameMissing = true;
            return;
        }

        if (NewCharacterFile.Prepare(Session, _sync, name, "create") is not { } to)
            return;

        try
        {
            byte[] bytes = CharacterCreation.Build(to.Game, to.Template, name, (byte)Math.Clamp(ClassIndex, 0, Classes.Count - 1),
                                                   level, IsHardcore, QuestsDone);
            NewCharacterFile.Write(to.Path, bytes);
            Session.Messages.Post(L($"{name} created. Open it from Characters."));
            // The choices stay, for another like it; the name cannot be used twice.
            NewName = "";
        }
        catch (Exception failure)
        {
            Log.Caught(failure);
            Session.Messages.Post(L($"{name} not created: {failure.Message}"));
        }
    }
}
