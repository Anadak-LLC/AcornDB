using System;
using System.Runtime.CompilerServices;

namespace AcornDB.Storage.BPlusTree
{
    /// <summary>
    /// CRC32 (IEEE 802.3) utility for page and WAL checksums.
    /// Table-based implementation — no external dependencies.
    /// </summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = GenerateTable();

        private static uint[] GenerateTable()
        {
            const uint polynomial = 0xEDB88320u; // IEEE 802.3
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
                }
                table[i] = crc;
            }
            return table;
        }

        /// <summary>
        /// Compute CRC32 over the given data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint Compute(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>
        /// Compute CRC32 over a page, excluding bytes at [excludeOffset, excludeOffset+excludeLen).
        /// Used for page CRC validation where the CRC field itself must be excluded.
        /// </summary>
        internal static uint ComputeExcluding(ReadOnlySpan<byte> data, int excludeOffset, int excludeLen)
        {
            uint crc = 0xFFFFFFFFu;

            int end1 = excludeOffset;
            for (int i = 0; i < end1; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);

            int start2 = excludeOffset + excludeLen;
            for (int i = start2; i < data.Length; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFFu;
        }
    }
}
