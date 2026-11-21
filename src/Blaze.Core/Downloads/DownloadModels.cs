using System.Text.Json.Serialization;

namespace Blaze.Core.Downloads;

public enum DownloadStatus { Queued, Probing, Downloading, WaitingForNetwork, Paused, Completed, Failed }

/// <summary>A byte range [Start, End] of the target file. Downloaded counts bytes already on disk from Start.</summary>
public sealed class Segment
{
    public long Start { get; set; }
    public long End { get; set; }
    public long Downloaded { get; set; }

    [JsonIgnore] public bool Assigned { get; set; }
    [JsonIgnore] public long Length => End - Start + 1;
    [JsonIgnore] public long Remaining => Length - Downloaded;
    [JsonIgnore] public long Position => Start + Downloaded;
}

public sealed class DownloadJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Directory { get; set; } = "";
    public string FileName { get; set; } = "";
    public long TotalBytes { get; set; } = -1;
    public bool Resumable { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public int Connections { get; set; } = 4;
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string? Error { get; set; }
    public string? Referer { get; set; }
    public string? Cookie { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<Segment> Segments { get; set; } = new();

    [JsonIgnore] public double BytesPerSecond { get; set; }
    [JsonIgnore] public string? Notice { get; set; }

    [JsonIgnore]
    public long DownloadedBytes
    {
        get
        {
            long sum = 0;
            lock (Segments) foreach (var s in Segments) sum += Math.Max(0, s.Downloaded);
            return Status == DownloadStatus.Completed && TotalBytes >= 0 ? TotalBytes : sum;
        }
    }

    [JsonIgnore] public string PartPath => Path.Combine(Directory, $"{FileName}.{Id[..8]}.blazepart");
    [JsonIgnore] public string FinalPath => Path.Combine(Directory, FileName);
}

public sealed class DownloadOptions
{
    public int DefaultConnections { get; set; } = 4;
    public long MinSegmentBytes { get; set; } = 1 << 20;
    public long SpeedLimitBytesPerSec { get; set; }                 // 0 = unlimited
    public int MaxParallelJobs { get; set; } = 3;
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan SaveInterval { get; set; } = TimeSpan.FromSeconds(1);
    public bool WatchOsNetwork { get; set; } = true;               // wake instantly when the NIC comes back
    public bool AutoResumeOnLoad { get; set; } = true;
    public string UserAgent { get; set; } = "Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0";
}

/// <summary>Do not retry: 404, 403, disk full, etc.</summary>
public sealed class FatalDownloadException(string message) : Exception(message);

/// <summary>The remote file is not the one we started: restart from zero.</summary>
public sealed class ContentChangedException(string message) : Exception(message);

/// <summary>Retry later: 5xx, 429, 408.</summary>
public sealed class TransientHttpException(string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
