namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// What a class starts with, and what each attribute point buys it, from
/// <c>charstats.txt</c>.
/// <para>
/// The starting values are also the floor: the game will not let a character have less
/// strength than its class began with, so neither will a respec here. PD2 ships its own
/// table, so these are read rather than assumed - Amazons and Barbarians do not start alike
/// and a season may move any of it.
/// </para>
/// </summary>
public sealed record ClassStats(
    string Name,
    int Strength,
    int Dexterity,
    int Energy,
    int Vitality,
    int LifePerVitality,
    int StaminaPerVitality,
    int ManaPerEnergy,
    int LifePerLevel = 0,
    int StaminaPerLevel = 0,
    int ManaPerLevel = 0,
    int StatPerLevel = 0,
    int ToHitFactor = 0,
    int BlockFactor = 0)
{
    /// <summary>
    /// The per-point columns are quarters: an Amazon's <c>LifePerVitality</c> of 12 is three
    /// life a point, a Barbarian's 16 is four.
    /// </summary>
    public const int PerPointDivisor = 4;

    /// <summary>Vanilla's values, used only when the table cannot be read.</summary>
    public static ClassStats Unknown { get; } =
        new("Unknown", 10, 10, 10, 10, 8, 4, 8);

    /// <summary>The floor for an attribute, or null if it is not one of the four.</summary>
    public int? Minimum(string attribute) => attribute.ToLowerInvariant() switch
    {
        "strength" => Strength,
        "dexterity" => Dexterity,
        "vitality" => Vitality,
        "energy" => Energy,
        _ => null,
    };

    public static ClassStats For(GameData game, byte classId)
    {
        string wanted = CharacterClasses.NameOf(classId) ?? CharacterClasses.Names[0];

        try
        {
            var table = game.Excel("charstats");

            int nameColumn = table.IndexOfColumn("class");
            if (nameColumn < 0)
                return Unknown;

            foreach (var fields in table.Rows)
            {
                if (nameColumn >= fields.Length
                    || !fields[nameColumn].Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int Column(string name)
                {
                    int i = table.IndexOfColumn(name);
                    return i >= 0 && i < fields.Length && int.TryParse(fields[i], out int value)
                        ? value
                        : 0;
                }

                return new ClassStats(
                    Name: wanted,
                    Strength: Column("str"),
                    Dexterity: Column("dex"),
                    // The table calls energy "int", as the game's own stat name is Energy
                    // but its column is magic.
                    Energy: Column("int"),
                    Vitality: Column("vit"),
                    LifePerVitality: Column("LifePerVitality"),
                    StaminaPerVitality: Column("StaminaPerVitality"),
                    ManaPerEnergy: Column("ManaPerMagic"),
                    // What a level buys: quarters again, and the attribute points to spend.
                    LifePerLevel: Column("LifePerLevel"),
                    StaminaPerLevel: Column("StaminaPerLevel"),
                    ManaPerLevel: Column("ManaPerLevel"),
                    StatPerLevel: Column("StatPerLevel"),
                    // Attack rating before any gear is dexterity x 5 + this - 35, and a shield blocks its own
                    // chance + this (the character sheet).
                    ToHitFactor: Column("ToHitFactor"),
                    BlockFactor: Column("BlockFactor"));
            }
        }
        catch (Exception)
        {
            // Without the table there is no floor to enforce, so editing is refused rather
            // than allowed against a guess - see AttributeEditor.
        }

        return Unknown;
    }
}
