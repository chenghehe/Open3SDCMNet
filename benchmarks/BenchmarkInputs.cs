namespace Open3SDCMNet.Benchmarks;

internal static class BenchmarkInputs
{
    internal const string DirectoryVariable = "OPEN3SDCM_BENCHMARK_INPUT_DIR";

    internal static string GetDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(DirectoryVariable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? "Files" : configured);
    }

    internal static string[] GetFiles()
    {
        string directory = GetDirectory();
        string[] files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
                .Where(path => Path.GetExtension(path).Equals(".dcm", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

        if (files.Length == 0)
            throw new InvalidOperationException(
                $"No DCM files found in '{directory}'. Set {DirectoryVariable} to a directory containing 3Shape scans.");

        return files;
    }
}
