using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The Sell tab's search for listings like the marked item: the vault's search form, filled from the
/// item - its name or type, rarity, states, sockets, and every property it has at four fifths of its
/// roll - for the player to edit, and sent to the trade site when they say. Nothing is asked on its
/// own. See <see cref="TradeSearch"/>.
/// </summary>
public sealed partial class SellSearchViewModel : ObservableObject
{
    private readonly Item _item;
    private readonly bool _hardcore;
    private CancellationTokenSource? _searching;

    public SellSearchViewModel(Item item, bool hardcore)
    {
        _item = item;
        _hardcore = hardcore;
        Form = new SearchForm(new VaultFilter(), sfxManager: null, inVault: false);
        Reset();
    }

    /// <summary>The form, over a search of its own - never the vault's.</summary>
    public SearchForm Form { get; }

    /// <summary>What came back.</summary>
    [ObservableProperty] private IReadOnlyList<SimilarListing> _results = [];

    /// <summary>The panel's line: how the search went.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>Everything back to the item's own.</summary>
    [RelayCommand]
    private void Reset()
    {
        Stop();
        var filter = Form.Filter;
        var start = TradeSearch.PrefillFor(_item);

        filter.Apply(new SearchPreset
        {
            ItemName = start.Name,
            Rarity = start.Rarity,
            TypeFrom = start.TypeFrom,
            TypeTo = start.TypeTo,
            Base = start.Base,
            Ethereal = start.Ethereal,
            Corrupted = start.Corrupted,
            Desecrated = start.Desecrated,
            SocketsMin = start.SocketsMin?.ToString(CultureInfo.InvariantCulture),
        });

        foreach (var property in start.Properties)
        {
            if (PropertyIndex.Options.FirstOrDefault(option => option.Key == property.Option) is not { } option)
                continue;

            var row = filter.AddProperty(option, Text(property.Roll));
            row.Skill = row.SkillChoices.FirstOrDefault(choice => choice.From == property.Skill) ?? VaultFilter.Any;
            row.Min = Text(property.Min);
            row.Max = Text(property.Max);
        }

        Results = [];
        Status = "";
    }

    /// <summary>
    /// The newest ten ladder listings meeting what is set, through the trade site's gate - asked the
    /// same again within ten minutes, answered from memory. Asking prices, not sales, and not
    /// averaged: the price is the player's to name.
    /// </summary>
    [RelayCommand]
    private async Task Search()
    {
        _searching?.Cancel();
        var searching = _searching = new CancellationTokenSource();

        Results = [];
        Status = "Searching...";

        try
        {
            var found = await TradeSearch.Search(Form.Filter.Query(), _hardcore, searching.Token);
            if (searching.IsCancellationRequested)
                return;

            var rows = new List<SimilarListing>();
            foreach (var listing in found)
            {
                try
                {
                    rows.Add(new SimilarListing(listing, TradeItems.Build(listing.Item, shown: true).Item));
                }
                catch (Exception)
                {
                    // One the rebuild cannot draw is left out, not the whole search.
                }
            }

            Results = rows;
            Status = rows.Count == 0 ? L("No live listings match.") : L($"Newest {rows.Count}.");
        }
        catch (OperationCanceledException) when (searching.IsCancellationRequested)
        {
            // Another search, the form reset, or the panel closed.
        }
        catch (TradeGate.TradeGateException refused)
        {
            Status = refused.Message;
        }
    }

    /// <summary>Stops a search on its way - the item unmarked, or another marked.</summary>
    public void Stop() => _searching?.Cancel();

    private static string Text(double? value) =>
        value is { } number ? number.ToString("0.##", CultureInfo.InvariantCulture) : "";
}
