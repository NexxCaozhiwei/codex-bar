using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class ProjectResolverTests
{
    [Fact]
    public async Task GitRootUsesRepositoryDirectoryName()
    {
        using var fixture = new DirectoryFixture();
        var repository = fixture.CreateDirectory("tactic-echo");
        var locator = new StubGitRootLocator(repository);
        var resolver = new ProjectResolver(locator);

        var result = await resolver.ResolveAsync(repository);

        Assert.Equal("tactic-echo", result.ProjectName);
        Assert.Equal(repository, result.GitRoot);
    }

    [Fact]
    public async Task GitSubdirectoryStillUsesRepositoryName()
    {
        using var fixture = new DirectoryFixture();
        var repository = fixture.CreateDirectory("tactic-echo");
        var subdirectory = Directory.CreateDirectory(Path.Combine(repository, "src", "Addon")).FullName;
        var resolver = new ProjectResolver(new StubGitRootLocator(repository));

        var result = await resolver.ResolveAsync(subdirectory);

        Assert.Equal("tactic-echo", result.ProjectName);
        Assert.Equal(subdirectory, result.WorkingDirectory);
    }

    [Fact]
    public async Task NonGitDirectoryUsesWorkingDirectoryName()
    {
        using var fixture = new DirectoryFixture();
        var directory = fixture.CreateDirectory("中文项目");
        var resolver = new ProjectResolver(new StubGitRootLocator(null));

        var result = await resolver.ResolveAsync(directory);

        Assert.Equal("中文项目", result.ProjectName);
        Assert.Null(result.GitRoot);
    }

    [Fact]
    public async Task EmptyPathReturnsNoProject()
    {
        var locator = new StubGitRootLocator(null);
        var resolver = new ProjectResolver(locator);

        var result = await resolver.ResolveAsync("  ");

        Assert.Null(result.ProjectName);
        Assert.Equal(0, locator.CallCount);
    }

    [Fact]
    public async Task MissingPathReturnsNoProject()
    {
        var locator = new StubGitRootLocator(null);
        var resolver = new ProjectResolver(locator);
        var missing = Path.Combine(Path.GetTempPath(), "CodexBar.Tests", Guid.NewGuid().ToString("N"));

        var result = await resolver.ResolveAsync(missing);

        Assert.Null(result.ProjectName);
        Assert.Equal(0, locator.CallCount);
    }

    [Fact]
    public async Task RepeatedWorkingDirectoryUsesCachedGitResult()
    {
        using var fixture = new DirectoryFixture();
        var directory = fixture.CreateDirectory("cached-project");
        var locator = new StubGitRootLocator(directory);
        var resolver = new ProjectResolver(locator);

        await resolver.ResolveAsync(directory);
        await resolver.ResolveAsync(directory);

        Assert.Equal(1, locator.CallCount);
    }

    private sealed class StubGitRootLocator : IGitRootLocator
    {
        private readonly string? _root;

        public StubGitRootLocator(string? root)
        {
            _root = root;
        }

        public int CallCount { get; private set; }

        public Task<string?> FindRootAsync(string workingDirectory, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_root);
        }
    }

    private sealed class DirectoryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "CodexBar.Tests",
            Guid.NewGuid().ToString("N"));

        public string CreateDirectory(string name)
            => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
