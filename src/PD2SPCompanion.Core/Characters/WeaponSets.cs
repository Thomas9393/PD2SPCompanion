using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// The two weapon sets, as the game keeps them.
/// <para>
/// <b>The weapons in hand are always in the main hand slots</b> (<see cref="ItemLocation.RightHand"/>,
/// <see cref="ItemLocation.LeftHand"/>). A swap in game moves the items: the pair in hand goes to the swap
/// slots and the other pair comes to the main ones (D2Game's weapon switch takes all four out and puts each
/// in its opposite body location), and what sits in the swap slots gives the character nothing. The save's
/// weapon-switch field (<see cref="D2S.ActiveWeapon"/>) is only which tab the game shows the pair in hand
/// under - 0 the I tab, 1 the II tab.
/// </para>
/// <para>
/// Read as "which slots are in hand", a character saved on its II tab would have its swap slots
/// counted and drawn - GoldFind, a Barbarian with a weapon in each hand, stood bare-handed on its card, and
/// its weapons gave nothing toward requirements. Seen in the save itself: switch 1, weapons only in the main
/// slots, and the game's own appearance bytes with a weapon drawn.
/// </para>
/// </summary>
public static class WeaponSets
{
    /// <summary>The set <see cref="Requirements.Counted"/> counts for the weapons in hand: the main slots', whatever tab shows them.</summary>
    public const int InHand = 0;

    /// <summary>The other pair, in the swap slots: judged as it would be in hand, giving nothing meanwhile.</summary>
    public const int Swapped = 1;

    /// <summary>The tab, 1 or 2, the game shows the weapons in hand under.</summary>
    public static int TabInHand(D2S save) => save.ActiveWeapon == 0 ? 1 : 2;
}
