using System.Diagnostics;
using System.Text.Json;
using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class CodexAppServerClient : IDisposable
{
    private readonly JsonQuotaParser _parser;
    private readonly ILogger<CodexAppServerClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private int _nextId = 1;
    private bool _initialized;

    public CodexAppServerClient(JsonQuotaParser parser, ILogger<CodexAppServerClient> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    public async Task<QuotaSnapshot> ReadQuotaAsync(string codexPath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            EnsureProcess(codexPath);
            if (!_initialized)
            {
                await SendRequestAsync("initialize", new
                {
                    clientInfo = new { name = "codex-bar", title = "Codex Bar", version = "0.1.6" },
                    capabilities = new { experimentalApi = true, optOutNotificationMethods = Array.Empty<string>() }
                }, timeoutCts.Token).ConfigureAwait(false);
                _initialized = true;
            }

            var response = await SendRequestAsync("account/rateLimits/read", null, timeoutCts.Token, includeParams: false).ConfigureAwait(false);
            var snapshot = _parser.ParseAppServerResponse(response);
            return snapshot with { Source = QuotaDataSource.AppServer };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested == false)
        {
            _logger.LogWarning(ex, "读取 Codex app-server 额度失败。");
            ResetProcess();
            return QuotaSnapshot.Empty($"app-server 失败：{ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken, bool includeParams = true)
    {
        if (_process?.StandardInput is null || _process.StandardOutput is null)
        {
            throw new InvalidOperationException("app-server 进程未运行。");
        }

        var id = _nextId++;
        var request = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method
        };

        if (includeParams)
        {
            request["params"] = parameters;
        }

        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync().ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var lineTask = _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
            var line = await lineTask.ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException("app-server stdout 已关闭。");
            }

            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var idElement) &&
                idElement.TryGetInt32(out var responseId) &&
                responseId == id)
            {
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException(error.ToString());
                }

                return line;
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private void EnsureProcess(string codexPath)
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        ResetProcess();

        var startInfo = new ProcessStartInfo
        {
            FileName = codexPath,
            Arguments = "app-server --listen stdio://",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("启动 codex app-server 失败。");
        _ = Task.Run(async () =>
        {
            try
            {
                while (_process is { HasExited: false } process)
                {
                    var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    _logger.LogDebug("codex app-server stderr: {Line}", line);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "stderr 读取已停止。");
            }
        });
    }

    private void ResetProcess()
    {
        _initialized = false;
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }

        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        ResetProcess();
        _gate.Dispose();
    }
}
