using PD2SPCompanion.Core.Characters;
using System;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.Services.Character;

// The character sheet: what the game's own Advanced Stats page would show
// of the character as it stands in the editor.
public sealed partial class CharacterSession
{
    /// <summary>
    /// The sheet of the open character, worked out afresh: null with no character or no game - or where working it
    /// out failed, which the log says (a sheet is a view; a failure in it must not stop the page).
    /// </summary>
    public CharacterSheet? Sheet(CharacterSheet.Options options)
    {
        if (_save is null || EnsureGame() is not { } game)
            return null;

        try
        {
            return CharacterSheet.For(_save, game, options);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return null;
        }
    }

    /// <summary>What the open character's sheets are worked out from, as one number (CharacterSheet.InputsOf); 0 with none open.</summary>
    public int SheetInputs()
    {
        if (_save is null)
            return 0;

        try
        {
            return CharacterSheet.InputsOf(_save);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return 0;
        }
    }

    private readonly Dictionary<string, Dictionary<string, bool>> _buffPresses = [];

    /// <summary>
    /// What was pressed in the Advanced Stats buff rows for the open character, by <see cref="Buff.Key"/>: for as
    /// long as the program runs - a press is not in the save, which holds no buff. Both sheets read it, the character's and the mercenary's.
    /// </summary>
    public Dictionary<string, bool> BuffPresses =>
        _buffPresses.TryGetValue(CharacterName, out var pressed) ? pressed : _buffPresses[CharacterName] = [];

    /// <summary>
    /// The mercenary's sheet (<see cref="CharacterSheet.ForMercenary"/>): null with none, or where working it out
    /// failed. <paramref name="character"/>: the character's, worked out already with the same options.
    /// </summary>
    public CharacterSheet? MercenarySheet(CharacterSheet.Options options, CharacterSheet? character = null)
    {
        if (_save is null || EnsureGame() is not { } game)
            return null;

        try
        {
            return CharacterSheet.ForMercenary(_save, game, options, character);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return null;
        }
    }

    /// <summary>
    /// Puts a skill in one of the character's two skill boxes - those of the weapons in hand, as the save keeps
    /// them - as the game's own list would (<see cref="CharacterSheet.RightChoices"/>).
    /// </summary>
    public void SetSkillBox(bool right, int skillId)
    {
        if (_save is null)
            return;

        var skill = new D2SLib.Model.Save.Skill { Id = (uint)skillId };
        if (right)
            _save.RightSkill = skill;
        else
            _save.LeftSkill = skill;

        RaiseAttributesChanged();
    }

    /// <summary>The difficulty the character is in, as the save says: 0 Normal, 1 Nightmare, 2 Hell.</summary>
    public int Difficulty => _save?.Location?.Hell?.Active == true ? 2 : _save?.Location?.Nightmare?.Active == true ? 1 : 0;
}
