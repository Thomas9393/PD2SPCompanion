namespace PD2SPCompanion.Shell.Services.Generic;

public interface ISFXManager
{
    #region Sound Playing
    #region Button Group
    void Method_ButtonSFX_Press();

    /// <summary>The sounds read from the game ahead of the first click; nothing where no folder is set.</summary>
    void Prepare();
    #endregion
    #endregion
}
