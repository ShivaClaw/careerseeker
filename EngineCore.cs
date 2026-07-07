using System.Globalization;
using System.Text.Json;

namespace SeekerSvc.Scout;

/// <summary>
/// An ATS adapter: knows how to build the public job-feed URL for a board and how to parse
/// that feed's JSON into normalized <see cref="DiscoveredJob"/>s. Implementations are PURE —
/// no I/O, no shared state — so they can be unit-tested against captured fixture JSON. All
/// fetching, retrying, and throttling lives behind <see cref="IBoardFetcher"/> instead.
/// </summary>
public interface IAtsProvider
{
    AtsKind Kind { get; }

    /// <summary>The absolute URL of the board's public JSON job feed.</summary>
    string BuildListUrl(CompanyBoard board);

    /// <summary>
    /// Parse a feed body into postings. A single malformed posting is skipped, never fatal;
    /// only a body that is not valid JSON at all throws (the orchestrator marks that board
    /// failed and continues with the others).
    /// </summary>
    IReadOnlyList<DiscoveredJob> Parse(CompanyBoard board, string json);
}

/// <summary>
/// Greenhouse job board API. <c>boards-api.greenhouse.io/v1/boards/{token}/jobs?content=true</c>
/// returns <c>{ "jobs": [...], "meta": {...} }</c>. There is no structured pay field — pay, if
/// disclosed, lives in the entity-escaped HTML <c>content</c>, so compensation is text-parsed
/// (and is null when absent).
/// </summary>
public sealed class GreenhouseProvider : IAtsProvider
{
    public AtsKind Kind => AtsKind.Greenhouse;

    public string BuildListUrl(CompanyBoard board)
        => $"https://boards-api.greenhouse.io/v1/boards/{board.Handle}/jobs?content=true";

    public IReadOnlyList<DiscoveredJob> Parse(CompanyBoard board, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var jobs = new List<DiscoveredJob>();

        foreach (var job in doc.RootElement.Arr("jobs"))
        {
            try
            {
                var id = job.Long("id")?.ToString(CultureInfo.InvariantCulture) ?? job.Str("id");
                var title = job.Str("title");
                var url = job.Str("absolute_url");
                if (id is null || title is null || url is null) continue;

                var description = Html.ToPlainText(job.Str("content"));
                var location = job.Prop("location").Str("name");

                var raw = new RawPosting(
                    JobId: id,
                    Title: title,
                    Location: location,
                    Remote: RemoteModes.Parse(workplaceType: null, isRemote: null, location: location),
                    Comp: CompParse.FromText(description), // pay only ever appears in the text
                    DescriptionText: description,
                    Url: url,
                    ApplyUrl: url, // Greenhouse absolute_url is the application page
                    FirstPublished: Dates.Parse(job.Str("first_published") ?? job.Str("updated_at")));

                jobs.Add(JobBuilder.Build(Kind, board, raw));
            }
            catch
            {
                // skip this posting; one malformed record never sinks the board
            }
        }

        return jobs;
    }
}

/// <summary>
/// Lever postings API. <c>api.lever.co/v0/postings/{site}?mode=json</c> returns a top-level JSON
/// array (an empty board is <c>200 []</c>). Title is <c>text</c>; the job body is
/// <c>descriptionPlain</c>. <c>additional/additionalPlain</c> is company boilerplate, NOT the job
/// description, so it is deliberately excluded. Compensation prefers the structured
/// <c>salaryRange</c>, then <c>salaryDescriptionPlain</c>, then the body text.
/// </summary>
public sealed class LeverProvider : IAtsProvider
{
    public AtsKind Kind => AtsKind.Lever;

    public string BuildListUrl(CompanyBoard board)
        => $"https://api.lever.co/v0/postings/{board.Handle}?mode=json";

    public IReadOnlyList<DiscoveredJob> Parse(CompanyBoard board, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var jobs = new List<DiscoveredJob>();

        foreach (var posting in doc.RootElement.Items()) // top-level array
        {
            try
            {
                var id = posting.Str("id");
                var title = posting.Str("text");
                var url = posting.Str("hostedUrl") ?? posting.Str("applyUrl");
                if (id is null || title is null || url is null) continue;

                var cats = posting.Prop("categories");
                var location = cats.Str("location");
                var workplaceType = posting.Str("workplaceType");

                var description = !string.IsNullOrEmpty(posting.Str("descriptionPlain"))
                    ? Html.CollapsePlain(posting.Str("descriptionPlain"))
                    : Html.ToPlainText(posting.Str("description"));

                var comp =
                    CompParse.FromLeverSalaryRange(posting.Prop("salaryRange"))
                    ?? CompParse.FromText(posting.Str("salaryDescriptionPlain"))
                    ?? CompParse.FromText(description);

                var raw = new RawPosting(
                    JobId: id,
                    Title: title,
                    Location: location,
                    Remote: RemoteModes.Parse(workplaceType, isRemote: null, location: location),
                    Comp: comp,
                    DescriptionText: description,
                    Url: url,
                    ApplyUrl: posting.Str("applyUrl"),
                    FirstPublished: Dates.FromUnixMs(posting.Long("createdAt")));

                jobs.Add(JobBuilder.Build(Kind, board, raw));
            }
            catch
            {
                // skip this posting
            }
        }

        return jobs;
    }
}

/// <summary>
/// Ashby posting API. <c>api.ashbyhq.com/posting-api/job-board/{board}?includeCompensation=true</c>
/// returns <c>{ "apiVersion": "...", "jobs": [...] }</c>. Postings with <c>isListed == false</c> are
/// excluded — they are deliberately unpublished, and surfacing them would misrepresent what the
/// employer is actually advertising. Compensation comes from the structured Salary component when
/// present, else the human-readable summary string.
/// </summary>
public sealed class AshbyProvider : IAtsProvider
{
    public AtsKind Kind => AtsKind.Ashby;

    public string BuildListUrl(CompanyBoard board)
        => $"https://api.ashbyhq.com/posting-api/job-board/{board.Handle}?includeCompensation=true";

    public IReadOnlyList<DiscoveredJob> Parse(CompanyBoard board, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var jobs = new List<DiscoveredJob>();

        foreach (var job in doc.RootElement.Arr("jobs"))
        {
            try
            {
                if (job.Bool("isListed") == false) continue; // honor unlisted postings

                var id = job.Str("id");
                var title = job.Str("title");
                var url = job.Str("jobUrl") ?? job.Str("applyUrl");
                if (id is null || title is null || url is null) continue;

                var location = job.Str("location");
                var description = !string.IsNullOrEmpty(job.Str("descriptionPlain"))
                    ? Html.CollapsePlain(job.Str("descriptionPlain"))
                    : Html.ToPlainText(job.Str("descriptionHtml"));

                var raw = new RawPosting(
                    JobId: id,
                    Title: title,
                    Location: location,
                    Remote: RemoteModes.Parse(job.Str("workplaceType"), job.Bool("isRemote"), location),
                    Comp: CompParse.FromAshbyCompensation(job.Prop("compensation")),
                    DescriptionText: description,
                    Url: url,
                    ApplyUrl: job.Str("applyUrl"),
                    FirstPublished: Dates.Parse(job.Str("publishedAt")));

                jobs.Add(JobBuilder.Build(Kind, board, raw));
            }
            catch
            {
                // skip this posting
            }
        }

        return jobs;
    }
}
