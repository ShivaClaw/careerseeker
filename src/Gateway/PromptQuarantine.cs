using System.Security;

namespace SeekerSvc.Gateway;

/// <summary>
/// Encodes untrusted text before it is placed inside a structured prompt boundary.
/// Encoding is only one layer: callers must label the boundary as untrusted, give
/// explicit data-only instructions, and validate output before using it.
/// </summary>
public static class PromptQuarantine
{
    public static string Encode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var safe = string.Concat(value.Where(c =>
            !char.IsControl(c) || c is '\t' or '\n' or '\r'));
        return SecurityElement.Escape(safe) ?? string.Empty;
    }
}
