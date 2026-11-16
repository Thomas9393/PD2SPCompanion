using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;

using Avalonia.Threading;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// The play mode a vault keeps:
/// a vault remembers the mode it is played under, by name - Legit, Casual, Free, or "Custom - name" with a
/// copy of that custom mode's rules, so it is whole when written out, brought in elsewhere or migrated.
/// <para>
/// <b>Never enforcement</b> - "a preference user can change anytime". Where the two differ, the editor asks,
/// at two moments: a vault opened under another play mode, and the play mode changed while a vault is open.
/// Two answers, each a click: use the vault's mode, or change the vault to this one. A vault that keeps none
/// yet takes the mode it is first opened under, unasked.
/// </para>
/// <para>
/// A custom mode this editor does not have comes into its list with the vault. One it has under the same name
/// with other rules is kept beside it as "name (OLD)" (a deliberately minimal versioning), and is the
/// vault's mode - so the two differ, and the ordinary question is asked. A custom mode edited under its own
/// name is the same mode: the vault open at the time takes the new rules unasked.
/// </para>
/// <para>
/// PD2SPC_VAULT_MODE=off (the UI tests set it, as PD2SPC_FOLDS) keeps nothing and asks nothing: the tests start
/// the editor under one mode after another on one pad, whose vault would otherwise remember the last.
/// </para>
/// </summary>
public sealed class VaultPlayMode
{
    private readonly VaultService _vaults;
    private readonly EditorSettings _settings;
    private readonly ConfirmService _confirm;
    private readonly CharacterSession _session;

