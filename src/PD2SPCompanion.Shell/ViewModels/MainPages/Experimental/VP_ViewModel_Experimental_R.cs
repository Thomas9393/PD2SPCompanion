using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The right half of the extras page: the character files it makes - Character Creation first,
/// then Armory Import (they were under Modded Single Player on the left).
/// </summary>
public class VP_ViewModel_Experimental_R : ViewModelBase
{
    public CharacterSession Session { get; }

    /// <summary>A new character, of a class, level, quests and mode chosen.</summary>
    public CharacterCreationViewModel Creation { get; }

    /// <summary>A character from PD2's armory, under a name of its own.</summary>
    public ArmoryImportViewModel Armory { get; }

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Experimental_R() : this(new CharacterSession(), null!, new PlayRules(new EditorSettings())) { }

    public VP_ViewModel_Experimental_R(CharacterSession session, ISFXManager sfxManager, PlayRules rules, GameSync? sync = null)
    {
        Session = session;
        Creation = new CharacterCreationViewModel(session, sfxManager, rules, sync);
        Armory = new ArmoryImportViewModel(session, sfxManager, rules, sync);
    }
}
