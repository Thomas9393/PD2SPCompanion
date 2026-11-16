using PD2SPCompanion.Shell.Helper.ViewLocator;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace PD2SPCompanion.Shell;
public partial class App
{
    private static void SvcReg(IServiceCollection services)
    {
        #region UI Layer
        //Generic
        SvcReg_UI_UX(services);

        //UI-Only Services
        SvcReg_UI_Services(services);
        //MVVM
        SvcReg_UI_Views(services);
        SvcReg_UI_ViewModels(services);
        #endregion
    }

    #region UI Layer
    #region User Experience Services
    private static void SvcReg_UI_UX(IServiceCollection services)
    {
        #region SFX
        services.AddSingleton<ISFXManager, SFXManager>();
        #endregion

        #region Character
        // One loaded save, shared by every page.
        services.AddSingleton<CharacterSession>();

        // The character and the shared stash are two files saved as one thing. See SaveSet.
        services.AddSingleton<SaveSet>();

        // Its own file, belonging to no character, so it is a service of its own and
        // not part of the character session.
        services.AddSingleton<SharedStashSession>();

        // The compendium of the open character's mode, a third file of the set: the Grail page stores into it.
        services.AddSingleton<CompendiumSession>();

        // Where a duplicate or an ejected gem lands (the item page sets its open panes on it).
        services.AddSingleton<PD2SPCompanion.Shell.Services.Character.ItemLanding>();

        // The game writes the same files - see GameSync.
        services.AddSingleton<PatchGuardService>();
        services.AddSingleton<GameSync>();

        // Into the compendium and out: the Grail page's arrows, an item's menu and its key - see CompendiumMoves.
        services.AddSingleton<PD2SPCompanion.Shell.Services.Character.CompendiumMoves>();
        services.AddSingleton<ConfirmService>();
        services.AddSingleton<PD2SPCompanion.Shell.Services.Settings.WhatsNewService>();
        services.AddSingleton<PD2SPCompanion.Shell.Services.Vault.VaultService>();
        services.AddSingleton<PD2SPCompanion.Shell.Services.Vault.SeasonMigrationService>();
        services.AddSingleton<PD2SPCompanion.Shell.Services.Vault.VaultPlayMode>();

        // The vault's half of a move, written with the character - see VaultMoves.
        services.AddSingleton<PD2SPCompanion.Shell.Services.Vault.VaultMoves>();

        // Saving by itself, and the session's history of what it saved - see AutoSave.
        services.AddSingleton<SessionHistory>();
        services.AddSingleton<AutoSave>();
        services.AddSingleton<PD2SPCompanion.Shell.Services.Vault.RestoreReconcile>();
        services.AddSingleton<PD2SPCompanion.Shell.Services.Vault.DuplicateIndex>();
        services.AddSingleton<PD2SPCompanion.Shell.ViewModels.SearchPanelViewModel>();
        services.AddSingleton<PD2SPCompanion.Shell.ViewModels.WaypointsViewModel>();
        services.AddSingleton<PD2SPCompanion.Shell.ViewModels.QuestsViewModel>();
        services.AddSingleton<PD2SPCompanion.Shell.ViewModels.MapSeedViewModel>();

        // What was bought and sold, written with the save - see ItemHistory.
        services.AddSingleton<PD2SPCompanion.Shell.Services.Vault.ItemHistory>();
        #endregion

        #region Settings
        // Read once at startup, which also applies them to the core.
        services.AddSingleton(EditorSettings.Load());

        // The play mode as the pages ask it - see PlayRules.
        services.AddSingleton<PlayRules>();

        // Asks for the game folder where the settings do not already name one. Built
        // eagerly by the main view model, so the question is up before anything reads
        // from the folder.
        services.AddSingleton<GameFolderSetup>();
        #endregion
    }
    #endregion

    #region UI-only Services
    private static void SvcReg_UI_Services(IServiceCollection services)
    {
        #region ViewLocator
        services.AddSingleton<MainViewPage_ViewLocator>();
        #endregion
    }
    #endregion

    #region MVVM
    #region Views
    private static void SvcReg_UI_Views(IServiceCollection services)
    {
        //Main Views
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainView>();

        //Main Pages
        services.AddSingleton<VP_View_Default_L>();
        services.AddSingleton<VP_View_Default_R>();

        services.AddSingleton<VP_View_Char_L>();
        services.AddSingleton<VP_View_Char_R>();

        services.AddSingleton<VP_View_Inv_L>();

        services.AddSingleton<VP_View_Settings_L>();
        services.AddSingleton<VP_View_Settings_R>();

        services.AddSingleton<VP_View_Vault_L>();
        // The vault list shown on the right page of the Items book: a second view, same view model.
        services.AddKeyedSingleton<VP_View_Vault_L>(PD2SPCompanion.Shell.Helper.ViewLocator.MainViewPage_ViewLocator.RightSide);
        services.AddSingleton<VP_View_Vault_R>();
        services.AddSingleton<VP_View_Trade>();
        services.AddSingleton<VP_View_Extra_L>();
        services.AddSingleton<VP_View_Extra_R>();

        services.AddSingleton<VP_View_Experimental_L>();
        services.AddSingleton<VP_View_Experimental_R>();
    }
    #endregion

    #region ViewModels
    private static void SvcReg_UI_ViewModels(IServiceCollection services)
    {
        //Main Views
        services.AddSingleton<MainViewModel>();

        //Main Pages
        services.AddSingleton<VP_ViewModel_Default_L>();
        services.AddSingleton<VP_ViewModel_Default_R>();

        services.AddSingleton<VP_ViewModel_Char_L>();
        services.AddSingleton<VP_ViewModel_Char_R>();

        services.AddSingleton<VP_ViewModel_Inv_L>();
        services.AddSingleton<VP_ViewModel_Inv_R>();

        services.AddSingleton<VP_ViewModel_Settings_L>();
        services.AddSingleton<VP_ViewModel_Settings_R>();

        services.AddSingleton<VP_ViewModel_Vault_L>();

        // The vault on the right page of the Items book: a vault page of its own over the same vault, with its
        // own folder, selection and look (VP_ViewModel_Vault_L.Twin.cs), shown by the right page's own view.
        services.AddKeyedSingleton<VP_ViewModel_Vault_L>(MainViewPage_ViewLocator.RightSide, (found, _) =>
            new VP_ViewModel_Vault_L(
                found.GetRequiredService<CharacterSession>(),
                found.GetRequiredService<EditorSettings>(),
                found.GetRequiredService<ISFXManager>(),
                found.GetRequiredService<ConfirmService>(),
                found.GetRequiredService<PD2SPCompanion.Shell.Services.Vault.VaultService>(),
                found.GetRequiredService<SharedStashSession>(),
                found.GetRequiredService<PlayRules>(),
                found.GetRequiredService<PD2SPCompanion.Shell.Services.Vault.VaultMoves>(),
                found.GetRequiredService<PD2SPCompanion.Shell.Services.Character.ItemLanding>(),
                found.GetService<GameSync>(),
                found.GetService<SearchPanelViewModel>())
            {
                PageName = VP_ViewModel_Vault_L.RightPage,
            });
        services.AddSingleton<VP_ViewModel_Vault_R>();
        services.AddSingleton<VP_ViewModel_Trade>();
        services.AddSingleton<VP_ViewModel_Extra_L>();
        services.AddSingleton<VP_ViewModel_Extra_R>();
        services.AddSingleton<VP_ViewModel_Experimental_L>();
        services.AddSingleton<VP_ViewModel_Experimental_R>();
    }
    #endregion
    #endregion
    #endregion
}