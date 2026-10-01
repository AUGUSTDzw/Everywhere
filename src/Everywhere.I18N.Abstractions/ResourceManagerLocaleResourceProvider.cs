using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Resources;

namespace Everywhere.I18N;

/// <summary>
/// Loads RESX resources lazily through .NET's ResourceManager, including satellite assemblies and per-key culture fallback.
/// </summary>
public sealed class ResourceManagerLocaleResourceProvider(string baseName, Assembly assembly) : ILocaleResourceProvider, IDisposable
{
    private readonly ResourceManager _resourceManager = new(baseName, assembly);

    /// <inheritdoc />
    public bool TryGetString(string key, LocaleName locale, [NotNullWhen(true)] out string? value)
    {
        value = _resourceManager.GetString(key, locale.ToCultureInfo());
        return value is not null;
    }

    /// <inheritdoc />
    public void Dispose() => _resourceManager.ReleaseAllResources();
}