using System.Security.Cryptography;

using PD2SPCompanion.Core.Graphics;

namespace PD2SPCompanion.Tests.GameFiles;

/// <summary>
/// The window frame's and the logo's recipes (Core's FrameArt and FrontEnd): each picture the editor cuts from
/// the install, by its size and a fingerprint of its pixels, plain and enlarged with Scale2x as the pages show
/// it. The values are those of S13's archives, where every one was compared
/// with the art the editor used to ship - so a change to the art code that moves a pixel fails here.
/// <para>
/// A season that redraws a piece fails here too, and is right to: the recipes cut by row and column, so a
/// redrawn piece needs looking at before its fingerprint is taken again.
/// </para>
/// </summary>
[Collection(nameof(GameTables))]
public sealed class FrameArtTests(Game game)
{
    [GameFact]
    public void Every_recipe_cuts_the_picture_it_did()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var logo = FrontEnd.Logo(data);
        var border = FrameArt.Border(data);
        var bar = FrameArt.Bar(data);

        // The resolution mod's archive and the base game's front end are both part of a PD2 install.
        Assert.NotNull(logo);
        Assert.NotNull(border);
        Assert.NotNull(bar);

        Assert.Equal("337x122 124E590796376EE1 x2 3CA3CF67CF62E6B0", Print(logo));
        Assert.Equal("337x136 46A12411A729DD47 x2 41FAA6C8E0DECF98", Print(FrontEnd.CentredOnLetters(logo)));
        Assert.Equal("252x27 E1BBE74B9501D5FC x2 21D9B89C3E1C6A7C", Print(border));
        Assert.Equal("3024x27 4CD4EA98162C5862 x2 E54685241EC6D7DD", Print(PixelArt.MirroredAcross(border, 12)));
        Assert.Equal("48x251 0B1EAE7862AAB022 x2 BF29E064DD929F10", Print(bar));
        Assert.Equal("48x2008 046E70281DDEC9EE x2 CBA6C51F449C2FFA", Print(PixelArt.MirroredDown(bar, 8)));
        Assert.Equal("54x54 1B4A80AA04CEAF90 x2 C7945B91225F966B", Print(FrameArt.Corner(data)));
        Assert.Equal("76x102 1F98ACC40566F9A8 x2 BADAC5A7C170988B", Print(FrameArt.Ornament(data)));
        Assert.Equal("254x254 3451699B954D7082 x2 D0B5A6BE61978BC7", Print(FrameArt.WindowBackground(data)));
        Assert.Equal("184x164 FA2CDAF96E3B92E9 x2 D2936E61CAB3D442", Print(FrameArt.PageBackground(data)));
    }

    [GameFact]
    public void The_burning_logo_is_thirty_frames_on_one_canvas()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var burning = FrontEnd.BurningLogo(data);
        Assert.Equal(30, burning.Count);
        Assert.All(burning, frame => Assert.Equal((burning[0].Width, burning[0].Height), (frame.Width, frame.Height)));
        Assert.Equal("40485DAF8BDC255F", Hash([.. burning.SelectMany(frame => frame.Bgra)]));
    }

    /// <summary>A picture's size and fingerprint, and the fingerprint of its Scale2x enlargement.</summary>
    private static string Print(Picture? picture) =>
        picture is null
            ? "missing"
            : $"{picture.Width}x{picture.Height} {Hash(picture.Bgra)} x2 {Hash(PixelArt.Scale2x(picture.Bgra, picture.Width, picture.Height))}";

    private static string Hash(uint[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes))[..16];
    }
}
