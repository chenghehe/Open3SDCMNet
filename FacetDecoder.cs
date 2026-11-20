// SPDX-License-Identifier: MIT AND BSL-1.0
// See THIRD-PARTY-NOTICES.md for upstream attribution and license scope.

using System.Buffers.Binary;

namespace Open3SDCMNet;

// The cursor indexes a circular frontier. Merging across its end preserves the
// upstream ordering by replacing the lower index and advancing from there.
internal sealed class FacetDecoder(byte[] bytes, int vertexCount, Triangle[] faces, int shortWidth)
{
    private readonly record struct Edge(int Start, int End);
    private readonly EdgeFrontier edges = new();
    private int current;
    private int offset;
    private int nextVertex;
    private int writtenFaces;

    internal static Triangle[] Decode(byte[] bytes, int vertexCount, int faceCount)
    {
        if (faceCount < 1 || faceCount > bytes.Length)
            throw new InvalidDataException("Facets count is incompatible with its instruction stream.");
        var faces = new Triangle[faceCount];
        foreach (int width in new[] { 2, 4 })
        {
            try
            {
                var decoder = new FacetDecoder(bytes, vertexCount, faces, width);
                return decoder.Run();
            }
            catch (InvalidDataException) when (width == 2)
            {
                // Some scans use 32-bit operands for the nominally short instructions.
            }
        }
        throw new InvalidDataException("Invalid Facets stream.");
    }

    private Triangle[] Run()
    {
        while (offset < bytes.Length)
        {
            int op = bytes[offset++] & 15;
            switch (op)
            {
                case 0: Extend(nextVertex++); break;
                case 1: Merge(previous: true); break;
                case 2: Merge(previous: false); break;
                case 3: Advance(1); break;
                case 4: Restart(nextVertex++, nextVertex++, nextVertex++); break;
                case 5:
                case 6:
                    int width = op == 5 ? shortWidth : 4;
                    int first = ReadIndex(width);
                    int second = ReadIndex(width);
                    int third = ReadIndex(width);
                    Restart(first, second, third);
                    break;
                case 7: Extend(ReadIndex(shortWidth)); break;
                case 8: Extend(ReadIndex(4)); break;
                case 9: Remove(); break;
                case 10: nextVertex++; break;
            }
        }
        if (writtenFaces != faces.Length)
            throw new InvalidDataException($"Facets declares {faces.Length} faces but decoded {writtenFaces}.");
        return faces;
    }

    private int ReadIndex(int width)
    {
        if (bytes.Length - offset < width)
            throw new InvalidDataException("Truncated facet operand.");
        var span = bytes.AsSpan(offset, width);
        offset += width;
        return width == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadInt32LittleEndian(span);
    }

    private void Add(int a, int b, int c)
    {
        if ((uint)a >= vertexCount || (uint)b >= vertexCount || (uint)c >= vertexCount || writtenFaces >= faces.Length)
            throw new InvalidDataException("Facet index or count exceeds the mesh bounds.");
        faces[writtenFaces++] = new(a, b, c);
    }

    private void Restart(int a, int b, int c)
    {
        Add(a, b, c);
        edges.Clear();
        edges.Add(new(a, b));
        edges.Add(new(b, c));
        edges.Add(new(c, a));
        current = 0;
    }

    private void Advance(int count)
    {
        if (edges.Count > 0)
            current = (current + count) % edges.Count;
    }

    private void Extend(int vertex)
    {
        if (edges.Count == 0) return;
        var e = edges[current];
        Add(vertex, e.End, e.Start);
        edges[current] = new(e.Start, vertex);
        edges.Insert(current + 1, new(vertex, e.End));
        Advance(2);
    }

    private int Previous => (current + edges.Count - 1) % edges.Count;

    private void Merge(bool previous)
    {
        if (edges.Count < 2) return;
        int other = previous ? Previous : (current + 1) % edges.Count;
        var e = edges[current];
        var adjacent = edges[other];
        Add(e.Start, previous ? adjacent.Start : adjacent.End, e.End);
        int low = Math.Min(current, other);
        edges[low] = previous ? new(adjacent.Start, e.End) : new(e.Start, adjacent.End);
        edges.RemoveAt(Math.Max(current, other));
        current = (low + 1) % edges.Count;
    }

    private void Remove()
    {
        if (edges.Count == 0) return;
        int previous = Previous;
        var p = edges[previous];
        var e = edges[current];
        if (p.Start == e.End && edges.Count > 2)
        {
            int low = Math.Min(current, previous);
            edges.RemoveAt(Math.Max(current, previous));
            edges.RemoveAt(low);
            current = low % edges.Count;
            edges[Previous] = edges[Previous] with { End = edges[current].Start };
        }
        else
        {
            edges[previous] = p with { End = e.End };
            edges.RemoveAt(current);
            current = edges.Count == 0 ? 0 : current % edges.Count;
        }
    }

    // Keep unused slots near the last edit, where the next frontier edit usually lands.
    // The ranges on either side of the gap still form the original logical edge order.
    private sealed class EdgeFrontier
    {
        private const int InitialCapacity = 4;
        private Edge[] items = new Edge[InitialCapacity];
        private int gapStart;
        private int gapEnd = InitialCapacity;

        internal int Count => items.Length - (gapEnd - gapStart);

        internal Edge this[int index]
        {
            get => items[Position(index)];
            set => items[Position(index)] = value;
        }

        internal void Clear()
        {
            gapStart = 0;
            gapEnd = items.Length;
        }

        internal void Add(Edge edge) => Insert(Count, edge);

        internal void Insert(int index, Edge edge)
        {
            if (gapStart == gapEnd)
                Grow();
            MoveGap(index);
            items[gapStart++] = edge;
        }

        internal void RemoveAt(int index)
        {
            MoveGap(index);
            gapEnd++;
        }

        private int Position(int index) => index < gapStart ? index : index + (gapEnd - gapStart);

        private void Grow()
        {
            int capacity = (int)Math.Min(2L * items.Length, Array.MaxLength);
            if (capacity == items.Length)
                throw new OutOfMemoryException();

            var larger = new Edge[capacity];
            Array.Copy(items, larger, gapStart);
            int tail = items.Length - gapEnd;
            Array.Copy(items, gapEnd, larger, larger.Length - tail, tail);
            items = larger;
            gapEnd = larger.Length - tail;
        }

        private void MoveGap(int index)
        {
            if (index < gapStart)
            {
                int count = gapStart - index;
                Array.Copy(items, index, items, gapEnd - count, count);
                gapStart = index;
                gapEnd -= count;
            }
            else if (index > gapStart)
            {
                int count = index - gapStart;
                Array.Copy(items, gapEnd, items, gapStart, count);
                gapStart = index;
                gapEnd += count;
            }
        }
    }
}
