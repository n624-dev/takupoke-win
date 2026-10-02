using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Takupoke.Win.UITests;

internal static partial class Program
{
    private static int CheckShortcut(string shortcut, string executable, string directory, string icon)
    {
        // Load the existing link explicitly. WScript.CreateShortcut can silently
        // return an empty new object when it cannot load a link in the host.
        object instance = new NativeShellLink();
        try
        {
            ((IPersistFile)instance).Load(Path.GetFullPath(shortcut), 0);
            var link = (IShellLinkW)instance;
            var target = new StringBuilder(32768); var working = new StringBuilder(32768); var image = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 4);
            link.GetWorkingDirectory(working, working.Capacity);
            link.GetIconLocation(image, image.Capacity, out var index);
            static bool Same(string actual, string expected) => actual.Length > 0 && string.Equals(
                Path.GetFullPath(actual.Trim('"')).TrimEnd('\\'), Path.GetFullPath(expected).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            Require(Same(target.ToString(), executable), $"Shortcut target [{target}] equals [{executable}]");
            Require(Same(working.ToString(), directory), $"Shortcut working directory [{working}] equals [{directory}]");
            Require(index == 0 && Same(image.ToString(), icon), $"Shortcut icon [{image},{index}] equals [{icon},0]");
            Console.WriteLine("Verified the installed native shortcut target, working directory and product icon.");
            return 0;
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class NativeShellLink;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, nint findData, uint flags);
        void GetIDList(out nint value);
        void SetIDList(nint value);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetHotkey(out ushort value);
        void SetHotkey(ushort value);
        void GetShowCmd(out int value);
        void SetShowCmd(int value);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
