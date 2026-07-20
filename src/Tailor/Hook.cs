namespace SeekerSvc.Tailor;

/// <summary>
/// Decides whether a researched company hook is safe to put in front of the generation model as
/// employer context. A hook is an assertion about the *company*, not the candidate. The current L1 Gate
/// verifies generated prose against candidate profile facts only, so the Tailor prompt treats admitted hooks
/// as emphasis context rather than applicant-facing facts to quote.
///
/// A hook is still admitted only if it carries none of the highest-risk candidate-claim patterns
/// (<see cref="Decomposer.LooksLikeCandidateClaim"/>).
/// This can only ever cause the Tailor to omit a hook, never to ship an unverified one — the failure
/// direction is safe. Quantified company facts still live in the dossier for the user to read; they just
/// don't go into an auto-generated letter.
/// </summary>
public static class HookGuard
{
    /// <summary>True if the hook is present and free of candidate-claim patterns (so it cannot become a Gate atom).</summary>
    public static bool IsSafe(string? hook) =>
        !string.IsNullOrWhiteSpace(hook) && !Decomposer.LooksLikeCandidateClaim(hook);
}
