// SPDX-License-Identifier: MIT AND BSL-1.0
// See THIRD-PARTY-NOTICES.md for upstream attribution and license scope.

using System.Numerics;

namespace Open3SDCMNet;

public readonly record struct Triangle(int A, int B, int C);
public readonly record struct Rgb(byte Red, byte Green, byte Blue);

public sealed record TextureCoordinates(string? Id, string? TextureId, string? Key,
    int EncodedByteCount, IReadOnlyList<Vector2?> Corners);

public sealed record TextureImage(string? Id, string? TextureId, string? RefTextureCoordId,
    string? TextureCoordSet, string? Name, string? Version, int Width, int Height,
    int BytesPerPixel, int EncodedByteCount, ReadOnlyMemory<byte> Bytes);

public sealed class DcmMesh
{
    public string Schema { get; }
    public IReadOnlyList<Vector3> Vertices { get; }
    public IReadOnlyList<Triangle> Triangles { get; }
    public Rgb? BaseColor { get; }
    /// <summary>Native RGB values in vertex order, or an empty list if unavailable.</summary>
    public IReadOnlyList<Rgb> VertexColors { get; }
    public IReadOnlyList<TextureCoordinates> CoordinateSets { get; }
    public IReadOnlyList<TextureImage> Images { get; }
    public IReadOnlyList<string> Warnings { get; }

    internal DcmMesh(string schema, Vector3[] vertices, Triangle[] triangles, Rgb? color, Rgb[] vertexColors,
        TextureCoordinates[] sets, TextureImage[] images, List<string> warnings)
    {
        Schema = schema;
        Vertices = Array.AsReadOnly(vertices);
        Triangles = Array.AsReadOnly(triangles);
        BaseColor = color;
        VertexColors = Array.AsReadOnly(vertexColors);
        CoordinateSets = Array.AsReadOnly(sets);
        Images = Array.AsReadOnly(images);
        Warnings = warnings.AsReadOnly();
    }

    public (TextureCoordinates? Coordinates, TextureImage? Image) GetTextureBinding()
    {
        var sets = CoordinateSets.Where(set => set.Corners.Count > 0).ToArray();
        foreach (var image in Images)
        {
            if (image.Bytes.IsEmpty || sets.Length == 0)
                continue;
            var match = sets.FirstOrDefault(s => image.RefTextureCoordId != null && s.Id == image.RefTextureCoordId)
                ?? sets.FirstOrDefault(s => image.TextureId != null && s.TextureId == image.TextureId)
                ?? sets[0];
            return (match, image);
        }
        return (sets.FirstOrDefault(), Images.FirstOrDefault());
    }
}
