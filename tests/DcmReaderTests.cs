using System.Numerics;
using System.Text;
using Xunit;

namespace Open3SDCMNet.Tests;

public class DcmReaderTests
{
    [Fact]
    public void ReadsNativeBgraVertexColorsInVertexOrder()
    {
        byte[] bgra = [30, 20, 10, 255, 60, 50, 40, 0, 90, 80, 70, 128, 120, 110, 100, 255];
        var mesh = ReadScan(vertexColors: $"<VertexColorSets><VertexColorSet Base64EncodedBytes=\"16\">{Convert.ToBase64String(bgra)}</VertexColorSet></VertexColorSets>");

        Assert.Equal<Rgb>([new(10, 20, 30), new(40, 50, 60), new(70, 80, 90), new(100, 110, 120)], mesh.VertexColors);
        Assert.Empty(mesh.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidOptionalVertexColorsReturnGeometryWithWarning(bool invalidBase64)
    {
        string data = invalidBase64 ? "!" : "AQIDBA==";
        var mesh = ReadScan(vertexColors: $"<VertexColorSets><VertexColorSet>{data}</VertexColorSet></VertexColorSets>");

        Assert.Equal(new[] { new Triangle(0, 1, 2), new Triangle(3, 1, 0) }, mesh.Triangles);
        Assert.Empty(mesh.VertexColors);
        Assert.Contains("VertexColorSet", Assert.Single(mesh.Warnings));
    }

    [Fact]
    public void InvalidOptionalUvPayloadReturnsGeometryWithWarning()
    {
        var mesh = ReadScan("!");

        Assert.Equal(new[] { new Triangle(0, 1, 2), new Triangle(3, 1, 0) }, mesh.Triangles);
        Assert.Empty(Assert.Single(mesh.CoordinateSets).Corners);
        Assert.Contains("PerVertexTextureCoord", Assert.Single(mesh.Warnings));
    }

    [Fact]
    public void UvCoordinatesFollowFaceCornerOrderAcrossSharedVertices()
    {
        byte[] uv =
        [
            2, 0, 0, 0, 0, 255, 127, 0, 0,
            1, 255, 127, 255, 127,
            1, 255, 255, 255, 255,
            1, 0, 0, 255, 127
        ];
        var mesh = ReadScan(Convert.ToBase64String(uv));

        Assert.Empty(mesh.Warnings);
        Assert.Equal<Vector2?>([Vector2.Zero, Vector2.One, null, Vector2.UnitY, Vector2.One, Vector2.UnitX],
            Assert.Single(mesh.CoordinateSets).Corners);
    }

    private static DcmMesh ReadScan(string uv = "", string vertexColors = "")
    {
        string texture = uv.Length > 0 ? $"<TextureData2><PerVertexTextureCoord>{uv}</PerVertexTextureCoord></TextureData2>" : "";
        var scan = $"""
            <HPS>
              <Packed_geometry>
                <Schema>CA</Schema>
                <Binary_data><CA>
                  <Vertices vertex_count="4">{Convert.ToBase64String(new byte[48])}</Vertices>
                  <Facets facet_count="2">BAA=</Facets>
                </CA></Binary_data>
              </Packed_geometry>
              {vertexColors}
              {texture}
            </HPS>
            """;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(scan));

        return DcmReader.Read(input);
    }
}
