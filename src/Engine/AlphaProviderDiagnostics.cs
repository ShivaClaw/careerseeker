using System.Net;
using System.Text;
using SeekerSvc.Gateway;

namespace SeekerSvc.Engine;

public enum ProviderTestOutcome
{
    InvalidCredentials,
    PermissionDenied,
    QuotaExceeded,
    ModelUnavailable,
    TransientFailure,
    OtherFailure,
}

public sealed record ProviderTestDiagnostic(
    ProviderTestOutcome Outcome,
    string FriendlyMessage,
    string AdvancedDetails,
    bool CredentialAuthenticated,
    bool CanSaveWithoutSuccessfulTest);

public static class AlphaProviderDiagnostics
{
    public static ProviderTestDiagnostic Classify(string providerDisplayName, Exception exception, string key)
    {
        if (exception is ProviderHttpException providerError)
            return ClassifyProviderError(providerDisplayName, providerError, key);

        if (exception is TaskCanceledException)
        {
            return new ProviderTestDiagnostic(
                ProviderTestOutcome.TransientFailure,
                $"{providerDisplayName} did not respond before the setup test timed out.",
                "The credential test timed out before the provider returned a result.",
                CredentialAuthenticated: false,
                CanSaveWithoutSuccessfulTest: true);
        }

        return new ProviderTestDiagnostic(
            ProviderTestOutcome.OtherFailure,
            $"{providerDisplayName} could not be reached for a successful credential test.",
            RedactedDetail(exception.Message, key),
            CredentialAuthenticated: false,
            CanSaveWithoutSuccessfulTest: false);
    }

    public static string SanitizePastedKey(string value)
    {
        var clean = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\u200b' or '\u200c' or '\u200d' or '\ufeff') continue;
            if (!char.IsWhiteSpace(ch) && !char.IsControl(ch))
                clean.Append(ch);
        }
        return clean.ToString().Trim('"', '\'');
    }

    public static string DescribeKey(string providerId, string key)
    {
        var type = providerId.Equals("google", StringComparison.OrdinalIgnoreCase)
            ? key.StartsWith("AQ.", StringComparison.Ordinal) ? "AQ. auth key"
            : key.StartsWith("AIza", StringComparison.Ordinal) ? "AIza standard key"
            : "unrecognized Gemini key format"
            : key.StartsWith("sk-ant-", StringComparison.Ordinal) ? "Anthropic key"
            : "unrecognized Anthropic key format";
        var ending = key.Length >= 4 ? key[^4..] : "short";
        return $"{type}, ending {ending}";
    }

    private static ProviderTestDiagnostic ClassifyProviderError(
        string providerDisplayName,
        ProviderHttpException error,
        string key)
    {
        var advanced = RedactedDetail(error.Message, key);
        if (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            var rolloutHint =
                error.Provider.Equals("google", StringComparison.OrdinalIgnoreCase) &&
                key.StartsWith("AQ.", StringComparison.Ordinal) &&
                string.Equals(error.ProviderReason, "ACCESS_TOKEN_TYPE_UNSUPPORTED", StringComparison.OrdinalIgnoreCase)
                    ? " This matches a reported Google authorization-key rollout failure; the request format is correct."
                    : "";
            return new ProviderTestDiagnostic(
                ProviderTestOutcome.InvalidCredentials,
                $"{providerDisplayName} rejected this credential (401 UNAUTHENTICATED).{rolloutHint}",
                advanced,
                CredentialAuthenticated: false,
                CanSaveWithoutSuccessfulTest: false);
        }

        if (error.StatusCode == HttpStatusCode.Forbidden)
        {
            return new ProviderTestDiagnostic(
                ProviderTestOutcome.PermissionDenied,
                $"{providerDisplayName} recognized the request but denied access. Check the project, API restrictions, billing, and account permissions.",
                advanced,
                CredentialAuthenticated: false,
                CanSaveWithoutSuccessfulTest: false);
        }

        if (error.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new ProviderTestDiagnostic(
                ProviderTestOutcome.QuotaExceeded,
                $"{providerDisplayName} authenticated the credential, but its current quota or rate limit is exhausted.",
                advanced,
                CredentialAuthenticated: true,
                CanSaveWithoutSuccessfulTest: false);
        }

        if (error.StatusCode == HttpStatusCode.NotFound)
        {
            return new ProviderTestDiagnostic(
                ProviderTestOutcome.ModelUnavailable,
                $"{providerDisplayName} authenticated the credential, but the configured model is unavailable.",
                advanced,
                CredentialAuthenticated: true,
                CanSaveWithoutSuccessfulTest: false);
        }

        if (error.StatusCode == HttpStatusCode.RequestTimeout ||
            error.StatusCode is { } statusCode && (int)statusCode >= 500)
        {
            return new ProviderTestDiagnostic(
                ProviderTestOutcome.TransientFailure,
                $"{providerDisplayName} returned a temporary server error. Retry before changing credentials.",
                advanced,
                CredentialAuthenticated: false,
                CanSaveWithoutSuccessfulTest: true);
        }

        return new ProviderTestDiagnostic(
            ProviderTestOutcome.OtherFailure,
            $"{providerDisplayName} rejected the setup test.",
            advanced,
            CredentialAuthenticated: false,
            CanSaveWithoutSuccessfulTest: false);
    }

    private static string RedactedDetail(string value, string key)
    {
        var redacted = string.IsNullOrEmpty(key)
            ? value
            : value.Replace(key, "[redacted-api-key]", StringComparison.Ordinal);
        return redacted.Length <= 800 ? redacted : redacted[..800] + "...";
    }
}
