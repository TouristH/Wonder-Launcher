// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

namespace Wonder.Core;

/// <summary>Filters known credentials before messages enter UI/log buffers.</summary>
public sealed class CredentialRedactor(IEnumerable<string> sensitiveValues)
{
    private readonly string[] _values = sensitiveValues.Where(value => !string.IsNullOrEmpty(value) && value != "0")
        .Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length).ToArray();

    public string Redact(string value)
    {
        foreach (var secret in _values)
        {
            value = value.Replace(secret, "[已隐藏凭据]", StringComparison.Ordinal);
            var escaped = Uri.EscapeDataString(secret);
            if (!string.Equals(secret, escaped, StringComparison.Ordinal))
                value = value.Replace(escaped, "[已隐藏凭据]", StringComparison.Ordinal);
        }
        return value;
    }
}
