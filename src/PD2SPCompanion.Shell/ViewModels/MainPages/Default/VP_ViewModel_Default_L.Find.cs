using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;

namespace PD2SPCompanion.Shell.ViewModels;

// Find across characters: the one standing
// search asked of every character on disk, from the list that works with nothing open. Read only - each card
// says what was found on it; to act, the character is opened, and the search is there with it.
// The page has no box of its own to type the search in; the window's search box asks every character
// (QuickSearchViewModel), and the cards follow the standing search.
public partial class VP_ViewModel_Default_L
{
    /// <summary>The standing search over a character's items, with a scratch index of its own.</summary>
    private SavedSearches? _finder;

    /// <summary>Which asking the cards are being marked for; a newer one stops an older.</summary>
    private int _findRound;

    private DispatcherTimer? _findSoon;

    /// <summary>Whether a search stands that a character can be asked.</summary>
    public bool IsFinding => _finder?.IsActive == true;

    /// <summary>How many of the characters it was found on, once every one has been asked.</summary>
    public string FoundOn { get; private set; } = "";

    /// <summary>The count beside the title: the characters there are, or - a search standing - how many have it.</summary>
    public string CountLine => IsFinding && FoundOn.Length > 0 ? FoundOn : CharacterCount;

    /// <summary>Hooked once, in the constructor: the cards follow the search, whoever changed it.</summary>
    private void FollowSearch()
    {
        if (_vaults is null)
            return;

        _finder = new SavedSearches(_vaults.Filter);
        _vaults.Filter.Changed += FindSoon;
    }

    /// <summary>The cards marked a moment after the last change: a name is typed a letter at a time.</summary>
    private void FindSoon()
    {
        SayFind();
        _findSoon ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _findSoon!.Stop();
            _ = FindAsync();
        });
        _findSoon.Stop();
        _findSoon.Start();
    }

    private void SayFind()
    {
        OnPropertyChanged(nameof(IsFinding));
        OnPropertyChanged(nameof(CountLine));
    }

    /// <summary>
    /// Every card told what the standing search finds on its character: one character at a time, the page
    /// answering in between, each save read once and kept until its file changes (<see cref="SavedItems"/>).
    /// Asked only while the list shows - with a character open the Items page answers for it. Nothing stands:
    /// every mark is taken off.
    /// </summary>
    private async System.Threading.Tasks.Task FindAsync()
    {
        int round = ++_findRound;
        var cards = Characters.ToList();
        if (_finder is not { IsActive: true } finder || !IsBrowsable)
        {
            foreach (var card in cards)
            {
                card.Finds = [];
                card.IsFindMiss = false;
            }
            FoundOn = "";
            SayFind();
            return;
        }

        void Restore()
        {
            if (Session.IsLoaded)
                Session.UseMyLayout();
        }

        int on = 0;
        foreach (var card in cards)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (round != _findRound)
                return;

            var lines = new List<string>();
            try
            {
                if (SavedItems.Of(card.Path, Restore) is { } held)
                {
                    // The index writes each item as its own save's layout has it.
                    SaveVerifier.Install(held.Format);
                    var found = finder.Matching(card.Path, held) ?? [];
                    lines.AddRange(held.All.Where(found.Contains)
                                           .Select(item => $"{ItemPresenter.Describe(item).Name} ({SavedItems.PlaceOf(held, item)})"));
                }
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
            }
            finally
            {
                Restore();
            }

            card.Finds = lines;
            card.IsFindMiss = lines.Count == 0;
            if (lines.Count > 0)
                on++;
        }

        FoundOn = L($"Found on {on} of {cards.Count}");
        SayFind();
    }
}
