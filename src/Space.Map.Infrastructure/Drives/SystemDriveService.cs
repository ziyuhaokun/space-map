using System.IO;
using Space.Map.Core.Abstractions;

namespace Space.Map.Infrastructure.Drives;

public sealed class SystemDriveService : IDriveService
{
    public IReadOnlyList<DriveVolume> GetDrives()
    {
        var list = new List<DriveVolume>();

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return list;
        }

        foreach (var drive in drives)
        {
            var rootPath = drive.Name;
            var name = rootPath.TrimEnd('\\', '/');
            var isReady = false;
            var volumeLabel = string.Empty;
            var format = "—";
            long total = 0;
            long free = 0;

            try
            {
                isReady = drive.IsReady;
                if (isReady)
                {
                    volumeLabel = drive.VolumeLabel ?? string.Empty;
                    format = string.IsNullOrWhiteSpace(drive.DriveFormat) ? "—" : drive.DriveFormat;
                    total = drive.TotalSize;
                    free = drive.TotalFreeSpace;
                }
            }
            catch
            {
                isReady = false;
            }

            var category = drive.DriveType switch
            {
                DriveType.Fixed => DriveCategory.Fixed,
                DriveType.Removable => DriveCategory.Removable,
                DriveType.Network => DriveCategory.Network,
                DriveType.CDRom => DriveCategory.CdRom,
                DriveType.Ram => DriveCategory.Ram,
                DriveType.NoRootDirectory => DriveCategory.NoRootDirectory,
                _ => DriveCategory.Unknown
            };

            list.Add(new DriveVolume(
                rootPath,
                name,
                volumeLabel,
                format,
                category,
                isReady,
                total,
                free));
        }

        return list;
    }

    public string ResolveSystemDriveRoot()
    {
        try
        {
            var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemPath))
            {
                var root = Path.GetPathRoot(systemPath);
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    return root;
                }
            }

            foreach (var drive in GetDrives())
            {
                if (drive.IsReady && drive.Category == DriveCategory.Fixed && Directory.Exists(drive.RootPath))
                {
                    return drive.RootPath;
                }
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                var root = Path.GetPathRoot(userProfile);
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    return root;
                }
            }
        }
        catch
        {
            // Fallback
        }

        return "C:\\";
    }
}
