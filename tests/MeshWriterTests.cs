using System.Text;
using Xunit;

namespace Open3SDCMNet.Tests;

public class MeshWriterTests
{
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
