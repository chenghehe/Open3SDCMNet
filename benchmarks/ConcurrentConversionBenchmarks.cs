using BenchmarkDotNet.Attributes;

namespace Open3SDCMNet.Benchmarks;

[MedianColumn]
public class ConcurrentConversionBenchmarks
{
    private const int BatchSize = 100;
    private (string Input, string Output)[] conversions = null!;
    private DirectoryInfo? outputDirectory;
    private int originalWorkerMinimum;
    private int originalIoMinimum;

    [GlobalSetup]
    public void Setup()
    {
        string[] inputs = BenchmarkInputs.GetFiles();
        ThreadPool.GetMinThreads(out originalWorkerMinimum, out originalIoMinimum);

        outputDirectory = Directory.CreateTempSubdirectory("Open3SDCMNet-benchmark-");
        conversions = new (string, string)[BatchSize];
        for (int i = 0; i < conversions.Length; i++)
        {
            conversions[i] = (inputs[i % inputs.Length], Path.Combine(outputDirectory.FullName, $"scan-{i:D3}.ply"));
        }

        if (!ThreadPool.SetMinThreads(Math.Max(originalWorkerMinimum, BatchSize), originalIoMinimum))
        {
            Cleanup();
            throw new InvalidOperationException("Could not configure the thread pool for the conversion batch.");
        }
    }

    [Benchmark]
    public Task<long> Convert100DcmToPly() => ConvertBatch(useAsync: false);

    [Benchmark]
    public Task<long> Convert100DcmToPlyAsync() => ConvertBatch(useAsync: true);

    private async Task<long> ConvertBatch(bool useAsync)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = new Task<long>[conversions.Length];
        for (int i = 0; i < jobs.Length; i++)
        {
            var (input, output) = conversions[i];
            jobs[i] = ConvertAsync(start.Task, input, output, useAsync);
        }

        start.SetResult();
        long[] triangleCounts = await Task.WhenAll(jobs).ConfigureAwait(false);
        return triangleCounts.Sum();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (outputDirectory is null)
            return;

        ThreadPool.SetMinThreads(originalWorkerMinimum, originalIoMinimum);
        outputDirectory.Delete(recursive: true);
        outputDirectory = null;
    }

    private static async Task<long> ConvertAsync(Task start, string input, string output, bool useAsync)
    {
        await start.ConfigureAwait(false);
        if (useAsync)
        {
            var asyncMesh = await DcmReader.ReadAsync(input).ConfigureAwait(false);
            await MeshWriter.WriteAsync(asyncMesh, output).ConfigureAwait(false);
            return asyncMesh.Triangles.Count;
        }

        var mesh = DcmReader.Read(input);
        MeshWriter.Write(mesh, output);
        return mesh.Triangles.Count;
    }
}
