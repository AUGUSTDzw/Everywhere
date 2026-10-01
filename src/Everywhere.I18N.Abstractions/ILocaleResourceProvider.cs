using System.Diagnostics.CodeAnalysis;

namespace Everywhere.I18N;

/// <summary>
/// Supplies localized strings independently of their storage format. Implementations own language loading,
/// fallback and resource updates; updates do not send language-change notifications.
/// </summary>
public interface ILocaleResourceProvider
{
    /// <summary>
    /// Looks up a key, lazily loading the requested language as necessary. Returns false for a missing key.
    /// </summary>
    bool TryGetString(string key, LocaleName locale, [NotNullWhen(true)] out string? value);
}