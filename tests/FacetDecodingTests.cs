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

    private static MemoryStream Scan(string schema, byte[] facets, int faceCount)
    {
        var xml = $"""
            <HPS><Packed_geometry><Schema>{schema}</Schema><Binary_data><{schema}>
            <Vertices vertex_count="4">{Convert.ToBase64String(new byte[48])}</Vertices>
            <Facets facet_count="{faceCount}">{Convert.ToBase64String(facets)}</Facets>
            </{schema}></Binary_data></Packed_geometry></HPS>
            """;
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }
}
