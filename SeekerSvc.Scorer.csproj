namespace SeekerSvc.Tailor;

/// <summary>
/// Decides whether a researched company hook is safe to put in front of the generation model as
/// employer context. A hook is an assertion about the *company*, not the candidate — but the Decomposer
/// scans the whole cover letter for numbers, credentials, and amplifiers and turns them into *candidate*
/// claim atoms the Fabrication Gate then checks against the profile. A grounded hook like "raised $40M"
/// or "is AWS-certified" would therefore read as an unverifiable candidate claim and force a false block.
///
/// So a hook is admitted only if it carries none of those patterns (<see cref="Decomposer.LooksLikeCandidateClaim"/>).
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
