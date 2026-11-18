// SPDX-License-Identifier: MIT AND BSL-1.0
// See THIRD-PARTY-NOTICES.md for upstream attribution and license scope.

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using static System.FormattableString;

namespace Open3SDCMNet;

public static class MeshWriter
{
    private const int StlFacetSize = 50;
    private const int PlyRowCapacity = 128;
    private const int TextChunkSize = 32 * 1024;
    private const string StlExtension = ".stl";
    private const string StlbExtension = ".stlb";
    private const string PlyExtension = ".ply";
    private const string ObjExtension = ".obj";
    private const string ObjMaterialName = "material0";
    private static readonly object ObjPublicationLock = new();

    /// <summary>Exports by file extension (.stl, .stlb, .ply or .obj). OBJ companions are written alongside the mesh.</summary>
    public static void Write(DcmMesh mesh, string path)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        using var staging = new StagedExport(path);
        using (var output = File.Create(staging.Target))
        {
            switch (staging.Extension)
            {
                case ObjExtension: WriteObj(mesh, output, staging.Target); break;
                case PlyExtension: WritePly(mesh, output); break;
                default: WriteStl(mesh, output); break;
            }
        }
        staging.Publish();
    }

    private sealed class StagedExport : IDisposable
    {
        private readonly string destination;
        private readonly DirectoryInfo directory;
        private bool keepStaging;

        internal string Extension { get; }
        internal string Target { get; }

        internal StagedExport(string path)
        {
            destination = Path.GetFullPath(path);
            Extension = Path.GetExtension(destination).ToLowerInvariant();
            if (Extension is not (StlExtension or StlbExtension or PlyExtension or ObjExtension))
                throw new ArgumentException($"Use {StlExtension}, {StlbExtension}, {PlyExtension} or {ObjExtension}.", nameof(path));

            string stagePath = Path.Combine(Path.GetDirectoryName(destination)!, ".open3sdcm-" + Guid.NewGuid().ToString("N"));
            Target = Path.Combine(stagePath, Path.GetFileName(destination));
            directory = Directory.CreateDirectory(stagePath);
        }

        internal void Publish(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Extension == ObjExtension)
                PublishObj(directory, Target, destination, ref keepStaging, cancellationToken);
            else
                File.Move(Target, destination, overwrite: true);
        }

        public void Dispose()
        {
            if (!keepStaging)
                directory.Delete(recursive: true);
        }
    }

    private static void PublishObj(DirectoryInfo staging, string source, string destination, ref bool keepStaging,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(destination)!;
        lock (ObjPublicationLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var published = new List<(string Path, string? Backup)>();
            try
            {
                foreach (var file in staging.GetFiles())
                {
                    if (file.FullName == source)
                        continue;

                    string path = Path.Combine(directory, file.Name);
                    string? backup = null;
                    if (File.Exists(path))
                    {
                        var backups = staging.CreateSubdirectory("backups");
                        backup = Path.Combine(backups.FullName, file.Name);
                        File.Copy(path, backup);
                    }
                    File.Move(file.FullName, path, overwrite: true);
                    published.Add((path, backup));
                }
                // A newly visible OBJ can resolve all of its companions.
                File.Move(source, destination, overwrite: true);
            }
            catch (Exception publishError)
            {
                var errors = new List<Exception> { publishError };
                for (int i = published.Count - 1; i >= 0; i--)
                {
                    var (path, backup) = published[i];
                    try
                    {
                        if (backup == null)
                            File.Delete(path);
                        else
                            File.Move(backup, path, overwrite: true);
                    }
                    catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                    {
                        errors.Add(rollbackError);
                    }
                }
                if (errors.Count == 1)
                    throw;

                keepStaging = true;
                throw new IOException($"OBJ export and rollback failed. Backups are in '{staging.FullName}'.",
                    new AggregateException(errors));
            }
        }
    }

    /// <summary>Writes a binary STL and leaves the destination stream open.</summary>
    public static void WriteStl(DcmMesh mesh, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(destination);

        Span<byte> buffer = stackalloc byte[80 * StlFacetSize];
        var header = buffer[..84];
        WriteStlHeader(mesh, header);
        destination.Write(header);

        int used = 0;
        foreach (var face in mesh.Triangles)
        {
            WriteStlFacet(mesh, face, buffer.Slice(used, StlFacetSize));
            used += StlFacetSize;
            if (used == buffer.Length)
            {
                destination.Write(buffer);
                used = 0;
            }
        }
        if (used > 0)
            destination.Write(buffer[..used]);
        destination.Flush();
    }

    private static void WriteStlHeader(DcmMesh mesh, Span<byte> header)
    {
        header.Clear();
        "Open3SDCMNet binary STL"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], (uint)mesh.Triangles.Count);
    }

    private static void WriteStlFacet(DcmMesh mesh, Triangle face, Span<byte> facet)
    {
        var a = mesh.Vertices[face.A];
        var b = mesh.Vertices[face.B];
        var c = mesh.Vertices[face.C];
        var normal = Vector3.Cross(b - a, c - a);
        float length = normal.Length();
        WriteVector(facet, length > 0 && float.IsFinite(length) ? normal / length : Vector3.Zero);
        WriteVector(facet[12..], a);
        WriteVector(facet[24..], b);
        WriteVector(facet[36..], c);
        BinaryPrimitives.WriteUInt16LittleEndian(facet[48..], 0);
    }

    private static void WriteVector(Span<byte> destination, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, v.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], v.Z);
    }

    private static StreamWriter Text(Stream output) =>
        new(output, new UTF8Encoding(false), 65536, leaveOpen: true) { NewLine = "\n" };

    private static int FormatPlyRow(Span<char> buffer, IFormatProvider culture,
        [InterpolatedStringHandlerArgument(nameof(buffer), nameof(culture))]
        scoped ref MemoryExtensions.TryWriteInterpolatedStringHandler row)
    {
        if (!buffer.TryWrite(culture, ref row, out int length))
            throw new InvalidOperationException("PLY row exceeded the numeric formatting buffer.");
        return length;
    }

    private static int FormatPlyVertex(Span<char> buffer, IFormatProvider culture, Vector3 vertex, string tint) =>
        FormatPlyRow(buffer, culture, $"{vertex.X:R} {vertex.Y:R} {vertex.Z:R}{tint}");

    private static int FormatPlyFace(Span<char> buffer, IFormatProvider culture, Triangle face) =>
        FormatPlyRow(buffer, culture, $"3 {face.A} {face.B} {face.C}");

    /// <summary>Writes an ASCII PLY and leaves the destination stream open.</summary>
    public static void WritePly(DcmMesh mesh, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        using var writer = Text(destination);
        var (header, tint) = GetPlyHeader(mesh);
        writer.Write(header);
        // Reuse one buffer for round-trip float coordinates, RGB, and face indices.
        Span<char> row = stackalloc char[PlyRowCapacity];
        var culture = CultureInfo.InvariantCulture;
        foreach (var v in mesh.Vertices)
            writer.WriteLine(row[..FormatPlyVertex(row, culture, v, tint)]);
        foreach (var f in mesh.Triangles)
            writer.WriteLine(row[..FormatPlyFace(row, culture, f)]);
    }

    private static (string Header, string Tint) GetPlyHeader(DcmMesh mesh)
    {
        string colorProperties = mesh.BaseColor.HasValue ? "property uchar red\nproperty uchar green\nproperty uchar blue\n" : "";
        string tint = mesh.BaseColor is { } color ? Invariant($" {color.Red} {color.Green} {color.Blue}") : "";
        return (Invariant($"ply\nformat ascii 1.0\nelement vertex {mesh.Vertices.Count}\nproperty float x\nproperty float y\nproperty float z\n{colorProperties}element face {mesh.Triangles.Count}\nproperty list uchar int vertex_indices\nend_header\n"), tint);
    }

    private static void WriteObj(DcmMesh mesh, Stream output, string path)
    {
        var (set, image, materialPath, texturePath) = GetObjLayout(mesh, path);
        using var writer = Text(output);
        if (materialPath != null)
        {
            using var materialStream = File.Create(materialPath);
            using var material = Text(materialStream);
            material.Write(GetObjMaterial(mesh.BaseColor, texturePath));
            if (texturePath != null)
            {
                using var texture = File.Create(texturePath);
                texture.Write(image!.Bytes.Span);
            }
        }
        foreach (string line in GetObjLines(mesh, set, materialPath))
            writer.WriteLine(line);
    }

    private static (TextureCoordinates? Coordinates, TextureImage? Image, string? MaterialPath, string? TexturePath)
        GetObjLayout(DcmMesh mesh, string path)
    {
        var (coordinates, image) = mesh.GetTextureBinding();
        string? texturePath = image is { Bytes.IsEmpty: false }
            ? Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_texture0.jpg")
            : null;
        string? materialPath = mesh.BaseColor.HasValue || texturePath != null ? Path.ChangeExtension(path, ".mtl") : null;
        return (coordinates, image, materialPath, texturePath);
    }

    private static string GetObjMaterial(Rgb? color, string? texturePath)
    {
        string tint = color is { } c
            ? Invariant($"Kd {c.Red / 255f:R} {c.Green / 255f:R} {c.Blue / 255f:R}") : "Kd 0.8 0.8 0.8";
        string mapping = texturePath == null ? "" : $"map_Kd {Path.GetFileName(texturePath)}\n";
        return $"newmtl {ObjMaterialName}\n{tint}\nillum 2\n{mapping}";
    }

    private static IEnumerable<string> GetObjLines(DcmMesh mesh, TextureCoordinates? set, string? materialPath)
    {
        if (materialPath != null) yield return $"mtllib {Path.GetFileName(materialPath)}";
        yield return "o mesh";
        if (materialPath != null) yield return $"usemtl {ObjMaterialName}";
        foreach (var v in mesh.Vertices)
            yield return Invariant($"v {v.X:R} {v.Y:R} {v.Z:R}");
        if (set != null)
            foreach (var uv in set.Corners)
                yield return uv is { } p ? Invariant($"vt {p.X:R} {1f - p.Y:R}") : "vt 0 0";
        int corner = 1;
        foreach (var f in mesh.Triangles)
        {
            yield return set == null
                ? Invariant($"f {f.A + 1} {f.B + 1} {f.C + 1}")
                : Invariant($"f {f.A + 1}/{corner} {f.B + 1}/{corner + 1} {f.C + 1}/{corner + 2}");
            corner += 3;
        }
    }

    /// <summary>Exports with asynchronous writes and publishes the completed files.</summary>
    public static async Task WriteAsync(DcmMesh mesh, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        cancellationToken.ThrowIfCancellationRequested();
        using var staging = new StagedExport(path);
        await using (var output = new FileStream(staging.Target, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            switch (staging.Extension)
            {
                case ObjExtension:
                    await WriteObjAsync(mesh, output, staging.Target, cancellationToken).ConfigureAwait(false);
                    break;
                case PlyExtension:
                    await WritePlyAsync(mesh, output, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await WriteStlAsync(mesh, output, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        staging.Publish(cancellationToken);
    }

    /// <summary>Writes a binary STL asynchronously and leaves the destination stream open.</summary>
    public static async Task WriteStlAsync(DcmMesh mesh, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        const int facetsPerChunk = 1280;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(facetsPerChunk * StlFacetSize);
        try
        {
            WriteStlHeader(mesh, buffer.AsSpan(0, 84));
            await destination.WriteAsync(buffer.AsMemory(0, 84), cancellationToken).ConfigureAwait(false);
            int index = 0;
            while (index < mesh.Triangles.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(facetsPerChunk, mesh.Triangles.Count - index);
                for (int i = 0; i < count; i++)
                    WriteStlFacet(mesh, mesh.Triangles[index + i], buffer.AsSpan(i * StlFacetSize, StlFacetSize));
                await destination.WriteAsync(buffer.AsMemory(0, count * StlFacetSize), cancellationToken).ConfigureAwait(false);
                index += count;
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    /// <summary>Writes an ASCII PLY asynchronously and leaves the destination stream open.</summary>
    public static async Task WritePlyAsync(DcmMesh mesh, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        await using var writer = Text(destination);
        var (header, tint) = GetPlyHeader(mesh);
        await writer.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);

        char[] buffer = ArrayPool<char>.Shared.Rent(TextChunkSize);
        try
        {
            int vertex = 0, face = 0;
            while (vertex < mesh.Vertices.Count || face < mesh.Triangles.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = FillPlyChunk(mesh, tint, ref vertex, ref face, buffer.AsSpan(0, TextChunkSize));
                await writer.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            }
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { ArrayPool<char>.Shared.Return(buffer, clearArray: true); }
    }

    private static int FillPlyChunk(DcmMesh mesh, string tint, ref int vertex, ref int face, Span<char> chunk)
    {
        // Invariant round-trip float32 values, RGB, and indices fit in 127 chars plus LF.
        var culture = CultureInfo.InvariantCulture;
        int used = 0;
        while (vertex < mesh.Vertices.Count && chunk.Length - used >= PlyRowCapacity)
        {
            var v = mesh.Vertices[vertex++];
            used += FormatPlyVertex(chunk.Slice(used, PlyRowCapacity - 1), culture, v, tint);
            chunk[used++] = '\n';
        }
        while (vertex == mesh.Vertices.Count && face < mesh.Triangles.Count && chunk.Length - used >= PlyRowCapacity)
        {
            var f = mesh.Triangles[face++];
            used += FormatPlyFace(chunk.Slice(used, PlyRowCapacity - 1), culture, f);
            chunk[used++] = '\n';
        }
        return used;
    }

    private static async Task WriteObjAsync(DcmMesh mesh, Stream output, string path, CancellationToken cancellationToken)
    {
        var (set, image, materialPath, texturePath) = GetObjLayout(mesh, path);
        if (materialPath != null)
            await File.WriteAllTextAsync(materialPath, GetObjMaterial(mesh.BaseColor, texturePath), cancellationToken).ConfigureAwait(false);
        if (texturePath != null)
            await File.WriteAllBytesAsync(texturePath, image!.Bytes, cancellationToken).ConfigureAwait(false);

        await using var writer = Text(output);
        var chunk = new StringBuilder(TextChunkSize);
        foreach (string line in GetObjLines(mesh, set, materialPath))
        {
            chunk.Append(line).Append('\n');
            if (chunk.Length < TextChunkSize)
                continue;
            await writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            chunk.Clear();
        }
        if (chunk.Length > 0)
            await writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
