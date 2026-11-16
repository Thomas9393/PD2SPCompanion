using System;
using System.Collections.Generic;

using PD2SPCompanion.Core.Characters;

namespace PD2SPCompanion.Shell.Services.Character;

// The mercenary, edited within its type (PD2SPCompanion.Core.Characters.MercenaryEdit).
public sealed partial class CharacterSession
{
    /// <summary>The variants the mercenary may be made: its own type's, each once. None with no mercenary.</summary>
    public IReadOnlyList<MercenaryEdit.Variant> MercenaryVariants() =>
        _save is null ? [] : MercenaryEdit.VariantsOf(_save, _game);

    /// <summary>Which of <see cref="MercenaryVariants"/> it is; -1 with no mercenary.</summary>
    public int MercenaryVariantIndex => _save is null ? -1 : MercenaryEdit.VariantIndex(_save, _game);

    /// <summary>Its name, from its type's list; "" where the list does not reach its number, or with none hired.</summary>
    public string MercenaryName => _save is null ? "" : MercenaryEdit.NameOf(_save, _game);

    /// <summary>The names its type draws from, in the save's own order.</summary>
    public IReadOnlyList<string> MercenaryNames() =>
        _save is null ? [] : MercenaryEdit.NamesOf(_save, _game);

    /// <summary>Which of <see cref="MercenaryNames"/> it has; -1 with no mercenary.</summary>
    public int MercenaryNameIndex => _save?.Mercenary is { Id: not 0 } hired ? hired.NameId : -1;

    /// <summary>Its row of the hireling table; -1 with no mercenary.</summary>
    public int MercenaryTypeId => _save?.Mercenary is { Id: not 0 } hired ? hired.TypeId : -1;

    /// <summary>
    /// The highest level it may be given: the character's own, as the game holds a mercenary to it - or the
    /// most there is where the play mode adds to a character freely.
    /// </summary>
    public int MercenaryMostLevel(bool freely) =>
        freely ? MercenaryEdit.MostLevel : Math.Clamp(Leveling?.Level ?? 1, 1, MercenaryEdit.MostLevel);

    public void SetMercenaryName(int index)
    {
        if (_save is null)
            return;

        MercenaryEdited(MercenaryEdit.SetName(_save, _game, index),
                        () => L($"Mercenary named {MercenaryEdit.NameOf(_save, _game)}."));
    }

    public void SetMercenaryVariant(int typeId)
    {
        if (_save is null)
            return;

        MercenaryEdited(MercenaryEdit.SetVariant(_save, _game, typeId),
                        () => L($"Mercenary: {Mercenary.VariantText}."));
    }

    public void SetMercenaryLevel(int level, bool freely)
    {
        if (_save is null)
            return;

        level = Math.Clamp(level, 1, MercenaryMostLevel(freely));
        MercenaryEdited(MercenaryEdit.SetLevel(_save, _game, level),
                        () => L($"Mercenary level {Mercenary.Level}."));
    }

    public void ReviveMercenary()
    {
        if (_save is null)
            return;

        MercenaryEdited(MercenaryEdit.Revive(_save), () => L("Mercenary revived."));
    }

    /// <summary>
    /// After an edit of the mercenary: refused and said, or read again - its level decides what its gear asks,
    /// so everything worn is judged again - said, and the character marked for the autosave.
    /// </summary>
    private void MercenaryEdited(string? refused, Func<string> done)
    {
        if (refused is not null)
        {
            Status = refused;
            return;
        }

        Rebuild();
        Status = done();
        RaiseAttributesChanged();
    }
}
