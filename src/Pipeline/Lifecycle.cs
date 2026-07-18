namespace SeekerSvc.Pipeline;

/// <summary>
/// The application lifecycle as a pure, deterministic state machine (spec section 3.3). This is the
/// module's safety anchor: the graph is the law. READY is reachable only from VERIFIED, and every
/// action state (where tailored content is exposed or sent in the user's name) is reachable only
/// through READY — so there is no edge that routes around the Fabrication Gate. The orchestrator asks
/// this type before every transition and refuses any edge it does not allow.
///
/// Control transitions are universal: USER_KILLED from any non-killed state, PAUSED from any active
/// state. Resume restores a remembered prior state and is a trusted control op (not a graph edge).
/// </summary>
public static class Lifecycle
{
    /// <summary>Forward lifecycle edges. Kill/pause are layered on in <see cref="CanTransition"/>.</summary>
    private static readonly IReadOnlyDictionary<AppState, AppState[]> Forward = new Dictionary<AppState, AppState[]>
    {
        [AppState.DISCOVERED] = new[] { AppState.SCREENED },
        [AppState.SCREENED] = new[] { AppState.EVALUATED },
        [AppState.EVALUATED] = new[] { AppState.TAILORED, AppState.REJECTED_BY_ENGINE },

        // The Gate. A tailored application either passes (VERIFIED), is blocked for unsupported claims,
        // or is deferred because verifier infrastructure is unavailable. READY has exactly one
        // predecessor — VERIFIED — so nothing reaches it without passing.
        [AppState.TAILORED] = new[] { AppState.VERIFIED, AppState.BLOCKED_FABRICATION, AppState.GATE_UNAVAILABLE },
        [AppState.VERIFIED] = new[] { AppState.READY },
        [AppState.BLOCKED_FABRICATION] = new[] { AppState.TAILORED }, // one rework loop; escalation is a human gate
        [AppState.GATE_UNAVAILABLE] = new[] { AppState.TAILORED },    // retry later when the verifier recovers

        // Autonomy routing happens here (see RouteFromReady).
        [AppState.READY] = new[] { AppState.DRAFTED, AppState.GATE_PENDING, AppState.SUBMITTING },

        [AppState.DRAFTED] = new[] { AppState.APPLIED },            // L1: the user sends the draft
        [AppState.GATE_PENDING] = new[] { AppState.APPROVED, AppState.SKIPPED, AppState.GATE_EXPIRED },
        [AppState.GATE_EXPIRED] = new[] { AppState.GATE_PENDING }, // re-prompt via digest
        [AppState.APPROVED] = new[] { AppState.SUBMITTING },
        [AppState.SUBMITTING] = new[] { AppState.APPLIED },

        [AppState.APPLIED] = new[] { AppState.AWAITING_RESPONSE },
        [AppState.AWAITING_RESPONSE] = new[]
        {
            AppState.RECRUITER_REPLY, AppState.FOLLOWUP_DUE, AppState.INTERVIEW_PROPOSED,
            AppState.REJECTED, AppState.OFFER, AppState.GHOSTED,
        },
        [AppState.RECRUITER_REPLY] = new[] { AppState.CORRESPONDENCE },
        [AppState.CORRESPONDENCE] = new[] { AppState.AWAITING_RESPONSE, AppState.INTERVIEW_PROPOSED, AppState.REJECTED, AppState.OFFER },
        [AppState.FOLLOWUP_DUE] = new[] { AppState.FOLLOWUP_SENT },
        [AppState.FOLLOWUP_SENT] = new[] { AppState.AWAITING_RESPONSE },
        [AppState.INTERVIEW_PROPOSED] = new[] { AppState.SLOTS_OFFERED },
        [AppState.SLOTS_OFFERED] = new[] { AppState.SCHEDULED },
        [AppState.SCHEDULED] = new[] { AppState.AWAITING_RESPONSE, AppState.OFFER, AppState.REJECTED },

        // terminals
        [AppState.REJECTED_BY_ENGINE] = Array.Empty<AppState>(),
        [AppState.SKIPPED] = Array.Empty<AppState>(),
        [AppState.REJECTED] = Array.Empty<AppState>(),
        [AppState.OFFER] = Array.Empty<AppState>(),
        [AppState.GHOSTED] = Array.Empty<AppState>(),
        [AppState.USER_KILLED] = Array.Empty<AppState>(),
        [AppState.PAUSED] = Array.Empty<AppState>(), // resume is a control op, not an edge
    };

    private static readonly HashSet<AppState> Terminals = new()
    {
        AppState.REJECTED_BY_ENGINE, AppState.SKIPPED, AppState.REJECTED,
        AppState.OFFER, AppState.GHOSTED, AppState.USER_KILLED,
    };

    /// <summary>States in which tailored content is created, exposed, or sent. All sit after READY.</summary>
    public static readonly IReadOnlySet<AppState> ActionStates = new HashSet<AppState>
    {
        AppState.DRAFTED, AppState.GATE_PENDING, AppState.APPROVED, AppState.SUBMITTING, AppState.APPLIED,
    };

    public static IReadOnlyList<AppState> AllStates { get; } = Enum.GetValues<AppState>();

    public static bool IsTerminal(AppState s) => Terminals.Contains(s);

    /// <summary>Active = can still progress and can be paused (not terminal, not already paused).</summary>
    public static bool IsActive(AppState s) => !IsTerminal(s) && s != AppState.PAUSED;

    /// <summary>The forward successors of a state (excludes the universal kill/pause edges).</summary>
    public static IReadOnlyList<AppState> ForwardSuccessors(AppState s) => Forward[s];

    /// <summary>Whether <paramref name="to"/> is a legal transition from <paramref name="from"/>.</summary>
    public static bool CanTransition(AppState from, AppState to)
    {
        if (to == from) return false;
        if (to == AppState.USER_KILLED) return from != AppState.USER_KILLED; // kill from anywhere
        if (to == AppState.PAUSED) return IsActive(from);                    // pause any active state
        return Forward[from].Contains(to);
    }

    /// <summary>The state READY routes to for a given autonomy level (spec section 2.1).</summary>
    public static AppState RouteFromReady(AutonomyLevel level) => level switch
    {
        AutonomyLevel.L1 => AppState.DRAFTED,      // Gmail draft; the human sends
        AutonomyLevel.L2 => AppState.GATE_PENDING, // push a one-tap approval
        AutonomyLevel.L3 => AppState.SUBMITTING,   // act within rails
        _ => AppState.DRAFTED,
    };

    /// <summary>Forward-reachable states from <paramref name="from"/>, treating <paramref name="blocked"/> as removed.</summary>
    public static IReadOnlySet<AppState> Reachable(AppState from, IReadOnlySet<AppState> blocked)
    {
        var seen = new HashSet<AppState>();
        var queue = new Queue<AppState>();
        if (!blocked.Contains(from)) { seen.Add(from); queue.Enqueue(from); }
        while (queue.Count > 0)
        {
            foreach (var next in Forward[queue.Dequeue()])
            {
                if (blocked.Contains(next) || !seen.Add(next)) continue;
                queue.Enqueue(next);
            }
        }
        return seen;
    }
}
