using System.IO;
using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class CodexSessionLogReader
{
    private const int MaxFilesToScan = 120;
    private const int MaxBytesPerFile = 4 * 1024 * 1024;
    private readonly JsonQuotaParser _parser;
    private readonly ILogger<CodexSessionLogReader> _logger;
    private IReadOnlyList<FileInfo> _cachedFiles = [];
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;

    public CodexSessionLogReader(JsonQuotaParser parser, ILogger<CodexSessionLogReader> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    public Task<QuotaSnapshot> ReadLatestQuotaAsync(DateTimeOffset? newerThanAppServer = null, CancellationToken cancellationToken = default)
        => Task.Run(() => ReadLatestQuota(newerThanAppServer, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<string>> ReadRecentLinesAsync(int maxLines = 400, CancellationToken cancellationToken = default)
        => Task.Run(() => ReadRecentLines(maxLines, cancellationToken), cancellationToken);

    private QuotaSnapshot ReadLatestQuota(DateTimeOffset? newerThanAppServer, CancellationToken cancellationToken)
    {
        foreach (var file in GetCandidateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var line in ReadTailLines(file))
            {
                QuotaSnapshot? snapshot;
                try
                {
                    snapshot = _parser.ParseJsonlTokenCountLine(line);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "解析 {File} 中的 token_count 行失败。", file.FullName);
                    continue;
                }

                if (snapshot is null)
                {
                    continue;
                }

                var effectiveTime = snapshot.LastRefresh == default
                    ? new DateTimeOffset(file.LastWriteTimeUtc)
                    : snapshot.LastRefresh;

                if (newerThanAppServer is not null && effectiveTime < newerThanAppServer.Value)
                {
                    continue;
                }

                return snapshot with { LastRefresh = effectiveTime, Source = QuotaDataSource.JsonlFallback };
            }
        }

        return QuotaSnapshot.Empty("没有找到可用的 Codex session token_count 额度事件。");
    }

    private IReadOnlyList<string> ReadRecentLines(int maxLines, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var file in GetCandidateFiles().Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.AddRange(ReadTailLines(file).Take(maxLines - lines.Count));
            if (lines.Count >= maxLines)
            {
                break;
            }
        }

        return lines;
    }

    private IReadOnlyList<FileInfo> GetCandidateFiles()
    {
        if ((DateTimeOffset.Now - _lastScan).TotalSeconds < 30 && _cachedFiles.Count > 0)
        {
            return _cachedFiles;
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "sessions");

        if (!Directory.Exists(root))
        {
            _cachedFiles = [];
            _lastScan = DateTimeOffset.Now;
            return _cachedFiles;
        }

        _cachedFiles = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxFilesToScan)
            .ToArray();
        _lastScan = DateTimeOffset.Now;
        return _cachedFiles;
    }

    private static IEnumerable<string> ReadTailLines(FileInfo file)
    {
        if (!file.Exists || file.Length == 0)
        {
            yield break;
        }

        using var stream = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var start = Math.Max(0, stream.Length - MaxBytesPerFile);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var allLines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            allLines.Add(line);
        }

        for (var i = allLines.Count - 1; i >= 0; i--)
        {
            yield return allLines[i];
        }
    }
}
