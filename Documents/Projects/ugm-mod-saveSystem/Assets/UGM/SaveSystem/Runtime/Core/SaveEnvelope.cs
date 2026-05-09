using System.Collections.Generic;

namespace UGM.SaveSystem
{
    /// <summary>
    /// The intermediate "JsonData-like" container produced by Stage 1
    /// (ISaveAggregator) and consumed by Stage 2 (ISaveCodec). One envelope
    /// is the entirety of a single save file's logical contents.
    ///
    /// Each slot value is stored *already encoded* as a byte[]. This costs one
    /// extra copy at save time but buys three things:
    ///   • Forward-compat: unknown slots from a future module version are
    ///     preserved verbatim and round-tripped on the next save.
    ///   • Lazy/typed decode: the codec can decode each slot against the
    ///     caller's registered type instead of hoping a single envelope-wide
    ///     resolver guesses every slot's shape correctly.
    ///   • Per-slot dirty caching: an aggregator can reuse a slot's encoded
    ///     bytes across saves when the underlying data didn't change.
    /// </summary>
    public class SaveEnvelope
    {
        /// <summary>Logical save format version. Bumped on breaking schema changes.</summary>
        public int Version = 1;

        /// <summary>Unix seconds (UTC) at which this envelope was captured.</summary>
        public long SavedAtUnix;

        /// <summary>
        /// Slot key → already-encoded bytes for that slot's payload. Plain
        /// Dictionary for broadest codec compatibility — every common
        /// serializer (MessagePack, JSON, ...) has a built-in formatter for
        /// Dictionary&lt;string, byte[]&gt;. If you need deterministic byte-
        /// for-byte output (e.g. cloud-sync hash comparisons), sort the keys
        /// before encoding inside your custom codec.
        /// </summary>
        public Dictionary<string, byte[]> Slots = new Dictionary<string, byte[]>();
    }
}
