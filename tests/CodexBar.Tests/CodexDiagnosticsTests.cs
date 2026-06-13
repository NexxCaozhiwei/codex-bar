using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class CodexDiagnosticsTests
{
    [Theory]
    [InlineData("fetch failed: ECONNRESET", "网络异常")]
    [InlineData("authentication failed: login required", "认证已过期")]
    [InlineData("request timed out", "请求超时")]
    public void ActivityErrorsReturnActionableDetails(string raw, string expected)
    {
        var detail = CodexDiagnostics.DescribeActivityError(raw);

        Assert.Contains(expected, detail);
        Assert.Contains(raw, detail);
    }

    [Fact]
    public void AppServerTimeoutReturnsActionableDetail()
    {
        var detail = CodexDiagnostics.DescribeAppServerFailure(new TimeoutException("stdio timeout"));

        Assert.Contains("app-server 请求超时", detail);
        Assert.Contains("stdio timeout", detail);
    }
}
