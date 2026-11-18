using System.Text;
using Xunit;

namespace Open3SDCMNet.Tests;

public class AsyncIoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncStreamConversionUsesAsyncIoAndLeavesStreamsOpen(bool binary)
    {
        using var input = new AsyncOnlyStream(Scan());
        var mesh = await DcmReader.ReadAsync(input);
        Assert.Single(mesh.Triangles);
        Assert.True(input.CanRead);

        using var expected = new MemoryStream();
        using var actual = new AsyncOnlyStream(new MemoryStream());
        if (binary)
        {
            MeshWriter.WriteStl(mesh, expected);
            await MeshWriter.WriteStlAsync(mesh, actual);
        }
        else
        {
            MeshWriter.WritePly(mesh, expected);
            await MeshWriter.WritePlyAsync(mesh, actual);
        }

        Assert.True(actual.CanWrite);
        Assert.Equal(expected.ToArray(), actual.ToArray());
    }

    [Fact]
    public async Task CancelledPathWritePreservesExistingOutput()
    {
        using var input = Scan();
        var mesh = DcmReader.Read(input);
        var root = Directory.CreateTempSubdirectory();
        try
        {
            string output = Path.Combine(root.FullName, "mesh.ply");
            File.WriteAllText(output, "existing output");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                MeshWriter.WriteAsync(mesh, output, new CancellationToken(canceled: true)));

            Assert.Equal("existing output", File.ReadAllText(output));
            Assert.Empty(root.EnumerateDirectories());
        }
        finally { root.Delete(recursive: true); }
    }

    private static MemoryStream Scan() => new(Encoding.UTF8.GetBytes($"""
        <HPS><Packed_geometry><Schema>CA</Schema><Binary_data><CA>
          <Vertices vertex_count="3">{Convert.ToBase64String(new byte[36])}</Vertices>
          <Facets facet_count="1" color="16711680">BA==</Facets>
        </CA></Binary_data></Packed_geometry></HPS>
        """));

    private sealed class AsyncOnlyStream(MemoryStream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => inner.ToArray();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
