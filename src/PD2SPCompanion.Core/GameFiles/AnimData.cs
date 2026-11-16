namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// <c>data\global\animdata.d2</c>: how many frames each animation runs, at what speed, and on which frame it
/// acts - what cast rate, hit recovery, block rate and attack speed are measured against (the character
/// sheet). PD2 ships its own, in <c>pd2assets.mpq</c> (607,904
/// bytes, 3,793 records). Read from the player's install, as every table is - no breakpoint
/// is written down in the program.
/// <para>
/// A record is named by the unit's token, the mode and the weapon class, run together: <c>AMSC2HT</c> is the
/// Amazon (AM) casting (SC) with a two-handed thrusting weapon (2HT); a monster's token is its monstats
/// <c>Code</c> (the wolf form is <c>40</c>, the bear <c>TG</c>). The file is 256 hash buckets, each a record
/// count and then 160-byte records - an 8-byte name, frames per direction, animation speed, and 144 bytes of
/// frame events: the first frame that holds one is the frame the animation acts on.
/// </para>
/// </summary>
public static class AnimData
{
    /// <summary>
    /// One animation: frames per direction, its speed - 256 is the engine's full speed - and the frame it acts
    /// on (the first with an event; 0 where it has none).
    /// </summary>
    public sealed record Animation(int Frames, int Speed, int ActionFrame);

    private static Dictionary<string, Animation> _animations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each class's token in animation names, by class id.</summary>
    private static readonly string[] Tokens = ["AM", "SO", "NE", "PA", "BA", "DZ", "AI"];

    public static bool IsLoaded => _animations.Count > 0;

    /// <summary>Every animation by its name, for a look at the file (<c>pd2spc anim</c>).</summary>
    public static IReadOnlyDictionary<string, Animation> All => _animations;

    public static void Load(GameData game)
    {
        var found = new Dictionary<string, Animation>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Read(@"data\global\animdata.d2") is { } hit)
            {
                var data = hit.Data;
                int at = 0;
                for (int bucket = 0; bucket < 256 && at + 4 <= data.Length; bucket++)
                {
                    int count = BitConverter.ToInt32(data, at);
                    at += 4;

                    for (int i = 0; i < count && at + 160 <= data.Length; i++)
                    {
                        string name = System.Text.Encoding.ASCII.GetString(data, at, 8).TrimEnd((char)0);
                        int frames = BitConverter.ToInt32(data, at + 8);
                        int action = 0;
                        for (int frame = 0; frame < frames && frame < 144; frame++)
                        {
                            if (data[at + 16 + frame] != 0)
                            {
                                action = frame;
                                break;
                            }
                        }

                        found[name] = new Animation(frames, BitConverter.ToInt32(data, at + 12), action);
                        at += 160;
                    }
                }
            }
        }
        catch (Exception problem)
        {
            // No animations: the sheet's breakpoint rows say nothing rather than something wrong.
            Log.Caught(problem);
        }

        _animations = found;
    }

    /// <summary>A class's token: <c>AM</c> for the Amazon.</summary>
    public static string? TokenOf(int classId) => classId >= 0 && classId < Tokens.Length ? Tokens[classId] : null;

    /// <summary>A unit's animation in a mode with a weapon class: <c>("AM", "SC", "2HT")</c>; hand to hand where the class is blank.</summary>
    public static Animation? Of(string token, string mode, string weaponClass) =>
        _animations.GetValueOrDefault($"{token}{mode}{(weaponClass.Length > 0 ? weaponClass : "HTH")}");

    /// <summary>A class's animation in a mode with a weapon class: <c>(0, "SC", "2HT")</c>.</summary>
    public static Animation? Of(int classId, string mode, string weaponClass) =>
        TokenOf(classId) is { } token ? Of(token, mode, weaponClass) : null;
}
