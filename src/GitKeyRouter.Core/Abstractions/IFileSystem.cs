namespace GitKeyRouter.Core.Abstractions;

public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    Task WriteAllTextAtomicAsync(string path, string content, CancellationToken cancellationToken = default);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    void DeleteFile(string path);

    IEnumerable<string> EnumerateDirectories(string path);
}
