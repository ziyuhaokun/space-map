using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Space.Map.Core.Scanning;

internal readonly record struct DirectoryIdentity(uint VolumeSerialNumber, ulong FileIndex);

internal static class WindowsDirectoryIdentity
{
    private const uint FileFlagBackupSemantics = 0x02000000;

    public static bool TryGet(
        string path,
        out DirectoryIdentity identity,
        out string errorMessage)
    {
        using var handle = CreateFile(
            path,
            0,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            identity = default;
            errorMessage = FormatWin32Error("Unable to open directory for identity");
            return false;
        }

        if (!GetFileInformationByHandle(handle, out var information))
        {
            identity = default;
            errorMessage = FormatWin32Error("Unable to read directory identity");
            return false;
        }

        var fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
        identity = new DirectoryIdentity(information.VolumeSerialNumber, fileIndex);
        errorMessage = string.Empty;
        return true;
    }

    private static string FormatWin32Error(string operation)
    {
        var errorCode = Marshal.GetLastWin32Error();
        return $"{operation}: {new Win32Exception(errorCode).Message} (Win32 {errorCode}).";
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FILETIME CreationTime;
        public FILETIME LastAccessTime;
        public FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
