using System.Globalization;
using System.Numerics;
using System.Text;
using Xunit;

namespace Open3SDCMNet.Tests;

public class MeshWriterTests
{
    // JPEG with distinct colors in each corner, in top-to-bottom row order.
    private const string CornerJpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB"
        + "AQEBAQEBAQEBAQEBAQEBAQH/2wBDAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB"
        + "AQEBAQEBAQEBAQEBAQH/wAARCAACAAIDAREAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABoQAAMBAQEBAAAAAAAA"
        + "AAAAAAMEBQIGBwH/xAAVAQEBAAAAAAAAAAAAAAAAAAAGCf/EAB4RAAIDAQACAwAAAAAAAAAAAAEGAgQFBwMIABEh/9oADAMB"
        + "AAIRAxEAPwATPWLdmV6n6VLl16c2bN7/ALJCdOQfaTRQRT6KkuokkouUa6qiq4xgWWAMYQBHgQsZxnOflqvVDY15erfrXKWp"
        + "oylLgPHJSlK9ZMpSPO1wmUifKSSSSSSfsn9Pxxw5ER+icU4/0DoCaqPT49ct5+4uzs4ruQzNzi3M6nkbbI1NTJt072ywsbDs"
        + "3rutubmtdt6etp27V+/asWrHl8s//9k=";

    [Fact]
    public async Task PlyExportWritesNativeVertexColors()
    {
        byte[] bgra = [3, 2, 1, 255, 6, 5, 4, 255, 9, 8, 7, 255, 12, 11, 10, 255];
        var mesh = ReadColorScan(native: Convert.ToBase64String(bgra));

        Assert.Equal<Rgb>([new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12)],
            await ExportColors(mesh));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task PlyExportBakesCornerTextureColorsAndPreservesFallback(bool swapRedBlue, bool hasBaseColor,
        bool hasNativeColors)
    {
        byte[] bgra = [43, 42, 41, 255, 43, 42, 41, 255, 43, 42, 41, 255, 43, 42, 41, 255];
        var mesh = ReadColorScan(texture: CornerJpeg, native: hasNativeColors ? Convert.ToBase64String(bgra) : "",
            hasBaseColor: hasBaseColor);
        var colors = await ExportColors(mesh, swapRedBlue);
        Rgb[] expected = [new(130, 54, 129), new(100, 20, 181), default, new(11, 222, 31)];

        foreach (int i in new[] { 0, 1, 3 })
        {
            var color = expected[i];
            if (swapRedBlue)
                color = new Rgb(color.Blue, color.Green, color.Red);
            Assert.InRange((int)colors[i].Red, color.Red - 2, color.Red + 2);
            Assert.InRange((int)colors[i].Green, color.Green - 2, color.Green + 2);
            Assert.InRange((int)colors[i].Blue, color.Blue - 2, color.Blue + 2);
        }
        var fallback = hasNativeColors ? new Rgb(41, 42, 43)
            : hasBaseColor ? new Rgb(1, 2, 3) : new Rgb(179, 179, 179);
        Assert.Equal(fallback, colors[2]);
    }

    [Fact]
    public async Task PlyExportFallsBackWhenTextureCannotBeDecoded()
    {
        var mesh = ReadColorScan(texture: "AQID", hasBaseColor: true);

        Assert.All(await ExportColors(mesh), color => Assert.Equal(new Rgb(1, 2, 3), color));
    }

