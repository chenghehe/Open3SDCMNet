// SPDX-License-Identifier: MIT AND BSL-1.0
// See THIRD-PARTY-NOTICES.md for upstream attribution and license scope.

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;

namespace Open3SDCMNet;

public static class DcmReader
{
    private const string InvalidDocumentMessage = "Expected a 3Shape HPS XML document.";
    private const string CeSchema = "CE";
    private const string BinaryDataElement = "Binary_data";
    private const string VertexCountAttribute = "vertex_count";
    private const string TextureIdAttribute = "TextureId";
    private const string EncodedByteCountAttribute = "Base64EncodedBytes";

    public static DcmMesh Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads one HPS scan from the stream's current position and leaves the stream open.</summary>
    public static DcmMesh Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var reader = CreateReader(input, useAsync: false);
        XElement root;
        try { root = XElement.Load(reader); }
        catch (XmlException e) { throw new InvalidDataException(InvalidDocumentMessage, e); }
        return Decode(root);
    }

    /// <summary>Loads an HPS scan with asynchronous file reads.</summary>
    public static async Task<DcmMesh> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads an HPS scan from the stream's current position and leaves it open.</summary>
    public static async Task<DcmMesh> ReadAsync(Stream input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = CreateReader(input, useAsync: true);
        XElement root;
        try
        {
            root = await XElement.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }
        catch (XmlException e)
        {
            throw new InvalidDataException(InvalidDocumentMessage, e);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var mesh = Decode(root);
        cancellationToken.ThrowIfCancellationRequested();
        return mesh;
    }

    private static XmlReader CreateReader(Stream input, bool useAsync) =>
        XmlReader.Create(input, new XmlReaderSettings
        {
            Async = useAsync,
            DtdProcessing = DtdProcessing.Prohibit
        });

    private static DcmMesh Decode(XElement root)
    {
        if (root.Name != "HPS")
            throw new InvalidDataException("Expected an HPS root element.");
        var geometry = root.Element("Packed_geometry") ?? root;
        string schema = geometry.Element("Schema")?.Value.Trim() ?? "";
        if (schema is not ("CA" or "CB" or "CC" or CeSchema))
            throw new InvalidDataException($"Unsupported HPS schema '{schema}'.");
        var binary = geometry.Element(BinaryDataElement)?.Element(schema)
            ?? throw new InvalidDataException($"Missing {BinaryDataElement}/{schema}.");
        var properties = new Dictionary<string, string>();
        foreach (var property in root.Descendants("Property"))
            if ((string?)property.Attribute("name") is { Length: > 0 } name)
                properties[name] = (string?)property.Attribute("value") ?? "";

        var ce = schema == CeSchema ? new CeDecoder(properties) : null;
        var warnings = new List<string>();
        var vertexNode = Required(binary, "Vertices");
        var bytes = DecodeBytes(vertexNode);
        if (ce != null) bytes = ce.Decode(bytes, false);
        int vertexCount = Count(vertexNode, VertexCountAttribute);
        if (vertexCount <= 0 || vertexCount > bytes.Length / 12)
            throw new InvalidDataException($"Vertices buffer length does not match {VertexCountAttribute}.");
        if (ce != null && uint.TryParse((string?)vertexNode.Attribute("check_value"), out uint checksum)
            && checksum != CeDecoder.VertexChecksum(bytes.AsSpan(0, vertexCount * 12)))
            warnings.Add("CE vertex checksum mismatch.");

        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            var v = bytes.AsSpan(i * 12, 12);
            vertices[i] = new(BinaryPrimitives.ReadSingleLittleEndian(v),
                BinaryPrimitives.ReadSingleLittleEndian(v[4..]), BinaryPrimitives.ReadSingleLittleEndian(v[8..]));
        }
        var facets = Required(binary, "Facets");
        var triangles = FacetDecoder.Decode(DecodeBytes(facets), vertexCount, Count(facets, "facet_count"));
        Rgb? color = uint.TryParse((string?)facets.Attribute("color"), out uint rgb)
            ? new Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : null;

        var textureData = root.Element("TextureData2");
        var images = (textureData ?? root).Element("TextureImages")?.Elements("TextureImage")
            .Select(ReadImage).ToArray() ?? [];
        var sets = new List<TextureCoordinates>();
        foreach (var node in textureData?.Elements("PerVertexTextureCoord") ?? [])
        {
            string? key = Attribute(node, "Key");
            int length = Size(node, EncodedByteCountAttribute);
            Vector2?[] corners;
            try
            {
                var data = DecodeBytes(node);
                if (ce != null) data = ce.Decode(data, key != null);
                var buffer = length > 0 && length < data.Length ? data.AsSpan(0, length) : data;
                corners = UvDecoder.Decode(buffer, vertexCount, triangles);
            }
            catch (InvalidDataException ex)
            {
                warnings.Add(ex.Message);
                corners = [];
            }
            sets.Add(new(Attribute(node, "TextureCoordId"), Attribute(node, TextureIdAttribute), key,
                length, Array.AsReadOnly(corners)));
        }
        return new DcmMesh(schema, vertices, triangles, color, [.. sets], images, warnings);
    }

    private static TextureImage ReadImage(XElement node) => new(
        Attribute(node, "Id"), Attribute(node, TextureIdAttribute), Attribute(node, "RefTextureCoordId"),
        Attribute(node, "TextureCoordSet"), Attribute(node, "TextureName"), Attribute(node, "Version"),
        Size(node, "Width"), Size(node, "Height"), Size(node, "BytesPerPixel"),
        Size(node, EncodedByteCountAttribute), DecodeBytes(node));

    private static XElement Required(XElement parent, string name) =>
        parent.Element(name) ?? throw new InvalidDataException($"Missing {name} in {parent.Name}.");

    private static string? Attribute(XElement node, string name) =>
        (string?)node.Attribute(name) is { Length: > 0 } value ? value : null;

    private static int Size(XElement node, string name) =>
        int.TryParse(Attribute(node, name), NumberStyles.None, CultureInfo.InvariantCulture, out int size) ? size : 0;

    private static int Count(XElement node, string name)
    {
        int size = Size(node, name);
        if (size <= 0) throw new InvalidDataException($"Missing or invalid {name} in {node.Name}.");
        return size;
    }

    private static byte[] DecodeBytes(XElement node)
    {
        try { return Convert.FromBase64String(node.Value); }
        catch (FormatException e) { throw new InvalidDataException($"Invalid base64 in {node.Name}.", e); }
    }
}
