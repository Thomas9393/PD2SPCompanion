namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What a property is made of, beyond its number - which is what says how many fields an
/// editor has to offer for it.
/// </summary>
public enum StatShape
{
    /// <summary>A number, and nothing else.</summary>
    Value,

    /// <summary>A number the file keeps in a single bit: on or off.</summary>
    Toggle,

    /// <summary>The parameter is a skill id and the value is how many levels of it.</summary>
    Skill,

    /// <summary>The parameter packs a skill and a level; the value is a chance.</summary>
    Chance,

    /// <summary>The same, with charges in place of the chance and a second byte for the most.</summary>
    Charges,

    /// <summary>The parameter is a class and one of its three trees, <c>class * 8 + tab</c>.</summary>
    SkillTree,

    /// <summary>The parameter is a class.</summary>
    Class,

    /// <summary>The parameter is an element.</summary>
    Element,

    /// <summary>The parameter is a monster; the value is the chance of raising one.</summary>
    Monster,

    /// <summary>The parameter is a state; the value is the bit that turns it on.</summary>
    State,
}

/// <summary>
/// Which shape each property has. One place, because two of them would disagree: the panel
/// draws the fields from this and the audit export lists them from it, and an export that
/// says a property has a skill picker it does not have is worse than no export.
/// </summary>
public static class StatShapes
{
    public static StatShape Of(string? statName)
    {
        if (statName is not { Length: > 0 } name)
            return StatShape.Value;

        // Five the tables do not describe as anything in particular, and which are only
        // known by what has been read off items in game. See StatText.ByParameter.
        switch (name)
        {
            case "item_addskill_tab": return StatShape.SkillTree;
            case "item_addclassskills": return StatShape.Class;
            case "item_elemskill": return StatShape.Element;
            case "item_reanimate": return StatShape.Monster;
            case "state": return StatShape.State;
        }

        // How it packs itself is the table's own answer, and a better one than the name:
        // two is a chance to cast, three a charged skill.
        switch (StatFields.EncodingOf(name))
        {
            case 2: return StatShape.Chance;
            case 3: return StatShape.Charges;
        }

        // descfunc 27 and 28 are the two single-skill forms, and 16 is the "Level [n]
        // [skill] ... When Equipped" one - item_aura and item_skillonequip.
        if (name is "item_singleskill" or "item_nonclassskill"
            || StatText.FunctionOf(name) is 16 or 27 or 28)
        {
            return StatShape.Skill;
        }

        return StatFields.RangeOf(name) is { Min: 0, Max: 1 } ? StatShape.Toggle : StatShape.Value;
    }

    /// <summary>The fields an editor offers for this shape, for a list someone has to read.</summary>
    public static string Fields(StatShape shape) => shape switch
    {
        StatShape.Toggle => "on/off",
        StatShape.Skill => L("skill, levels"),
        StatShape.Chance => L("skill, skill level, chance"),
        StatShape.Charges => L("skill, skill level, charges, max charges"),
        StatShape.SkillTree => L("skill tree, levels"),
        StatShape.Class => L("class, levels"),
        StatShape.Element => L("element, levels"),
        StatShape.Monster => L("monster, chance"),
        StatShape.State => "state, on/off",
        _ => "value",
    };
}
