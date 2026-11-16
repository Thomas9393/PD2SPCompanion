namespace PD2SPCompanion.Core.Graphics;

/// <summary>One body part a pose draws: its composit slot and the weapon class its DCC is named with.</summary>
/// <param name="Component">The <c>composit.txt</c> row: 0 HD, 1 TR, 2 LG, 3 RA, 4 LA, 5 RH, 6 LH, 7 SH, 8-15 S1-S8.</param>
/// <param name="WeaponClass">Three letters - often the pose's own (2HT), sometimes HTH for a part the weapon leaves alone.</param>
/// <param name="DrawEffect">How the layer blends; 0-6 are the game's transparency modes, anything else is drawn solid.</param>
public sealed record CofLayer(int Component, bool Shadow, bool Transparent, int DrawEffect, string WeaponClass);

/// <summary>
/// A COF: one pose of one character class with one weapon class (AMTN2HT: the Amazon standing in town
/// holding a two-handed thrusting weapon). It names the layers the pose is made of and, for every
/// direction and frame, the order they are drawn in - the right arm goes behind the body facing one
/// way and in front of it facing another.
/// </summary>
public sealed class Cof
{
    public required IReadOnlyList<CofLayer> Layers { get; init; }
    public required int Frames { get; init; }
    public required int Directions { get; init; }
    /// <summary>Frames a second is this over 256 x 25.</summary>
    public required int Speed { get; init; }
    private byte[] _order = [];

    public static string PathFor(string token, string mode, string weaponClass) =>
        $@"data\global\chars\{token}\COF\{token}{mode}{weaponClass}.cof";

    public static Cof Read(byte[] data)
    {
        if (data.Length < 28)
            throw new InvalidDataException($"COF is {data.Length} bytes, shorter than its header.");
        int layers = data[0], frames = data[1], directions = data[2];
        int at = 28;
        var list = new List<CofLayer>(layers);
        for (int i = 0; i < layers; i++, at += 9)
        {
            if (at + 9 > data.Length)
                throw new InvalidDataException("COF ends inside its layer list.");
            string wclass = System.Text.Encoding.ASCII.GetString(data, at + 5, 3).TrimEnd('\0');
            list.Add(new CofLayer(data[at], data[at + 1] != 0, data[at + 3] != 0, data[at + 4], wclass));
        }
        at += frames; // one event byte a frame: where an attack lands, a sound plays
        int orderLength = directions * frames * layers;
        if (at + orderLength > data.Length)
            throw new InvalidDataException("COF ends inside its drawing order.");
        return new Cof
        {
            Layers = list,
            Frames = frames,
            Directions = directions,
            Speed = BitConverter.ToInt16(data, 24),
            _order = data[at..(at + orderLength)],
        };
    }

    /// <summary>The components to draw, back to front, for one frame of one direction.</summary>
    public IReadOnlyList<int> Order(int direction, int frame)
    {
        int layers = Layers.Count, start = (direction * Frames + frame) * layers;
        return [.. _order[start..(start + layers)]];
    }
}