    /// <summary>Switched off for a test run (see the class's note).</summary>
    private static bool Off =>
        string.Equals(Environment.GetEnvironmentVariable("PD2SPC_VAULT_MODE"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mode is being set from the vault's: not a choice to ask about.</summary>
    private bool _applying;

    public VaultPlayMode(VaultService vaults, EditorSettings settings, ConfirmService confirm, CharacterSession session)
    {
        _vaults = vaults;
        _settings = settings;
        _confirm = confirm;
        _session = session;

        // Opened from inside whatever read it first: looked at once that read is done.
        vaults.VaultOpened += () => Dispatcher.UIThread.Post(Check, DispatcherPriority.Background);
        settings.ModeChosen += Chosen;
    }

    /// <summary>The open vault's mode held against the editor's: taken where the vault keeps none, asked about where they differ.</summary>
    public void Check()
    {
        if (Off || _applying || _vaults.Store is not { } vault)
            return;

        string kept = Kept(vault);
        if (kept.Length == 0)
        {
            Write(vault);
            return;
        }

        if (kept != _settings.ModeLabel)
            Ask(kept);
    }

    /// <summary>A mode was chosen in Settings: the open vault follows where it is the same mode, and is asked about where it is another.</summary>
    private void Chosen()
    {
        if (Off || _applying || _vaults.Store is not { } vault)
            return;

        string kept = vault.About(VaultLibrary.PlayModeKey) ?? "";

        // None kept yet; or the same mode by name, its rules edited in place (silently); or the
        // custom mode that is the vault's renamed - its old name gone from the list, its rules the same.
        bool renamed = PlayPolicy.NameInLabel(kept) is { } was && _settings.Custom(was) is null && Rules(vault) == _settings.Policy;
        if (kept.Length == 0 || kept == _settings.ModeLabel || renamed)
        {
            Write(vault);
            return;
        }

        Ask(Kept(vault));
    }

    /// <summary>
    /// The mode the vault keeps, as a label, a custom one's rules brought into the editor's list on the way
    /// (see the class's note); "" where it keeps none, or one that cannot be made out.
    /// </summary>
    private string Kept(PD2SPCompanion.Core.Collection.Vault vault)
    {
        string kept = vault.About(VaultLibrary.PlayModeKey) ?? "";
        if (PlayPolicy.NameInLabel(kept) is not { } name)
            return PlayPolicy.Preset(kept) is not null ? kept : "";

        if (Rules(vault) is not { } rules)
            return "";

        // Rules that are a preset's are that preset, whatever they were called.
        if (PlayPolicy.PresetNameOf(rules) is { } preset)
            return Relabel(vault, preset);

        if (_settings.Custom(name) is not { } mine)
        {
            _settings.KeepCustom(name, rules);
            _settings.Save();
            return kept;
        }

        if (mine.Policy == rules)
            return kept;

        // The same name, other rules: the vault's stay its own, under the name with "(OLD)".
        string old = _settings.CustomPresets.FirstOrDefault(each =>
                         each.Policy == rules && each.Name.StartsWith(name + " (OLD", StringComparison.OrdinalIgnoreCase))?.Name
                     ?? FreeOld(name);
        _settings.KeepCustom(old, rules);
        _settings.Save();
        return Relabel(vault, PlayPolicy.CustomLabel(old));
    }

    private string Relabel(PD2SPCompanion.Core.Collection.Vault vault, string label)
    {
        _vaults.WriteQuietly(() =>
        {
            vault.SetAbout(VaultLibrary.PlayModeKey, label);
            return 0;
        });
        _vaults.SayListed();
        return label;
    }

    /// <summary>"name (OLD)", or with a number where that is taken by yet other rules.</summary>
    private string FreeOld(string name)
    {
        for (int n = 1; ; n++)
        {
            string old = n == 1 ? $"{name} (OLD)" : $"{name} (OLD {n})";
            if (_settings.Custom(old) is null)
                return old;
        }
    }

    private static PlayPolicy? Rules(PD2SPCompanion.Core.Collection.Vault vault)
    {
        try
        {
            return vault.About(VaultLibrary.PlayRulesKey) is { Length: > 0 } rules
                ? JsonSerializer.Deserialize<PlayPolicy>(rules)?.Normalized()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The editor's mode written into the vault: its name, and for a custom one its rules.</summary>
    private void Write(PD2SPCompanion.Core.Collection.Vault vault)
    {
        string label = _settings.ModeLabel;
        string rules = _settings.PlayMode == PlayPolicy.CustomName ? JsonSerializer.Serialize(_settings.Policy) : "";
        if (vault.About(VaultLibrary.PlayModeKey) == label && (vault.About(VaultLibrary.PlayRulesKey) ?? "") == rules)
            return;

        _vaults.WriteQuietly(() =>
        {
            vault.SetAbout(VaultLibrary.PlayModeKey, label);
            vault.SetAbout(VaultLibrary.PlayRulesKey, rules);
            return 0;
        });
        _vaults.SayListed();
    }

    /// <summary>The question, once no other is on screen: the vault's mode, or the vault changed to the editor's.</summary>
    private void Ask(string kept)
    {
        if (_confirm.IsAsking)
        {
            void Asked(object? sender, PropertyChangedEventArgs changed)
            {
                if (changed.PropertyName != nameof(ConfirmService.IsAsking) || _confirm.IsAsking)
                    return;

                _confirm.PropertyChanged -= Asked;
                Dispatcher.UIThread.Post(Check, DispatcherPriority.Background);
            }

            _confirm.PropertyChanged += Asked;
            return;
        }

        string vault = _vaults.VaultName;
        _confirm.Ask(L("Play mode"),
                     L($"{vault} is a {kept} vault. Play mode is {_settings.ModeLabel}."),
                     L($"USE {kept.ToUpperInvariant()}"),
                     () => Use(kept),
                     cancelLabel: L("CHANGE VAULT"),
                     onCancel: () => Change(vault),
                     needsClick: true);
    }

    /// <summary>The editor takes the vault's mode.</summary>
    private void Use(string kept)
    {
        _applying = true;
        try
        {
            if (PlayPolicy.NameInLabel(kept) is { } custom)
                _settings.ChooseCustom(custom);
            else if (PlayPolicy.Preset(kept) is { } preset)
                _settings.ChoosePolicy(preset);
        }
        finally
        {
            _applying = false;
        }

        _session.Status = L($"Play mode: {_settings.ModeLabel}.");
    }

    /// <summary>The vault takes the editor's mode - where it is still the vault that was asked about.</summary>
    private void Change(string asked)
    {
        if (_vaults.Store is not { } vault || !string.Equals(asked, _vaults.VaultName, StringComparison.OrdinalIgnoreCase))
            return;

        Write(vault);
        _session.Status = L($"{asked}: {_settings.ModeLabel} vault.");
    }
}
