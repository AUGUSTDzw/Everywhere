using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Everywhere.I18N;

namespace Everywhere.Linux.I18N;

internal static class LocaleRegistration
{
    // Registration must remain inert during module initialization. ResourceManager is created on first lookup.
    [SuppressMessage("Usage", "CA2255", Justification = "Resource assemblies self-register without running provider or UI code.")]
    [ModuleInitializer]
    internal static void Register() => LocaleManager.RegisterProvider(
        typeof(LocaleRegistration).Assembly,
        static () => new ResourceManagerLocaleResourceProvider("Everywhere.Linux.I18N.Strings", typeof(LocaleRegistration).Assembly));
}