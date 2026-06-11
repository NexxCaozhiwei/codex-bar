using CodexBar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodexBar.Tests;

public sealed class CodexLocatorTests
{
    [Fact]
    public async Task MissingCodexReturnsClearError()
    {
        var locator = new CodexLocator(
            NullLogger<CodexLocator>.Instance,
            (_, _) => Task.FromResult<string?>(null),
            []);
        var result = await locator.LocateAsync(@"Z:\definitely-not-installed\codex.exe");

        Assert.False(result.Found);
        Assert.Contains("Codex CLI was not found", result.Error);
    }
}
