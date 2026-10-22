namespace Space.Map.Core.Scanning;

public readonly record struct DirectoryIdentity(uint VolumeSerialNumber, ulong FileIndex);

public interface IDirectoryIdentityResolver
{
    bool TryGet(string path, out DirectoryIdentity identity, out string errorMessage);
}
