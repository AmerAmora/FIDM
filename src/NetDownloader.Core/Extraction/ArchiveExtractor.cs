using System.Text.RegularExpressions;
using NetDownloader.Core.Models;
using NetDownloader.Core.Storage;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace NetDownloader.Core.Extraction;

public partial class ArchiveExtractor : IArchiveExtractor
{
    private readonly IFileStorageService _fileStorage;
    private readonly EngineConfiguration _config;

    [GeneratedRegex(@"\.(rar|zip|7z|tar|gz|tgz|001|r\d{2}|z\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchiveExtensionRegex();

    [GeneratedRegex(@"\.part0*1\.(rar|exe)$", RegexOptions.IgnoreCase)]
    private static partial Regex PartOneRegex();

    public ArchiveExtractor(IFileStorageService fileStorage, EngineConfiguration? config = null)
    {
        _fileStorage = fileStorage;
        _config = config ?? new EngineConfiguration();
    }

    public bool IsArchive(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        return ArchiveExtensionRegex().IsMatch(fileName);
    }

    public async Task<bool> ExtractPackageAsync(
        DownloadPackage package,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var finalFiles = package.Items
                    .Select(i => _fileStorage.GetFinalFilePath(package.DestinationDirectory, i.FileName))
                    .Where(File.Exists)
                    .ToList();

                if (finalFiles.Count == 0)
                    return false;

                var primaryVolume = FindPrimaryVolume(finalFiles);
                if (primaryVolume == null)
                    return false;

                var readerOptions = new ReaderOptions
                {
                    Password = package.ExtractionPassword
                };

                IArchive archive;
                var fileInfos = finalFiles.Select(f => new FileInfo(f)).ToList();

                if (fileInfos.Count > 1)
                {
                    try
                    {
                        archive = ArchiveFactory.OpenArchive(fileInfos, readerOptions);
                    }
                    catch
                    {
                        archive = ArchiveFactory.OpenArchive(primaryVolume, readerOptions);
                    }
                }
                else
                {
                    archive = ArchiveFactory.OpenArchive(primaryVolume, readerOptions);
                }

                using (archive)
                {
                    var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                    long totalSize = entries.Sum(e => e.Size);
                    long extractedSize = 0;

                    var extractionOptions = new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    };

                    foreach (var entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        entry.WriteToDirectory(package.DestinationDirectory, extractionOptions);

                        extractedSize += entry.Size;
                        if (totalSize > 0)
                        {
                            double pct = Math.Clamp((double)extractedSize / totalSize * 100.0, 0.0, 100.0);
                            progress?.Report(pct);
                        }
                    }
                }

                progress?.Report(100.0);

                if (_config.DeleteArchiveAfterExtract)
                {
                    foreach (var file in finalFiles)
                    {
                        _fileStorage.DeleteFileIfExists(file);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }

    public string? FindPrimaryVolume(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
            return null;

        // 1. Look for .part1.rar or .part01.rar or .part001.rar
        var partOne = filePaths.FirstOrDefault(f => PartOneRegex().IsMatch(f));
        if (partOne != null) return partOne;

        // 2. Look for .7z.001 or .001
        var splitOne = filePaths.FirstOrDefault(f => f.EndsWith(".001", StringComparison.OrdinalIgnoreCase));
        if (splitOne != null) return splitOne;

        // 3. Look for .r00
        var r00 = filePaths.FirstOrDefault(f => f.EndsWith(".r00", StringComparison.OrdinalIgnoreCase));
        if (r00 != null) return r00;

        // 4. Look for standard extensions (.rar, .zip, .7z, .tar.gz, etc.)
        var standardArchive = filePaths.FirstOrDefault(f =>
            f.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase));

        return standardArchive ?? filePaths.FirstOrDefault();
    }
}
