using System.ComponentModel;

namespace CodexBar.Services;

public static class CodexDiagnostics
{
    private const int MaxRawMessageLength = 180;

    public static string MissingCli =>
        "未找到 Codex CLI。请安装 Codex CLI，或在设置中指定 codex.exe / codex.cmd 路径。";

    public static string NoQuotaData =>
        "没有可用的额度数据。请确认 Codex 已登录，并至少运行过一次 Codex 会话。";

    public static string NoTokenCount =>
        "没有找到可用的 Codex session token_count 额度事件。请运行一次 Codex 后再刷新。";

    public static string DescribeActivityError(string raw)
        => Describe(raw, "最近的 Codex session 事件显示任务出错或中止。");

    public static string DescribeAppServerFailure(Exception exception)
    {
        var raw = exception.Message;
        if (exception is OperationCanceledException or TimeoutException)
        {
            return WithRaw("Codex app-server 请求超时。请检查网络、代理或 VPN，然后刷新。", raw);
        }

        if (exception is Win32Exception)
        {
            return WithRaw("无法启动 Codex app-server。请确认 Codex CLI 路径有效并可执行。", raw);
        }

        var fallback = exception switch
        {
            InvalidOperationException => "Codex app-server 暂不可用。已尝试回退读取本地 session 日志。",
            _ => "Codex app-server 暂不可用。已尝试回退读取本地 session 日志。"
        };

        return Describe(raw, fallback);
    }

    public static string DescribeSessionLogFailure(string raw)
        => WithRaw("无法读取本地 Codex session 日志。请确认 %USERPROFILE%\\.codex\\sessions 存在且当前用户有读取权限。", raw);

    public static string DescribeQuotaUnavailable(params string?[] reasons)
    {
        var reason = reasons.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return reason ?? NoQuotaData;
    }

    private static string Describe(string raw, string fallback)
    {
        if (ContainsAny(raw, "not logged in", "not signed in", "login required", "unauthorized", "authentication", "auth", "401"))
        {
            return WithRaw("Codex 尚未登录或认证已过期。请在终端运行 codex login 后刷新。", raw);
        }

        if (ContainsAny(raw, "network", "offline", "disconnect", "connection reset", "fetch failed", "econnreset", "enotfound", "etimedout", "tls"))
        {
            return WithRaw("检测到网络异常或连接中断。请检查网络、代理或 VPN，然后刷新。", raw);
        }

        if (ContainsAny(raw, "timeout", "timed out", "cancelled", "canceled"))
        {
            return WithRaw("Codex 请求超时。请检查网络、代理或 VPN，稍后刷新。", raw);
        }

        if (ContainsAny(raw, "app-server stdout", "app-server process", "进程未运行", "stdout 已关闭"))
        {
            return WithRaw("Codex app-server 暂不可用。已尝试回退读取本地 session 日志。", raw);
        }

        return WithRaw(fallback, raw);
    }

    private static bool ContainsAny(string? value, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string WithRaw(string detail, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return detail;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxRawMessageLength)
        {
            trimmed = trimmed[..MaxRawMessageLength] + "...";
        }

        return $"{detail} 原始信息：{trimmed}";
    }
}
