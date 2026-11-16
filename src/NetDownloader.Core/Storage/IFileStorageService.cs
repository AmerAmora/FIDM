using Microsoft.Win32.SafeHandles;

namespace NetDownloader.Core.Storage;

public interface IFileStorageService
{
    string GetPartFilePath(string destinationDirectory, string fileName);
    string GetFinalFilePath(string destinationDirectory, string fileName);
    SafeFileHandle OpenOrCreatePartFile(string partFilePath, long expectedTotalBytes);
    ValueTask WriteAsync(SafeFileHandle handle, ReadOnlyMemory<byte> buffer, long fileOffset, CancellationToken cancellationToken = default);
    void FinalizePartFile(string partFilePath, string finalFilePath);
    void DeleteFileIfExists(string filePath);
    bool FileExists(string filePath);
}
