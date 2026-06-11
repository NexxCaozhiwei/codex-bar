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
        LastLocation = await _locator.LocateAsync(settings.CodexPath, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? appServerAttemptTime = DateTimeOffset.Now;

        if (LastLocation.Found)
        {
            var appServer = await _appServerClient
                .ReadQuotaAsync(LastLocation.Path!, TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);

            if (appServer.Source == QuotaDataSource.AppServer && (appServer.FiveHour is not null || appServer.Weekly is not null))
            {
                return appServer;
            }

            _logger.LogInformation("app-server 返回后回退到 session jsonl：{Error}", appServer.Error);
        }

        var fallback = await _sessionLogReader.ReadLatestQuotaAsync(appServerAttemptTime, cancellationToken).ConfigureAwait(false);
        if (fallback.Source == QuotaDataSource.JsonlFallback)
        {
            return fallback;
        }

        return QuotaSnapshot.Empty(LastLocation.Error ?? fallback.Error ?? "没有可用的额度数据。");
    }
}
