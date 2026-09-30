using MonoMod;

// Avalonia's Win32 interop container is internal. These declarations provide the
// compile-time shapes consumed by patch_WindowImpl; MonoMod relinks them to the
// matching nested types in Avalonia.Win32 when weaving the assembly.
namespace Avalonia.Win32.Interop;

[MonoModIgnore]
internal static class UnmanagedMethods
{
    [MonoModIgnore]
    public enum WindowStyles : uint;

    [MonoModIgnore]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}