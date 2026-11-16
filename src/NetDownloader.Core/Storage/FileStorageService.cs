using Microsoft.Win32.SafeHandles;

namespace NetDownloader.Core.Storage;

public class FileStorageService : IFileStorageService
{
    public string GetPartFilePath(string destinationDirectory, string fileName)
    {
        return Path.Combine(destinationDirectory, fileName + ".part");
    }

    public string GetFinalFilePath(string destinationDirectory, string fileName)
    {
        return Path.Combine(destinationDirectory, fileName);
    }

    public SafeFileHandle OpenOrCreatePartFile(string partFilePath, long expectedTotalBytes)
    {
        var dir = Path.GetDirectoryName(partFilePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var handle = File.OpenHandle(
            partFilePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            FileOptions.Asynchronous
        );

        if (expectedTotalBytes > 0)
        {
            var currentLength = RandomAccess.GetLength(handle);
            if (currentLength < expectedTotalBytes)
            {
                RandomAccess.SetLength(handle, expectedTotalBytes);
            }
        }

        return handle;
    }

    public ValueTask WriteAsync(SafeFileHandle handle, ReadOnlyMemory<byte> buffer, long fileOffset, CancellationToken cancellationToken = default)
    {
        return RandomAccess.WriteAsync(handle, buffer, fileOffset, cancellationToken);
    }

    public void FinalizePartFile(string partFilePath, string finalFilePath)
    {
        if (!File.Exists(partFilePath))
            throw new FileNotFoundException("Part file not found", partFilePath);

        var dir = Path.GetDirectoryName(finalFilePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(finalFilePath))
        {
            File.Delete(finalFilePath);
        }

        File.Move(partFilePath, finalFilePath, overwrite: true);
    }

    public void DeleteFileIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Ignore cleanup error
            }
        }
    }

    public bool FileExists(string filePath) => File.Exists(filePath);
}
