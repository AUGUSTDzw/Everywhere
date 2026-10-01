using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using CommunityToolkit.Mvvm.Messaging;
using Everywhere.I18N;

namespace Everywhere.Abstractions.Tests.I18N;

[TestFixture]
[NonParallelizable]
public sealed class LocaleManagerTests
{
    [Test]
    public async Task RegisterProvider_AfterLookup_DoesNotInvokeFactoryOrNotify()
    {
        LocaleManager.TryGetString("Common_Copied", out _);
        var hasConstructed = false;
        var recipient = new LanguageRecipient();
        WeakReferenceMessenger.Default.Register(recipient);
        try
        {
            using var registration = await Task.Run(() => LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () =>
            {
                hasConstructed = true;
                return new TestProvider("RegistrationProbe", "value");
            })).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(hasConstructed, Is.False);
                Assert.That(recipient.Count, Is.Zero);
            });
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    [Test]
    public async Task ConcurrentLookups_ConstructProviderOnce()
    {
        var count = 0;
        using var registration = LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () =>
        {
            Interlocked.Increment(ref count);
            return new TestProvider("ConcurrencyProbe", "value");
        });
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => "ConcurrencyProbe".I18N())));
        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(results, Is.All.EqualTo("value"));
        });
    }

    [Test]
    public void UnregisterProvider_RestoresPreviousValueAndDisposesOnce()
    {
        using var original = LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () => new TestProvider("OverrideProbe", "original"));
        var provider = new TestProvider("OverrideProbe", "override");
        using var replacement = LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () => provider);
        Assert.That("OverrideProbe".I18N(), Is.EqualTo("override"));
        replacement.Dispose();
        replacement.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That("OverrideProbe".I18N(), Is.EqualTo("original"));
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void UnregisterProvider_BeforeLookup_DoesNotConstructProvider()
    {
        var hasConstructed = false;
        var registration = LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () =>
        {
            hasConstructed = true;
            return new TestProvider("UnusedProbe", "value");
        });
        registration.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That("UnusedProbe".I18N(), Is.EqualTo("UnusedProbe"));
            Assert.That(hasConstructed, Is.False);
        });
    }

    [Test]
    public void ProviderFactory_CanRegisterFromAnotherThread_WithoutRegistryLockDeadlock()
    {
        using var registration = LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () =>
        {
            using var other = Task.Run(() => LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly,
                () => new TestProvider("InnerProbe", "inner"))).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return new TestProvider("ReentrantProbe", "value");
        });
        Assert.That("ReentrantProbe".I18N(), Is.EqualTo("value"));
    }

    [Test]
    public async Task UnregisterProvider_DuringLookup_WaitsForReadThenDisposes()
    {
        using var hasEntered = new ManualResetEventSlim();
        using var canReturn = new ManualResetEventSlim();
        var provider = new BlockingProvider(hasEntered, canReturn);
        using var registration = LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () => provider);
        var lookup = Task.Run(() => "InFlightProbe".I18N());
        Task? disposal = null;
        try
        {
            Assert.That(hasEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            var hasStartedDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = Task.Run(() =>
            {
                hasStartedDisposal.SetResult();
                registration.Dispose();
            });
            await hasStartedDisposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(provider.DisposeCount, Is.Zero);
        }
        finally
        {
            canReturn.Set();
            await lookup.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposal is not null) await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Multiple(() =>
        {
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
            Assert.That("InFlightProbe".I18N(), Is.EqualTo("InFlightProbe"));
        });
    }

    [Test]
    public void CollectibleContext_Unload_UnregistersProviderAndClearsRetainedToken()
    {
        var context = new AssemblyLoadContext("Locale provider test", isCollectible: true);
        var owner = context.LoadFromAssemblyPath(typeof(LocaleName).Assembly.Location);
        var provider = new TestProvider("UnloadProbe", "value");
        using var registration = LocaleManager.RegisterProvider(owner, () => provider);
        Assert.That("UnloadProbe".I18N(), Is.EqualTo("value"));
        context.Unload();
        Assert.Multiple(() =>
        {
            Assert.That("UnloadProbe".I18N(), Is.EqualTo("UnloadProbe"));
            Assert.That(provider.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ProviderUpdates_OnlyLanguageChangeSendsNotification()
    {
        var oldLocale = LocaleManager.CurrentLocale;
        var recipient = new LanguageRecipient();
        WeakReferenceMessenger.Default.Register(recipient);
        try
        {
            var provider = new TestProvider("UpdateProbe", "first");
            using var registration = LocaleManager.RegisterProvider(typeof(LocaleManagerTests).Assembly, () => provider);
            Assert.That("UpdateProbe".I18N(), Is.EqualTo("first"));
            provider.Value = "second";
            Assert.That("UpdateProbe".I18N(), Is.EqualTo("second"));
            registration.Dispose();
            Assert.That(recipient.Count, Is.Zero);
            LocaleManager.CurrentLocale = oldLocale == LocaleName.En ? LocaleName.De : LocaleName.En;
            LocaleManager.CurrentLocale = LocaleManager.CurrentLocale;
            Assert.That(recipient.Count, Is.EqualTo(1));
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
            LocaleManager.CurrentLocale = oldLocale;
        }
    }

    [Test]
    public void ResourceManager_MissingTranslation_FallsBackPerKey()
    {
        using var provider = new ResourceManagerLocaleResourceProvider("Everywhere.Abstractions.Tests.I18N.ProviderResources", typeof(LocaleManagerTests).Assembly);
        Assert.Multiple(() =>
        {
            Assert.That(provider.TryGetString("Specific", LocaleName.ZhHantHk, out var specific), Is.True);
            Assert.That(specific, Is.EqualTo("Hong Kong"));
            Assert.That(provider.TryGetString("Parent", LocaleName.ZhHantHk, out var parent), Is.True);
            Assert.That(parent, Is.EqualTo("Traditional Chinese"));
            Assert.That(provider.TryGetString("Neutral", LocaleName.ZhHantHk, out var neutral), Is.True);
            Assert.That(neutral, Is.EqualTo("English fallback"));
            Assert.That(provider.TryGetString("Empty", LocaleName.De, out var empty), Is.True);
            Assert.That(empty, Is.Empty);
            Assert.That(provider.TryGetString("Missing", LocaleName.En, out _), Is.False);
        });
    }

    [TestCase(LocaleName.ZhHans, "已复制")]
    [TestCase(LocaleName.En, "Copied")]
    public void ResxModule_SelfRegistersWithoutApplicationInitialization(LocaleName locale, string expected)
    {
        Assert.That(LocaleManager.TryGetString("Common_Copied", locale, out var value), Is.True);
        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void ResxModule_AllCultures_HaveCanonicalSatelliteResourceNames()
    {
        var assembly = typeof(Abstractions.I18N.LocaleKey).Assembly;
        foreach (var locale in Enum.GetValues<LocaleName>().Where(locale => locale != LocaleName.En))
        {
            var culture = locale.ToCultureInfo();
            var satellite = assembly.GetSatelliteAssembly(culture);
            Assert.That(satellite.GetManifestResourceNames(), Does.Contain($"Everywhere.I18N.Strings.{culture.Name}.resources"));
        }
        Assert.That(assembly.GetManifestResourceNames(), Does.Contain("Everywhere.I18N.Strings.resources"));
        Assert.That(assembly.GetType("Everywhere.Abstractions.I18N.LocaleResolver"), Is.Null);
        Assert.That(assembly.GetTypes().Any(type => type.Name.StartsWith("__", StringComparison.Ordinal) && type.Namespace == "Everywhere.Abstractions.I18N"), Is.False);
    }

    [Test]
    public void MissingAndEmptyKeys_PreserveFallback()
    {
        Assert.Multiple(() =>
        {
            Assert.That("UnknownLocalizationKey".I18N(), Is.EqualTo("UnknownLocalizationKey"));
            Assert.That(string.Empty.I18N(), Is.Empty);
            Assert.That(DynamicLocaleKey.Resolve(null), Is.Empty);
            Assert.That(DynamicLocaleKey.Resolve(42), Is.EqualTo("42"));
        });
    }

    public sealed class LanguageRecipient : IRecipient<LocaleChangedMessage>
    {
        public int Count { get; private set; }
        public void Receive(LocaleChangedMessage message) => Count++;
    }

    private class TestProvider(string key, string value) : ILocaleResourceProvider, IDisposable
    {
        public string Value { get; set; } = value;
        public int DisposeCount { get; private set; }

        public virtual bool TryGetString(string resourceKey, LocaleName locale, [NotNullWhen(true)] out string? result)
        {
            result = resourceKey == key ? Value : null;
            return result is not null;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class BlockingProvider(ManualResetEventSlim hasEntered, ManualResetEventSlim canReturn) : TestProvider("InFlightProbe", "value")
    {
        public override bool TryGetString(string resourceKey, LocaleName locale, [NotNullWhen(true)] out string? result)
        {
            hasEntered.Set();
            if (!canReturn.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Lookup was not released.");
            return base.TryGetString(resourceKey, locale, out result);
        }
    }
}
