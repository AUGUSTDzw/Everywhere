using Everywhere.Patches.Contracts.Interop;
using MonoMod;
using static Avalonia.Win32.Interop.UnmanagedMethods;

namespace Everywhere.Patches.Avalonia.Win32;

[MonoModPatch("Avalonia.Win32.WindowImpl")]
internal class patch_WindowImpl : IWindowCornerRadiusFeature
{
    private long _everywhereCornerRadiusBits;
    private int _everywhereCornerRadiusConfigured;
    private int _everywhereCornerRadiusSuppressed;

    public void SetCornerRadius(double radius)
    {
        if (!double.IsFinite(radius) || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "The corner radius must be finite and non-negative.");
        }

        radius = Math.Min(radius, 4096);
        Volatile.Write(ref _everywhereCornerRadiusBits, BitConverter.DoubleToInt64Bits(radius));
        Volatile.Write(ref _everywhereCornerRadiusConfigured, 1);
    }

    public void SetCornerRadiusSuppressed(bool suppressed) =>
        Volatile.Write(ref _everywhereCornerRadiusSuppressed, suppressed ? 1 : 0);

    public bool TryGetEffectiveCornerRadius(out double radius)
    {
        if (Volatile.Read(ref _everywhereCornerRadiusConfigured) == 0)
        {
            radius = 0;
            return false;
        }

        if (Volatile.Read(ref _everywhereCornerRadiusSuppressed) != 0)
        {
            radius = 0;
            return true;
        }

        var bits = Volatile.Read(ref _everywhereCornerRadiusBits);
        radius = BitConverter.Int64BitsToDouble(bits);
        return true;
    }

    private extern RECT orig_ClientRectToWindowRect(
        RECT clientRect,
        WindowStyles? styleOverride = null,
        WindowStyles? extendedStyleOverride = null);

    private RECT ClientRectToWindowRect(
        RECT clientRect,
        WindowStyles? styleOverride = null,
        WindowStyles? extendedStyleOverride = null)
    {
        // The custom frame handles WM_NCCALCSIZE by making the complete restored
        // window rectangle client area. Do not let Avalonia add the native caption
        // and resize-frame metrics to a requested client size a second time.
        //
        // Keep explicit style conversions intact. Resize() uses one to calculate its
        // extended-client-area top correction, including the collapsed caption pixel.
        if (Volatile.Read(ref _everywhereCornerRadiusConfigured) != 0 &&
            styleOverride is null &&
            extendedStyleOverride is null)
        {
            return clientRect;
        }

        return orig_ClientRectToWindowRect(clientRect, styleOverride, extendedStyleOverride);
    }
}
