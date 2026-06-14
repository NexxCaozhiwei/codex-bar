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
    private string? _lastScanError;

    public CodexSessionLogReader(JsonQuotaParser parser, ILogger<CodexSessionLogReader> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    public Task<QuotaSnapshot> ReadLatestQuotaAsync(DateTimeOffset? newerThanAppServer = null, CancellationToken cancellationToken = default)
        => Task.Run(() => ReadLatestQuota(newerThanAppServer, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<string>> ReadRecentLinesAsync(int maxLines = 400, CancellationToken cancellationToken = default)
        => Task.Run(() => ReadRecentLines(maxLines, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<CodexSessionLogEntry>> ReadRecentLogEntriesAsync(int maxLines = 400, CancellationToken cancellationToken = default)
        => Task.Run(() => ReadRecentLogEntries(maxLines, cancellationToken), cancellationToken);

    private QuotaSnapshot ReadLatestQuota(DateTimeOffset? newerThanAppServer, CancellationToken cancellationToken)
    {
        string? readError = null;
        QuotaSnapshot? latestSnapshot = null;
        DateTimeOffset? latestEffectiveTime = null;

        foreach (var file in GetCandidateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
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

                    if (latestEffectiveTime is null || effectiveTime > latestEffectiveTime.Value)
                    {
                        latestEffectiveTime = effectiveTime;
                        latestSnapshot = snapshot with { LastRefresh = effectiveTime, Source = QuotaDataSource.JsonlFallback };
                    }

                    break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "读取 Codex session 文件失败：{File}", file.FullName);
                readError ??= CodexDiagnostics.DescribeSessionLogFailure(ex.Message);
            }
        }

        if (latestSnapshot is not null)
        {
            return latestSnapshot;
        }

        return QuotaSnapshot.Empty(_lastScanError ?? readError ?? CodexDiagnostics.NoTokenCount);
    }

    private IReadOnlyList<string> ReadRecentLines(int maxLines, CancellationToken cancellationToken)
        => ReadRecentLogEntries(maxLines, cancellationToken).Select(entry => entry.Line).ToArray();

    private IReadOnlyList<CodexSessionLogEntry> ReadRecentLogEntries(int maxLines, CancellationToken cancellationToken)
    {
        var entries = new List<CodexSessionLogEntry>();
        string? readError = null;
        foreach (var file in GetCandidateFiles().Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileLastWriteTime = new DateTimeOffset(file.LastWriteTimeUtc);
            try
            {
                entries.AddRange(ReadTailLines(file)
                    .Take(maxLines - entries.Count)
                    .Select(line => new CodexSessionLogEntry(line, fileLastWriteTime, file.FullName)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "读取 Codex session 文件失败：{File}", file.FullName);
                readError ??= CodexDiagnostics.DescribeSessionLogFailure(ex.Message);
            }

            if (entries.Count >= maxLines)
            {
                break;
            }
        }

        if (entries.Count == 0 && (_lastScanError is not null || readError is not null))
        {
            throw new IOException(_lastScanError ?? readError);
        }

        return entries;
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
            _lastScanError = null;
            _cachedFiles = [];
            _lastScan = DateTimeOffset.Now;
            return _cachedFiles;
        }

        try
        {
            _cachedFiles = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(MaxFilesToScan)
                .ToArray();
            _lastScanError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "扫描 Codex session 日志目录失败：{Root}", root);
            _cachedFiles = [];
            _lastScanError = CodexDiagnostics.DescribeSessionLogFailure(ex.Message);
        }

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

public sealed record CodexSessionLogEntry(
    string Line,
    DateTimeOffset? FileLastWriteTimeUtc = null,
    string? SourceFile = null);
