using System.Text;
using Xunit;

namespace Open3SDCMNet.Tests;

public class FacetDecodingTests
{
    [Theory]
    [InlineData("CA")]
    [InlineData("CB")]
    [InlineData("CC")]
    public void RestartAndExtendPreserveCornerOrder(string schema)
    {
        using var stream = Scan(schema, [4, 0], 2);
        var mesh = DcmReader.Read(stream);
        Assert.True(stream.CanRead);
        Assert.Equal(new[] { new Triangle(0, 1, 2), new Triangle(3, 1, 0) }, mesh.Triangles);
    }

    [Fact]
    public void WideRestartRetriesWithFreshFrontier()
    {
        // In short mode the remaining zero bytes become extra extend operations.
        using var stream = Scan("CA", [5, 3,0,0,0, 1,0,0,0, 2,0,0,0], 1);
        var mesh = DcmReader.Read(stream);
        Assert.Equal(new Triangle(3, 1, 2), Assert.Single(mesh.Triangles));
    }

    [Fact]
    public void TruncatedIndexDoesNotProducePartialMesh()
    {
        using var stream = Scan("CA", [4, 7, 2], 2);
        Assert.Throws<InvalidDataException>(() => DcmReader.Read(stream));
    }

    [Fact]
    public void GrowingAndRestartingFrontierPreservesTriangleOrder()
    {
        using var stream = Scan("CA", [4, 0, 0, 1, 2, 0, 9, 4, 0], 8, vertexCount: 10);
        var mesh = DcmReader.Read(stream);

        Assert.Equal<Triangle>(
            [new(0, 1, 2), new(3, 1, 0), new(4, 2, 1), new(2, 4, 0),
             new(0, 1, 3), new(5, 4, 1), new(6, 7, 8), new(9, 7, 6)],
            mesh.Triangles);
    }

    [Fact]
    public void MergingAcrossBoundaryAndEmptyingFrontierPreservesTriangleOrder()
    {
        using var stream = Scan("CA", [4, 1, 9, 0, 2, 9, 9, 4, 0], 6, vertexCount: 8);
        var mesh = DcmReader.Read(stream);

        Assert.Equal<Triangle>(
            [new(0, 1, 2), new(0, 2, 1), new(3, 2, 2), new(2, 2, 3), new(4, 5, 6), new(7, 5, 4)],
            mesh.Triangles);
    }

    private static MemoryStream Scan(string schema, byte[] facets, int faceCount, int vertexCount = 4)
    {
        var xml = $"""
            <HPS><Packed_geometry><Schema>{schema}</Schema><Binary_data><{schema}>
            <Vertices vertex_count="{vertexCount}">{Convert.ToBase64String(new byte[12 * vertexCount])}</Vertices>
            <Facets facet_count="{faceCount}">{Convert.ToBase64String(facets)}</Facets>
            </{schema}></Binary_data></Packed_geometry></HPS>
            """;
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }
}
