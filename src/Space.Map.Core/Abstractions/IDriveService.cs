namespace Space.Map.Core.Abstractions;

public interface IDriveService
{
    IReadOnlyList<DriveVolume> GetDrives();

    string ResolveSystemDriveRoot();
}
