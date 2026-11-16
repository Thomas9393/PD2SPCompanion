namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// The game's own skill icons, one sheet per class.
/// <para>
/// Each class has a <c>&lt;xx&gt;skillicon.dc6</c> of 48x48 frames, two per skill - the
/// normal face and the pressed one - and <c>skilldesc.txt</c>'s <c>IconCel</c> indexes
/// straight to the first of the pair. Six of the seven sheets come from
/// <c>pd2assets.mpq</c>, so these are PD2's icons rather than vanilla's.
/// </para>
/// </summary>
public sealed class SkillIcons(GameData game, Palette palette)
{
    /// <summary>The file prefix each class's sheet uses.</summary>
    private static readonly string[] ClassPrefixes = ["am", "so", "ne", "pa", "ba", "dr", "as", ""];

    /// <summary>The sheet of the skills of no class - Attack, Throw, Unsummon - in place of a class id.</summary>
    public const byte NoClass = 7;

    private readonly Dictionary<byte, IReadOnlyList<Dc6Frame>?> _sheets = [];

    public static SkillIcons? Current { get; private set; }

    public static SkillIcons Install(GameData game, Palette palette) =>
        Current = new SkillIcons(game, palette);

    /// <summary>
    /// One skill's icon, or null when the sheet or the frame is not there. A missing icon is
    /// not an error - the tree still reads without it.
    /// </summary>
    public Sprite? Load(byte classId, int iconCel)
    {
        if (iconCel < 0)
            return null;

        var frames = Sheet(classId);
        if (frames is null || iconCel >= frames.Count)
            return null;

        var frame = frames[iconCel];
        return new Sprite(
            InvFile: $"skillicon-{classId}-{iconCel}",
            Width: frame.Width,
            Height: frame.Height,
            Bgra: frame.ToBgra(palette));
    }

    private IReadOnlyList<Dc6Frame>? Sheet(byte classId)
    {
        if (_sheets.TryGetValue(classId, out var cached))
            return cached;

        IReadOnlyList<Dc6Frame>? frames = null;
        try
        {
            string prefix = classId < ClassPrefixes.Length ? ClassPrefixes[classId] : ClassPrefixes[0];
            var hit = game.Read($@"data\global\ui\SPELLS\{prefix}skillicon.dc6");
            if (hit is not null)
                frames = Dc6.Read(hit.Value.Data);
        }
        catch (Exception)
        {
            // A sheet that will not decode simply leaves its class without icons.
        }

        _sheets[classId] = frames;
        return frames;
    }
}
