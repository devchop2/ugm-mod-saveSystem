using System;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Stage 2 of the pipeline: turns a populated <see cref="SaveEnvelope"/>
    /// into a byte stream and back. Codecs are interchangeable — the same
    /// aggregator + provider can pair with any codec (MessagePack, JSON,
    /// FlatBuffers, ...). The header magic bytes written/read by SaveManager
    /// remember which codec produced a file so old saves keep loading after
    /// the user swaps codecs.
    ///
    /// Implementations MUST be thread-safe for parallel encode/decode calls
    /// (SaveManager may run them on a background thread).
    /// </summary>
    public interface ISaveCodec
    {
        /// <summary>
        /// Stable id stamped into the file header. Reserve a unique value per
        /// codec implementation so headers can dispatch reads to the correct
        /// codec without metadata strings.
        ///
        /// Built-in codecs use ids 0x0000–0x00FF; user codecs should pick
        /// 0x0100+.
        /// </summary>
        ushort CodecId { get; }

        /// <summary>
        /// Human-readable name shown in error messages and the save header
        /// dump tool. e.g. "messagepack", "json".
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Encode the envelope's body. The header is added by SaveManager —
        /// codec returns just the payload bytes.
        /// </summary>
        byte[] EncodeEnvelope(SaveEnvelope envelope);

        /// <summary>
        /// Decode the envelope body. Bytes here are the body only (header
        /// already stripped by SaveManager).
        /// </summary>
        SaveEnvelope DecodeEnvelope(byte[] body);

        /// <summary>
        /// Encode a single slot's payload. The aggregator calls this once per
        /// slot at save time and stashes the result in <see cref="SaveEnvelope.Slots"/>.
        /// </summary>
        byte[] EncodeSlot(object slotData, Type type);

        /// <summary>
        /// Decode a single slot's payload back into the registered type.
        /// Returns null if <paramref name="bytes"/> is null/empty.
        /// </summary>
        object DecodeSlot(byte[] bytes, Type type);
    }
}
