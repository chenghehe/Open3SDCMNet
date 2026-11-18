// SPDX-License-Identifier: MIT AND BSL-1.0
// See THIRD-PARTY-NOTICES.md for upstream attribution and license scope.

using System.Buffers.Binary;
using System.Numerics;

namespace Open3SDCMNet;

internal static class UvDecoder
{
    internal static Vector2?[] Decode(ReadOnlySpan<byte> bytes, int vertexCount, Triangle[] faces)
    {
        int cornerCount = checked(faces.Length * 3);
        var offsets = new int[vertexCount];
        foreach (var face in faces)
        {
            offsets[face.A]++;
            offsets[face.B]++;
            offsets[face.C]++;
        }
        int start = 0;
        for (int i = 0; i < offsets.Length; i++)
        {
            int degree = offsets[i];
            offsets[i] = start;
            start += degree;
        }
        // Group corners by vertex without allocating a list for every vertex.
        // Insertion order matches the order of each vertex's UV data.
        var corners = new int[cornerCount];
        int next = 0;
        foreach (var face in faces)
        {
            corners[offsets[face.A]++] = next++;
            corners[offsets[face.B]++] = next++;
            corners[offsets[face.C]++] = next++;
        }

        var result = new Vector2?[cornerCount];
        start = 0;
        foreach (int end in offsets)
        {
            var indices = corners.AsSpan(start, end - start);
            start = end;
            if (bytes.IsEmpty)
                throw new InvalidDataException("Truncated UV stream.");
            int count = bytes[0];
            bytes = bytes[1..];
            if ((count != 1 && count != indices.Length) || bytes.Length < count * 4)
                throw new InvalidDataException("Invalid UV count or truncated coordinates.");

            for (int i = 0; i < count; i++)
            {
                uint packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                bytes = bytes[4..];
                Vector2? uv = packed == uint.MaxValue ? null :
                    new Vector2(Component((ushort)packed), Component((ushort)(packed >> 16)));
                if (count == 1)
                    foreach (int corner in indices) result[corner] = uv;
                else
                    result[indices[i]] = uv;
            }
        }
        return result;
    }

    private static float Component(ushort bits) => (bits & 0x8000) == 0
        ? bits / 32767f : (bits & 0x7fff) * (512f / 32767f) - 256f;
}
