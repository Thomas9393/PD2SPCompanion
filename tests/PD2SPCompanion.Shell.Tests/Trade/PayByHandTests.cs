using System.Globalization;

using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Trade;

/// <summary>
/// The payment's two ways: an offer typed and counted out, as it was,
/// or each currency picked by hand and the offer what they come to - a button on the panel switches, saying
/// "Pick by hand" or "Count for me". A listing of the corpus's is opened by its link from the trade cache (no
/// network), the payment looked at and closed: nothing is paid.
/// <para>
/// What the payment decides, asked as the panel's boxes and buttons ask. That the button and the currencies'
/// boxes are there to click is the UI case's to show (units/pay-by-hand.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PayByHandTests(Program program)
{
    private VP_ViewModel_Trade Trade => TradeHands.Page(program);
    private PaymentViewModel Payment => Trade.Payment;

    private static decimal Offer(PaymentViewModel payment) => decimal.Parse(payment.Price, CultureInfo.InvariantCulture);

    [CorpusFact]
    public void The_payment_opens_counting_an_offer_out_and_its_button_switches_to_picking_by_hand_and_back()
    {
        if (!program.Ready() || !TradeHands.Cache())
            return;

        string mode = program.On(() => program.Get<EditorSettings>().PlayMode);
        try
        {
            program.On(() => TradeHands.OnRight(program));
            TradeHands.Paste(program);

            program.On(() =>
            {
                Trade.ConfirmCommand.Execute(null);

                Assert.True(Trade.IsPaymentOpen);
                Assert.False(Payment.PicksByHand);
                Assert.Equal("Pick by hand", Payment.PickTip);
                Assert.Equal("Pay", Payment.ActionTip);
                Assert.Equal("PAY", Payment.ActionWord);
                Assert.True(Payment.IsOfferTyped);

                Payment.TogglePickingCommand.Execute(null);
                Assert.Equal("Count for me", Payment.PickTip);
                Assert.False(Payment.IsOfferTyped, "picking: the offer is what was picked, not typed");
                Assert.NotEmpty(Payment.Rows);

                Payment.TogglePickingCommand.Execute(null);
                Assert.Equal("Pick by hand", Payment.PickTip);
                Assert.True(Payment.IsOfferTyped, "the offer is counted out again");
            });
        }
        finally
        {
            program.On(() => TradeHands.Rest(program, mode));
            TradeHands.Uncache();
        }
    }

    [CorpusFact]
    public void Picked_by_hand_the_offer_is_what_the_currencies_come_to_held_at_what_the_tab_holds()
    {
        if (!program.Ready() || !TradeHands.Cache())
            return;

        string mode = program.On(() => program.Get<EditorSettings>().PlayMode);
        try
        {
            program.On(() => TradeHands.OnRight(program));
            TradeHands.Paste(program);

            program.On(() =>
            {
                Trade.ConfirmCommand.Execute(null);
                Payment.TogglePickingCommand.Execute(null);

                // Everything put back to nought: the offer is nothing.
                foreach (var row in Payment.Rows)
                    row.Count = "0";
                Assert.Equal(0m, Offer(Payment));
                Assert.Equal("Paying nothing.", Payment.Summary);

                // One of a currency the tab holds: the offer is its worth, whatever the listing asked.
                var one = Payment.Rows.First(row => row.Stock > 0);
                one.Count = "1";
                Assert.Equal(one.Worth, Offer(Payment));
                Assert.True(Payment.PayCommand.CanExecute(null), "and it can be paid");

                // More than the tab holds is held at what it holds.
                one.Count = (one.Stock + 5).ToString(CultureInfo.InvariantCulture);
                Assert.Equal(one.Worth * one.Stock, Offer(Payment));
                Assert.Equal(one.Stock, one.Paid);
            });
        }
        finally
        {
            program.On(() => TradeHands.Rest(program, mode));
            TradeHands.Uncache();
        }
    }
}
