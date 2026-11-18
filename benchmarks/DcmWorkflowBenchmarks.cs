using BenchmarkDotNet.Attributes;

namespace Open3SDCMNet.Benchmarks;

[MemoryDiagnoser]
[MedianColumn]
public class DcmWorkflowBenchmarks
{
    private string inputPath = null!;
    private DirectoryInfo? outputDirectory;
    private string stlPath = null!;
    private string plyPath = null!;
    private string objPath = null!;
    private DcmMesh mesh = null!;

    [ParamsSource(nameof(InputFiles))]
    public string InputFile { get; set; } = null!;

    public IEnumerable<string> InputFiles => BenchmarkInputs.GetFiles().Select(static path => Path.GetFileName(path));

    [GlobalSetup(Targets = [nameof(ReadDcm), nameof(ReadDcmAsync)])]
    public void SetupReader() => inputPath = Path.Combine(BenchmarkInputs.GetDirectory(), InputFile);

    [GlobalSetup(Targets = [nameof(ConvertDcmToPly), nameof(ConvertDcmToPlyAsync)])]
    public void SetupConversion()
    {
        SetupReader();
        outputDirectory = Directory.CreateTempSubdirectory("open3sdcm-benchmark-");
        stlPath = Path.Combine(outputDirectory.FullName, "mesh.stl");
        plyPath = Path.Combine(outputDirectory.FullName, "mesh.ply");
        objPath = Path.Combine(outputDirectory.FullName, "mesh.obj");
    }

    [GlobalSetup(Targets = [nameof(ExportStl), nameof(ExportPly), nameof(ExportObj)])]
    public void SetupExporter()
    {
        SetupConversion();
        mesh = DcmReader.Read(inputPath);
    }

    [Benchmark]
    public DcmMesh ReadDcm() => DcmReader.Read(inputPath);

    [Benchmark]
    public Task<DcmMesh> ReadDcmAsync() => DcmReader.ReadAsync(inputPath);

    [Benchmark]
    public void ExportStl() => MeshWriter.Write(mesh, stlPath);

    [Benchmark]
    public void ExportPly() => MeshWriter.Write(mesh, plyPath);

    [Benchmark]
    public void ExportObj() => MeshWriter.Write(mesh, objPath);

    [Benchmark]
    public void ConvertDcmToPly() => MeshWriter.Write(DcmReader.Read(inputPath), plyPath);

    [Benchmark]
    public async Task ConvertDcmToPlyAsync()
    {
        var mesh = await DcmReader.ReadAsync(inputPath).ConfigureAwait(false);
        await MeshWriter.WriteAsync(mesh, plyPath).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => outputDirectory?.Delete(recursive: true);
}
