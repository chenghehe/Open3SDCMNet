using StbImageSharp;

namespace Open3SDCMNet;

internal static class PlyColorBaker
{
    private static readonly Rgb DefaultColor = new(179, 179, 179);

    internal static IReadOnlyList<Rgb> GetVertexColors(DcmMesh mesh, bool swapTextureRedBlue,
        CancellationToken cancellationToken = default)
    {
        var nativeColors = mesh.VertexColors;
        if (mesh.Images.Count == 0 || mesh.CoordinateSets.Count == 0)
            return nativeColors;

        var (coordinates, texture) = mesh.GetTextureBinding();
        if (coordinates == null || texture == null || texture.Bytes.IsEmpty ||
            coordinates.Corners.Count != 3L * mesh.Triangles.Count)
            return nativeColors;

        ImageResult image;
        try { image = ImageResult.FromMemory(texture.Bytes.ToArray(), ColorComponents.RedGreenBlue); }
        catch (InvalidOperationException) { return nativeColors; }
        cancellationToken.ThrowIfCancellationRequested();

        var sums = new (long Red, long Green, long Blue, int Count)[mesh.Vertices.Count];
        int redOffset = swapTextureRedBlue ? 2 : 0;
        int blueOffset = 2 - redOffset;
        var corners = coordinates.Corners;
        int corner = 0;
        bool hasSamples = false;
        foreach (var face in mesh.Triangles)
        {
            if (corner % (3 * 4096) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            Sample(face.A, corner++);
            Sample(face.B, corner++);
            Sample(face.C, corner++);
        }
        if (!hasSamples)
            return nativeColors;

        var fallback = mesh.BaseColor ?? DefaultColor;
        var colors = new Rgb[sums.Length];
        for (int i = 0; i < sums.Length; i++)
        {
            if (i % 4096 == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var (red, green, blue, count) = sums[i];
            var defaultColor = nativeColors.Count == 0 ? fallback : nativeColors[i];
            colors[i] = count == 0 ? defaultColor
                : new Rgb(Average(red, count), Average(green, count), Average(blue, count));
        }
        return colors;

        void Sample(int vertex, int cornerIndex)
        {
            if (corners[cornerIndex] is not { } uv || !float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
                return;

            // DCM UVs address the decoded texture from the top row.
            int x = Pixel(uv.X, image.Width);
            int y = Pixel(uv.Y, image.Height);
            int offset = (y * image.Width + x) * 3;
            ref var sum = ref sums[vertex];
            sum.Red += image.Data[offset + redOffset];
            sum.Green += image.Data[offset + 1];
            sum.Blue += image.Data[offset + blueOffset];
            sum.Count++;
            hasSamples = true;
        }
    }

    private static int Pixel(float uv, int size) => (int)Math.Round(Math.Clamp((double)uv, 0, 1) * (size - 1));

    private static byte Average(long sum, int count) => (byte)Math.Round((double)sum / count);
}
