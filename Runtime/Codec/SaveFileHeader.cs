using System;
using System.IO;

namespace UGM.SaveSystem
{
    /// <summary>
    /// On-disk envelope around a codec's encoded body. Layout:
    /// <code>
    /// offset  size  field
    /// 0       4     magic       = "UGMS"
    /// 4       2     formatVer   = 1
    /// 6       2     codecId     (matches ISaveCodec.CodecId)
    /// 8       4     bodyLength
    /// 12      N     body        (bytes returned by ICodec.EncodeEnvelope)
    /// 12+N    4     crc32       over [4 .. 12+N)  (i.e. everything after magic)
    /// </code>
    ///
    /// Why a header at all?
    /// • <b>Detect corruption</b> via CRC32 — torn writes / disk errors don't
    ///   silently ship garbage to the caller.
    /// • <b>Auto-detect codec</b> — a file written with MessagePack still loads
    ///   correctly after the user switches the runtime default to Json. The
    ///   SaveManager reads the codecId byte and dispatches.
    /// • <b>Future migration</b> — formatVer bump signals a non-back-compat
    ///   change at the envelope level (separate from codec evolution).
    /// </summary>
    public static class SaveFileHeader
    {
        public const int MagicLength    = 4;
        public const int HeaderLength   = 12;       // magic + ver + codec + length
        public const int TrailerLength  = 4;        // crc32
        public const ushort CurrentFormatVersion = 1;

        // "UGMS" — UGM Save. 4 ASCII bytes; cheap to compare.
        private static readonly byte[] Magic = { (byte)'U', (byte)'G', (byte)'M', (byte)'S' };

        public struct ParsedHeader
        {
            public ushort FormatVersion;
            public ushort CodecId;
            public int BodyOffset;     // byte index where body begins inside the source array
            public int BodyLength;     // number of body bytes
        }

        /// <summary>
        /// Wrap <paramref name="body"/> with a header + trailing CRC and return
        /// the full file bytes. <paramref name="codecId"/> must match the
        /// codec that produced the body so loaders can route correctly.
        /// </summary>
        public static byte[] Wrap(byte[] body, ushort codecId)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            var total = HeaderLength + body.Length + TrailerLength;
            var buffer = new byte[total];

            // 0..4 magic
            Buffer.BlockCopy(Magic, 0, buffer, 0, MagicLength);

            // 4..6 format version
            WriteUInt16LE(buffer, 4, CurrentFormatVersion);

            // 6..8 codec id
            WriteUInt16LE(buffer, 6, codecId);

            // 8..12 body length
            WriteInt32LE(buffer, 8, body.Length);

            // 12..(12+N) body
            Buffer.BlockCopy(body, 0, buffer, HeaderLength, body.Length);

            // CRC32 over [4 .. 12+N) — everything that's metadata + body, but
            // not the magic itself (a torn write that didn't even land the
            // magic is detected by magic mismatch, not by checksum).
            uint crc = Crc32.Compute(buffer, MagicLength, HeaderLength - MagicLength + body.Length);
            WriteUInt32LE(buffer, HeaderLength + body.Length, crc);

            return buffer;
        }

        /// <summary>
        /// Validate the magic / format / CRC and return where the body lives
        /// inside <paramref name="fileBytes"/>. Throws on any mismatch with a
        /// message that names the specific failure (helps user debugging).
        /// </summary>
        public static ParsedHeader Unwrap(byte[] fileBytes)
        {
            if (fileBytes == null) throw new ArgumentNullException(nameof(fileBytes));
            if (fileBytes.Length < HeaderLength + TrailerLength)
                throw new InvalidDataException(
                    $"Save file too small: {fileBytes.Length} bytes < minimum header+trailer ({HeaderLength + TrailerLength}).");

            for (int i = 0; i < MagicLength; i++)
                if (fileBytes[i] != Magic[i])
                    throw new InvalidDataException(
                        "Save file has wrong magic bytes — not a UGM SaveSystem file (or corrupt header).");

            var formatVer = ReadUInt16LE(fileBytes, 4);
            if (formatVer != CurrentFormatVersion)
                throw new InvalidDataException(
                    $"Unsupported save format version {formatVer}; this build expects {CurrentFormatVersion}.");

            var codecId   = ReadUInt16LE(fileBytes, 6);
            var bodyLen   = ReadInt32LE(fileBytes, 8);
            if (bodyLen < 0 || HeaderLength + bodyLen + TrailerLength > fileBytes.Length)
                throw new InvalidDataException(
                    $"Save file body length {bodyLen} is invalid for file size {fileBytes.Length}.");

            var declaredCrc = ReadUInt32LE(fileBytes, HeaderLength + bodyLen);
            var actualCrc   = Crc32.Compute(fileBytes, MagicLength, HeaderLength - MagicLength + bodyLen);
            if (declaredCrc != actualCrc)
                throw new InvalidDataException(
                    $"Save file CRC mismatch (file=0x{declaredCrc:X8}, computed=0x{actualCrc:X8}). " +
                    "File is likely corrupt.");

            return new ParsedHeader
            {
                FormatVersion = formatVer,
                CodecId       = codecId,
                BodyOffset    = HeaderLength,
                BodyLength    = bodyLen,
            };
        }

        /// <summary>Slice the body bytes out of <paramref name="fileBytes"/> based on a parsed header.</summary>
        public static byte[] ExtractBody(byte[] fileBytes, ParsedHeader header)
        {
            var body = new byte[header.BodyLength];
            Buffer.BlockCopy(fileBytes, header.BodyOffset, body, 0, header.BodyLength);
            return body;
        }

        // ── little-endian read/write helpers (avoid BitConverter for
        // determinism across architectures with different default endianness) ──
        private static void WriteUInt16LE(byte[] dst, int offset, ushort value)
        {
            dst[offset]     = (byte)(value & 0xFF);
            dst[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
        private static void WriteInt32LE(byte[] dst, int offset, int value) => WriteUInt32LE(dst, offset, (uint)value);
        private static void WriteUInt32LE(byte[] dst, int offset, uint value)
        {
            dst[offset]     = (byte)(value & 0xFF);
            dst[offset + 1] = (byte)((value >> 8) & 0xFF);
            dst[offset + 2] = (byte)((value >> 16) & 0xFF);
            dst[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
        private static ushort ReadUInt16LE(byte[] src, int offset) =>
            (ushort)(src[offset] | (src[offset + 1] << 8));
        private static int ReadInt32LE(byte[] src, int offset) => (int)ReadUInt32LE(src, offset);
        private static uint ReadUInt32LE(byte[] src, int offset) =>
            (uint)(src[offset]
                | (src[offset + 1] << 8)
                | (src[offset + 2] << 16)
                | (src[offset + 3] << 24));
    }

    /// <summary>
    /// CRC32 (IEEE 802.3 polynomial 0xEDB88320). Used by SaveFileHeader for
    /// torn-write / corruption detection. Lifted into its own class so users
    /// who want to stamp their own checksums on save sidecar files can reuse
    /// it.
    /// </summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            const uint poly = 0xEDB88320u;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (poly ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        public static uint Compute(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFFu;
            int end = offset + length;
            for (int i = offset; i < end; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
