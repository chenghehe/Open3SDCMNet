using System.Numerics;
using System.Text;
using Xunit;

namespace Open3SDCMNet.Tests;

public class DcmReaderTests
{
    [Fact]
    public void InvalidOptionalUvPayloadReturnsGeometryWithWarning()
    {
        var mesh = ReadScanWithUv("!");

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
        var mesh = ReadScanWithUv(Convert.ToBase64String(uv));

        Assert.Empty(mesh.Warnings);
        Assert.Equal<Vector2?>([Vector2.Zero, Vector2.One, null, Vector2.UnitY, Vector2.One, Vector2.UnitX],
            Assert.Single(mesh.CoordinateSets).Corners);
    }

    private static DcmMesh ReadScanWithUv(string uv)
    {
        var scan = $"""
            <HPS>
              <Packed_geometry>
                <Schema>CA</Schema>
                <Binary_data><CA>
                  <Vertices vertex_count="4">{Convert.ToBase64String(new byte[48])}</Vertices>
                  <Facets facet_count="2">BAA=</Facets>
                </CA></Binary_data>
              </Packed_geometry>
              <TextureData2>
                <PerVertexTextureCoord>{uv}</PerVertexTextureCoord>
              </TextureData2>
            </HPS>
            """;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(scan));

        return DcmReader.Read(input);
    }
}
