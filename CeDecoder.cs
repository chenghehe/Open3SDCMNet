// SPDX-License-Identifier: MIT AND BSL-1.0
// See THIRD-PARTY-NOTICES.md for upstream attribution and license scope.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace Open3SDCMNet;

internal sealed class CeDecoder
{
    private const string PackageLockKeyId = "1";
    private readonly byte[] key;

    internal CeDecoder(IReadOnlyDictionary<string, string> properties)
    {
        key = Convert.FromBase64String("YcVXxg16HMEjV0yKbgMRSQ==");
        for (int i = 0; i < key.Length; i++)
            key[i] ^= 0x55;

        if (properties.GetValueOrDefault("EKID", PackageLockKeyId) == PackageLockKeyId &&
            properties.TryGetValue("PackageLockList", out var locks))
        {
            var items = new SortedSet<string>(locks.Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            if (items.Count > 0)
            {
                var canonical = Encoding.UTF8.GetBytes(string.Join(';', items) + ";");
                var hash = Convert.ToHexString(MD5.HashData(canonical));
                key = [.. key, .. Encoding.ASCII.GetBytes(hash)];
            }
        }
    }

    internal byte[] Decode(byte[] data, bool scramble)
    {
        var blockKey = (byte[])key.Clone();
        if (scramble)
        {
            Array.Reverse(blockKey);
            for (int i = 0; i < blockKey.Length; i++)
                blockKey[i] ^= 0x7b;
        }
        var cipher = new BlowfishEngine();
        cipher.Init(false, new KeyParameter(blockKey));
        if (data.Length % 8 != 0)
            Array.Resize(ref data, checked(data.Length + 8 - data.Length % 8));

        for (int i = 0; i < data.Length; i += 8)
        {
            var block = data.AsSpan(i, 8);
            block[..4].Reverse();
            block[4..].Reverse();
            cipher.ProcessBlock(block, block);
            block[..4].Reverse();
            block[4..].Reverse();
        }
        return data;
    }

    internal static uint VertexChecksum(ReadOnlySpan<byte> bytes)
    {
        const uint modulus = 65521;
        // Starting below the modulus, 4096 bytes keep b at most 2,408,052,720.
        const int chunkSize = 4096;
        uint a = 1, b = 0;
        while (!bytes.IsEmpty)
        {
            var chunk = bytes[..Math.Min(bytes.Length, chunkSize)];
            foreach (byte value in chunk)
            {
                a += value;
                b += a;
            }
            a %= modulus;
            b %= modulus;
            bytes = bytes[chunk.Length..];
        }
        return BinaryPrimitives.ReverseEndianness(b << 16 | a);
    }
}
