using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class QuotaService
{
    private readonly CodexLocator _locator;
    private readonly CodexAppServerClient _appServerClient;
    private readonly CodexSessionLogReader _sessionLogReader;
    private readonly ILogger<QuotaService> _logger;

    public QuotaService(
        CodexLocator locator,
        CodexAppServerClient appServerClient,
        CodexSessionLogReader sessionLogReader,
        ILogger<QuotaService> logger)
    {
        _locator = locator;
        _appServerClient = appServerClient;
        _sessionLogReader = sessionLogReader;
        _logger = logger;
    }

    public CodexLocationResult LastLocation { get; private set; } = new(null, "尚未检查。");

    public async Task<QuotaSnapshot> ReadAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        LastLocation = await _locator.LocateAsync(settings.CodexPath, cancellationToken).ConfigureAwait(false);
        string? appServerError = null;
        QuotaSnapshot? appServerAccountData = null;
        if (LastLocation.Found)
        {
            var appServer = await _appServerClient
                .ReadQuotaAsync(LastLocation.Path!, TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            if (appServer.Source == QuotaDataSource.AppServer && appServer.HasQuotaWindows)
            {
                return QuotaSnapshotNormalizer.NormalizeExpiredWindows(appServer, now);
            }

            appServerError = appServer.Error;
            if (appServer.Source == QuotaDataSource.AppServer && appServer.HasQuotaData)
            {
                appServerAccountData = appServer;
                appServerError ??= "app-server 未返回额度窗口。";
            }

            _logger.LogInformation("app-server 返回后回退到 session jsonl：{Error}", appServerError);
        }

        var fallback = await _sessionLogReader.ReadLatestQuotaAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (fallback.Source == QuotaDataSource.JsonlFallback && fallback.HasQuotaWindows)
        {
            var combined = appServerAccountData is null
                ? fallback
                : QuotaSnapshotNormalizer.MergeFallbackWithAccountData(fallback, appServerAccountData);
            return QuotaSnapshotNormalizer.NormalizeExpiredWindows(combined, now);
        }

        if (appServerAccountData is not null)
        {
            return QuotaSnapshotNormalizer.NormalizeExpiredWindows(appServerAccountData, now);
        }

        if (fallback.Source == QuotaDataSource.JsonlFallback && fallback.HasQuotaData)
        {
            return QuotaSnapshotNormalizer.NormalizeExpiredWindows(fallback, now);
        }

        return QuotaSnapshotNormalizer.NormalizeExpiredWindows(QuotaSnapshot.Empty(CodexDiagnostics.DescribeQuotaUnavailable(
            LastLocation.Error,
            appServerError,
            fallback.Error,
            CodexDiagnostics.NoQuotaData)), now);
    }
}
