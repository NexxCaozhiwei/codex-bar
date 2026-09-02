using CodexBar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodexBar.Tests;

public sealed class CodexSessionLogReaderTests
{
    [Fact]
    public async Task NewSessionFileInvalidatesCachedFileList()
    {
        using var fixture = new DirectoryFixture();
        using var reader = new CodexSessionLogReader(
            new JsonQuotaParser(),
            NullLogger<CodexSessionLogReader>.Instance,
            fixture.Root);

        var firstFile = fixture.Write("first.jsonl", "{}");
        var initial = await reader.ReadRecentLogEntriesAsync(10);
        Assert.Contains(initial, entry => entry.SourceFile == firstFile);

        var secondFile = fixture.Write("second.jsonl", "{}");
        IReadOnlyList<CodexSessionLogEntry> refreshed = [];
        for (var attempt = 0; attempt < 40; attempt++)
        {
            refreshed = await reader.ReadRecentLogEntriesAsync(10);
            if (refreshed.Any(entry => entry.SourceFile == secondFile))
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.Contains(refreshed, entry => entry.SourceFile == secondFile);
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public DirectoryFixture()
        {
            Root = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(),
                "CodexBar.Tests",
                Guid.NewGuid().ToString("N"))).FullName;
        }

        public string Root { get; }

        public string Write(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
