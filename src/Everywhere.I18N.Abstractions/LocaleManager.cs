using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using CommunityToolkit.Mvvm.Messaging;

namespace Everywhere.I18N;

/// <summary>
/// Resolves localized strings without depending on application or UI initialization.
/// </summary>
public static class LocaleManager
{
    /// <summary>
    /// Gets or changes the language used for subsequent lookups. Only language changes send notifications.
    /// Recipients that update UI objects must marshal notifications to their UI thread.
    /// </summary>
    public static LocaleName CurrentLocale
    {
        get => (LocaleName)Volatile.Read(ref _currentLocale);
        set
        {
            var oldLocale = (LocaleName)Interlocked.Exchange(ref _currentLocale, (int)value);
            if (oldLocale != value) WeakReferenceMessenger.Default.Send(new LocaleChangedMessage(oldLocale, value));
        }
    }

    private static readonly Lock SyncRoot = new();
    private static ProviderRegistration[] _registrations = [];
    private static int _currentLocale = (int)GetCurrentCultureLocale();

    /// <summary>
    /// Registers a lazily constructed provider. Later registrations take precedence for matching keys.
    /// Dispose the returned token to unregister and dispose the provider. Collectible assembly contexts
    /// also unregister their providers when unloading.
    /// </summary>
    /// <remarks>
    /// Safe to call from a module initializer: registration never invokes the factory, reads resources,
    /// sends notifications, or waits for the UI thread. Factories must not resolve keys through themselves.
    /// </remarks>
    public static IDisposable RegisterProvider(Assembly owner, Func<ILocaleResourceProvider> providerFactory)
    {
        var registration = new ProviderRegistration(owner, providerFactory);
        lock (SyncRoot)
        {
            if (!registration.IsDisposed) Volatile.Write(ref _registrations, [.. _registrations, registration]);
        }
        return registration;
    }

    /// <summary>
    /// Looks up a key in the current language. A lookup captures the language and registration snapshot once.
    /// </summary>
    public static bool TryGetString(string key, [NotNullWhen(true)] out string? value) => TryGetString(key, CurrentLocale, out value);

    /// <summary>
    /// Looks up a key in a specified language, without changing the current language.
    /// </summary>
    public static bool TryGetString(string key, LocaleName locale, [NotNullWhen(true)] out string? value)
    {
        if (key.Length != 0)
        {
            var registrations = Volatile.Read(ref _registrations);
            for (var i = registrations.Length - 1; i >= 0; i--)
            {
                if (registrations[i].TryGetString(key, locale, out value)) return true;
            }
        }
        value = null;
        return false;
    }

    private static LocaleName GetCurrentCultureLocale()
    {
        var culture = CultureInfo.CurrentUICulture;
        while (!string.IsNullOrEmpty(culture.Name))
        {
            if (Enum.TryParse<LocaleName>(culture.Name.Replace("-", ""), true, out var locale)) return locale;
            culture = culture.Parent;
        }
        return LocaleName.En;
    }

    private sealed class ProviderRegistration : IDisposable
    {
        public bool IsDisposed => Volatile.Read(ref _isDisposed);

        private readonly Lock _syncRoot = new();
        private Func<ILocaleResourceProvider>? _providerFactory;
        private ILocaleResourceProvider? _provider;
        private AssemblyLoadContext? _loadContext;
        private bool _isDisposed;

        public ProviderRegistration(Assembly owner, Func<ILocaleResourceProvider> providerFactory)
        {
            _providerFactory = providerFactory;
            var loadContext = AssemblyLoadContext.GetLoadContext(owner);
            if (loadContext is { IsCollectible: true })
            {
                _loadContext = loadContext;
                loadContext.Unloading += HandleUnloading;
            }
        }

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public bool TryGetString(string key, LocaleName locale, [NotNullWhen(true)] out string? value)
        {
            // This lock coordinates construction, reads and disposal for this provider only.
            // Plugin code is never called while holding the registry lock.
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    value = null;
                    return false;
                }
                if (_provider is null && _providerFactory is { } factory)
                {
                    _provider = factory();
                    _providerFactory = null;
                }
                if (_provider is { } provider) return provider.TryGetString(key, locale, out value);
                value = null;
                return false;
            }
        }

        public void Dispose()
        {
            lock (SyncRoot)
            {
                Volatile.Write(ref _registrations, _registrations.Where(registration => registration != this).ToArray());
            }

            ILocaleResourceProvider? provider;
            lock (_syncRoot)
            {
                if (_isDisposed) return;

                _isDisposed = true;
                provider = _provider;
                _provider = null;
                _providerFactory = null;
                _loadContext?.Unloading -= HandleUnloading;
                _loadContext = null;
            }

            if (provider is IDisposable disposable) disposable.Dispose();
        }

        private void HandleUnloading(AssemblyLoadContext context) => Dispose();
    }
}