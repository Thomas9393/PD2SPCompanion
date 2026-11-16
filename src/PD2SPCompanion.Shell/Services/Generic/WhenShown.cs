using System;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>
/// Work a page needs only while it is seen. PageDeck keeps hidden pages, and what they did on every
/// change - recounting backups, the rune wizard, a sale's search - went on for nobody: 20 ms of an
/// inventory move on a slower PC (measured). Asked while the page is hidden, the work is
/// owed and done once when it is shown; asked while it is shown, it is done at once.
/// <para>
/// <paramref name="afreshOnShow"/>: a page that reads everything again each time it is opened - what
/// changed on disk meanwhile raised no event - owes the work on every visit, asked or not.
/// </para>
/// </summary>
public sealed class WhenShown(Action work, bool afreshOnShow = false)
{
    private readonly bool _afreshOnShow = afreshOnShow;
    private bool _shown, _owed = afreshOnShow;

    public void Ask()
    {
        if (_shown)
            work();
        else
            _owed = true;
    }

    public void Shown()
    {
        _shown = true;
        if (!_owed)
            return;
        _owed = false;
        work();
    }

    /// <summary>
    /// What is owed, done now with the page still hidden: before it is laid out ahead of its first visit
    /// (PageDeck.Prebuild), so that layout is of what it will show.
    /// </summary>
    public void Settle()
    {
        if (!_owed)
            return;
        _owed = false;
        work();
    }

    public void Hidden()
    {
        _shown = false;
        if (_afreshOnShow)
            _owed = true;
    }

    /// <summary>
    /// For work that does itself a moment after it is asked - once for a burst, after what set it off has
    /// drawn: asked again as that moment comes. True where the page was put away meanwhile - the work is owed
    /// then, done when the page is next shown, and the caller leaves it. Without it the work went on behind
    /// another page for nobody, and on whatever had changed since (the home page's health mark, judged after
    /// its page was hidden - a test caught it in one run of five).
    /// </summary>
    public bool PutOff()
    {
        if (_shown)
            return false;

        _owed = true;
        return true;
    }
}