    private static DcmMesh ReadColorScan(string texture = "", string native = "", bool hasBaseColor = false)
    {
        // Vertex 0 has two UVs on the top row. Vertex 2 has no UV.
        byte[] uv = [2, 0, 0, 0, 0, 255, 127, 0, 0, 1, 255, 127, 255, 127, 1, 255, 255, 255, 255, 1, 0, 0, 255, 127];
        string color = hasBaseColor ? " color=\"66051\"" : "";
        string textures = texture.Length == 0 ? "" : $"""
            <TextureData2>
                <PerVertexTextureCoord>{Convert.ToBase64String(uv)}</PerVertexTextureCoord>
                <TextureImages><TextureImage>{texture}</TextureImage></TextureImages>
            </TextureData2>
            """;
        string nativeColors = native.Length == 0 ? "" : $"<VertexColorSets><VertexColorSet>{native}</VertexColorSet></VertexColorSets>";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes($"""
            <HPS><Packed_geometry><Schema>CA</Schema><Binary_data><CA>
            <Vertices vertex_count="4">{Convert.ToBase64String(new byte[48])}</Vertices>
            <Facets facet_count="2"{color}>BAA=</Facets>
            </CA></Binary_data></Packed_geometry>{textures}{nativeColors}</HPS>
            """));
        return DcmReader.Read(input);
    }

    private static async Task<Rgb[]> ExportColors(DcmMesh mesh, bool swapRedBlue = true)
    {
        using var sync = new MemoryStream();
        using var asyncOutput = new MemoryStream();
        MeshWriter.WritePly(mesh, sync, swapRedBlue);
        await MeshWriter.WritePlyAsync(mesh, asyncOutput, CancellationToken.None, swapRedBlue);
        Assert.Equal(sync.ToArray(), asyncOutput.ToArray());

        string ply = Encoding.UTF8.GetString(sync.ToArray());
        Assert.Contains("property uchar red\nproperty uchar green\nproperty uchar blue\n", ply);
        int start = ply.IndexOf("end_header\n", StringComparison.Ordinal) + "end_header\n".Length;
        using var rows = new StringReader(ply[start..]);
        var colors = new Rgb[mesh.Vertices.Count];
        for (int i = 0; i < colors.Length; i++)
        {
            var fields = rows.ReadLine()!.Split(' ');
            Assert.Equal(9, fields.Length);
            colors[i] = new Rgb(byte.Parse(fields[6], CultureInfo.InvariantCulture), byte.Parse(fields[7], CultureInfo.InvariantCulture),
                byte.Parse(fields[8], CultureInfo.InvariantCulture));
        }
        return colors;
    }

    [Theory]
    [InlineData(1f, true)]
    [InlineData(1e20f, false)]
    [InlineData(1e-25f, true)]
    public async Task PlyExportWritesAreaWeightedVertexNormals(float scale, bool hasColor)
    {
        Vector3[] vertices =
        [
            Vector3.Zero, new(scale, 0, 0), new(0, 2 * scale, 0),
            new(0, 0, scale), new(0, 4 * scale, 0), new(scale, scale, scale)
        ];
        using var vertexBytes = new MemoryStream();
        using (var writer = new BinaryWriter(vertexBytes, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var vertex in vertices)
            {
                writer.Write(vertex.X);
                writer.Write(vertex.Y);
                writer.Write(vertex.Z);
            }
        }

        // Faces (0,1,2), (0,3,1), and the collinear (0,2,4).
        byte[] facets = [4, 5, 0, 0, 3, 0, 1, 0, 5, 0, 0, 2, 0, 4, 0];
        string color = hasColor ? " color=\"66051\"" : "";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes($"""
            <HPS><Packed_geometry><Schema>CA</Schema><Binary_data><CA>
            <Vertices vertex_count="6">{Convert.ToBase64String(vertexBytes.ToArray())}</Vertices>
            <Facets facet_count="3"{color}>{Convert.ToBase64String(facets)}</Facets>
            </CA></Binary_data></Packed_geometry></HPS>
            """));
        var mesh = DcmReader.Read(input);

        using var sync = new MemoryStream();
        using var asyncOutput = new MemoryStream();
        MeshWriter.WritePly(mesh, sync);
        await MeshWriter.WritePlyAsync(mesh, asyncOutput);
        byte[] output = sync.ToArray();
        Assert.Equal(output, asyncOutput.ToArray());

        using var ply = new StringReader(Encoding.UTF8.GetString(output));
        Assert.Equal("ply", ply.ReadLine());
        Assert.Equal("format ascii 1.0", ply.ReadLine());
        Assert.Equal("element vertex 6", ply.ReadLine());
        foreach (string component in new[] { "x", "y", "z", "nx", "ny", "nz" })
            Assert.Equal($"property float {component}", ply.ReadLine());
        if (hasColor)
        {
            foreach (string channel in new[] { "red", "green", "blue" })
                Assert.Equal($"property uchar {channel}", ply.ReadLine());
        }
        Assert.Equal("element face 3", ply.ReadLine());
        Assert.Equal("property list uchar int vertex_indices", ply.ReadLine());
        Assert.Equal("end_header", ply.ReadLine());

        var sharedNormal = new Vector3(0, 1 / MathF.Sqrt(5), 2 / MathF.Sqrt(5));
        Vector3[] normals = [sharedNormal, sharedNormal, Vector3.UnitZ, Vector3.UnitY, Vector3.Zero, Vector3.Zero];
        for (int i = 0; i < vertices.Length; i++)
        {
            string[] fields = ply.ReadLine()!.Split(' ');
            Assert.Equal(hasColor ? 9 : 6, fields.Length);
            float[] values = fields[..6].Select(field => float.Parse(field, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(vertices[i], new Vector3(values[0], values[1], values[2]));
            Assert.Equal(normals[i].X, values[3], precision: 6);
            Assert.Equal(normals[i].Y, values[4], precision: 6);
            Assert.Equal(normals[i].Z, values[5], precision: 6);
            if (hasColor)
                Assert.Equal(new[] { "1", "2", "3" }, fields[6..]);
        }
        Assert.Equal("3 0 1 2", ply.ReadLine());
        Assert.Equal("3 0 3 1", ply.ReadLine());
        Assert.Equal("3 0 2 4", ply.ReadLine());
        Assert.Null(ply.ReadLine());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedObjPublishRestoresExistingCompanionAndRemovesNewOne(bool useAsync)
    {
        var scan = $"""
            <HPS>
              <Packed_geometry>
                <Schema>CA</Schema>
                <Binary_data><CA>
                  <Vertices vertex_count="3">{Convert.ToBase64String(new byte[36])}</Vertices>
                  <Facets facet_count="1" color="16711680">BA==</Facets>
                </CA></Binary_data>
              </Packed_geometry>
              <TextureImages><TextureImage>AQID</TextureImage></TextureImages>
            </HPS>
            """;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(scan));
        var mesh = DcmReader.Read(input);

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var destination = Path.Combine(directory.FullName, "scan.obj");
            var material = Path.Combine(directory.FullName, "scan.mtl");
            var texture = Path.Combine(directory.FullName, "scan_texture0.jpg");
            Directory.CreateDirectory(destination);
            File.WriteAllText(material, "original material");

            var error = await Record.ExceptionAsync(async () =>
            {
                if (useAsync)
                    await MeshWriter.WriteAsync(mesh, destination);
                else
                    MeshWriter.Write(mesh, destination);
            });
            Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected export result: {error}");

            Assert.True(Directory.Exists(destination));
            Assert.Equal("original material", File.ReadAllText(material));
            Assert.False(File.Exists(texture));
            Assert.Single(directory.EnumerateDirectories());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
