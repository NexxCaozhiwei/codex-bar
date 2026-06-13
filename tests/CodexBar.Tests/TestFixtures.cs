namespace CodexBar.Tests;

internal static class TestFixtures
{
    public static string ReadText(string fileName)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    public static string[] ReadJsonlNewestFirst(string fileName)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Reverse()
            .ToArray();
}
