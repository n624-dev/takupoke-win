using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Takupoke.Infrastructure.Materials;

namespace Takupoke.Win.Platform;

public sealed class WindowsFileIdentity : IFileIdentityProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    public string Identity(FileStream stream)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info) || info.FileIndexHigh == 0 && info.FileIndexLow == 0) throw new SourceException(SourceFailure.Unavailable);
        return $"{info.VolumeSerialNumber:x8}:{info.FileIndexHigh:x8}{info.FileIndexLow:x8}";
    }
}
