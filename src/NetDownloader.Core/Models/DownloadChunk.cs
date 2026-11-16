namespace NetDownloader.Core.Models;

public class DownloadChunk
{
    public int ChunkIndex { get; set; }
    public long StartByte { get; set; }
    public long EndByte { get; set; }
    public long CurrentByteOffset { get; set; }
    public bool IsCompleted { get; set; }

    public long TotalBytes => EndByte >= StartByte ? (EndByte - StartByte + 1) : 0;
    public long DownloadedBytes => Math.Clamp(CurrentByteOffset - StartByte, 0, TotalBytes);
    public double ProgressPercentage => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100.0 : (IsCompleted ? 100.0 : 0.0);

    public DownloadChunk()
    {
    }

    public DownloadChunk(int chunkIndex, long startByte, long endByte)
    {
        ChunkIndex = chunkIndex;
        StartByte = startByte;
        EndByte = endByte;
        CurrentByteOffset = startByte;
        IsCompleted = false;
    }
}
