using System.Globalization;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

/// <summary>
/// One time window of the Pro outcome funnel. <see cref="Sent"/> is every application with a tracked
/// outcome whose timestamp falls in the window; <see cref="Replied"/> is those that drew any response
/// (replied/interview/offer/rejected — a rejection is still a reply), so <see cref="ReplyRatePct"/> is
/// the honest reply rate. <see cref="Interviews"/> counts interview-or-further; <see cref="Offers"/> and
/// <see cref="Rejected"/> are the terminal results; <see cref="NoReply"/> is the explicit dead-air state.
/// </summary>
public sealed record FunnelWindow(
    int Days, int Sent, int Replied, int Interviews, int Offers, int Rejected, int NoReply)
{
    /// <summary>Reply rate as a 0–100 int (same scale the rest of the UI speaks). 0 when nothing was sent.</summary>
    public int ReplyRatePct => Sent == 0 ? 0 : (int)Math.Round(100.0 * Replied / Sent, MidpointRounding.AwayFromZero);

    /// <summary>Terminal results: offers plus rejections.</summary>
    public int Results => Offers + Rejected;
}

/// <summary>
/// The Pro funnel board (P4 §2.5): sent → reply rate → interviews → results over 7/30/90 days, computed
/// purely from the outcome state and timestamp already on each application row. No phone, no account, no
/// pairing — the honest core of Pro, computable entirely on the desktop.
///
/// Pure and deterministic given an injected <c>now</c>, so the windowing is fully unit-testable. The
/// input is whatever recent application slice the caller supplies; over-90-day or outcome-less rows are
/// simply not counted.
/// </summary>
public sealed record FunnelBoard(FunnelWindow Last7, FunnelWindow Last30, FunnelWindow Last90)
{
    public static FunnelBoard Compute(IReadOnlyList<ApplicationSummaryRow> apps, DateTimeOffset now) => new(
        Window(apps, now, 7), Window(apps, now, 30), Window(apps, now, 90));

    private static FunnelWindow Window(IReadOnlyList<ApplicationSummaryRow> apps, DateTimeOffset now, int days)
    {
        var cutoff = now.AddDays(-days);
        int sent = 0, replied = 0, interviews = 0, offers = 0, rejected = 0, noReply = 0;
        foreach (var a in apps)
        {
            if (a.Outcome is null || a.OutcomeAt is null) continue;
            if (!DateTimeOffset.TryParse(a.OutcomeAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) continue;
            if (at < cutoff || at > now) continue;

            sent++; // any tracked outcome means the application was sent
            switch (a.Outcome)
            {
                case "no_reply": noReply++; break;
                case "replied": replied++; break;
                case "interview": replied++; interviews++; break;
                case "offer": replied++; interviews++; offers++; break;
                case "rejected": replied++; rejected++; break; // a rejection is a reply that came back
                // "sent": counted in `sent` only — acted on, no further signal yet
            }
        }
        return new FunnelWindow(days, sent, replied, interviews, offers, rejected, noReply);
    }
}
