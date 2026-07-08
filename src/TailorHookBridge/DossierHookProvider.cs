using SeekerSvc.Pipeline;
using SeekerSvc.Researcher;

namespace SeekerSvc.Tailor;

/// <summary>
/// Implements the Tailor's <see cref="IHookProvider"/> on the Researcher's dossier: for a job's company it
/// returns <see cref="Dossier.BestHook"/> — the single grounded, source-backed company line (spec §5.5).
/// Lives apart from both modules so the Tailor never references the Researcher; the composition root wires
/// it. Returns null when research grounded no hook, and the result still passes the Tailor's
/// <see cref="HookGuard"/> before reaching the model, so a quantified hook is dropped rather than risked.
/// </summary>
public sealed class DossierHookProvider : IHookProvider
{
    private readonly SeekerSvc.Researcher.Researcher _researcher;

    public DossierHookProvider(SeekerSvc.Researcher.Researcher researcher) => _researcher = researcher;

    public async Task<string?> GetHookAsync(PipelineJob job, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(job.Company)) return null;
        var dossier = await _researcher.BuildAsync(new CompanyRef(job.Company), ct: ct).ConfigureAwait(false);
        return dossier.BestHook?.Text;
    }
}
