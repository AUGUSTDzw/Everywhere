namespace Everywhere.I18N;

/// <summary>
/// Resolves localization keys to intentional language snapshots.
/// </summary>
public static class LocaleKeyExtensions
{
    /// <summary>
    /// Resolves a key in the current language, returning the key itself when missing.
    /// Use IDynamicLocaleKey for persistent UI text that must follow language changes.
    /// </summary>
    public static string I18N(this string key) => LocaleManager.TryGetString(key, out var value) ? value : key;
}