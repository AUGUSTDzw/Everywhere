using System.Diagnostics.CodeAnalysis;

namespace Everywhere.I18N;

internal static class LocaleRegistration
{
    // Registration must remain inert during module initialization. ResourceManager is created on first lookup.
    [SuppressMessage("Usage", "CA2255", Justification = "Resource assemblies self-register without running provider or UI code.")]
    [ModuleInitializer]
    internal static void Register() => LocaleManager.RegisterProvider(
        typeof(LocaleRegistration).Assembly,
        static () => new ResourceManagerLocaleResourceProvider("Everywhere.I18N.Strings", typeof(LocaleRegistration).Assembly));
}