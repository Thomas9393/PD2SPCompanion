namespace PD2SPCompanion.Tests;

/// <summary>
/// The "UI scale" Automatic chooses (ScreenFit): the reference monitor is
/// left as it is, a screen with less room is given the reference's or the nearest under it, never more than a
/// step from what Windows asks, and a grid's cell lands on whole pixels.
/// </summary>
public class ScreenFitTests
{
    /// <summary>A screen's working area in the window's units: its pixels over Windows' scale, less the taskbar's 48 units.</summary>
    private static (double Width, double Height) Room(int wide, int tall, double scaling) => (wide / scaling, tall / scaling - 48);

    private static double Auto(int wide, int tall, double scaling)
    {
        var (width, height) = Room(wide, tall, scaling);
        return ScreenFit.Automatic(width, height, scaling);
    }

    [Fact]
    public void TheReferenceMonitorIsLeftAsItIs()
    {
        // 2560 x 1440 at 125%: 2048 x 1104 units, the room the layout was drawn for.
        Assert.Equal(1, Auto(2560, 1440, 1.25));

        // And what is nearly that: a taller taskbar, a screen a little larger.
        Assert.Equal(1, ScreenFit.Automatic(2048, 1080, 1.25));
        Assert.Equal(1, ScreenFit.Automatic(2200, 1200, 1.25));
    }

    [Theory]
    [InlineData(1920, 1080, 1.0, 26, 28)]    // 1920 x 1032 units: under the reference by a sixteenth; 26 pixels to the cell
    [InlineData(1920, 1080, 1.25, 27, 35)]   // 1536 x 816: held at three quarters, and up to the cell's next whole pixel
    [InlineData(1920, 1080, 1.5, 32, 42)]    // 1280 x 680: the least layout did not fit; scaled, it has 1680 x 892
    [InlineData(1920, 1080, 1.75, 37, 49)]
    [InlineData(2560, 1440, 1.0, 35, 28)]    // more room than the reference: drawn a quarter larger, the reference's composition
    [InlineData(3840, 2160, 1.5, 52, 42)]    // a 4K screen at 150%: 2560 x 1400 units, held at a step over
    public void OtherScreensGetTheReferencesRoom_OnWholePixels(int wide, int tall, double scaling, int pixels, int cell)
    {
        double scale = Auto(wide, tall, scaling);

        Assert.Equal((double)pixels / cell, scale, 6);

        // A cell is a whole number of screen pixels.
        double drawn = ScreenFit.Cell * scaling * scale;
        Assert.Equal(Math.Round(drawn), drawn, 6);

        // Never further than a step from Windows' own scale.
        Assert.InRange(scale, ScreenFit.Least - 0.0001, ScreenFit.Most + 0.0001);
    }

    [Fact]
    public void TheLeastLayoutFitsEveryScreenFromFullHdUp()
    {
        // The window's least layout is 1280 x 720 units and its caption; at 1920 x 1080 and 150% the room is
        // 1280 x 680, and the window's foot lay under the taskbar.
        foreach (double scaling in new[] { 1.0, 1.25, 1.5, 1.75 })
        {
            var (width, height) = Room(1920, 1080, scaling);
            double scale = ScreenFit.Automatic(width, height, scaling);
            Assert.True(width / scale >= 1280, $"{scaling}: {width / scale} units wide");
            Assert.True(height / scale >= 750, $"{scaling}: {height / scale} units tall");
        }
    }

    [Fact]
    public void NothingToGoOn_IsOne()
    {
        Assert.Equal(1, ScreenFit.Automatic(0, 0, 1));
        Assert.Equal(1, ScreenFit.Automatic(1920, 1040, 0));
    }
}
