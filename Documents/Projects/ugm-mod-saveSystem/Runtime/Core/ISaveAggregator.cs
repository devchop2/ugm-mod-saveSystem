using System;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Stage 1 of the pipeline: knows which game-side data goes under which
    /// save slot key, and can pull it (Capture) or push it back (Apply).
    /// One aggregator instance is owned by SaveManager and reused across
    /// every save / load.
    ///
    /// The contract is intentionally minimal so the *implementation* can vary
    /// (manual register, attribute-discovery, ECS, ScriptableObject-driven, …)
    /// without SaveManager learning about any of it.
    /// </summary>
    public interface ISaveAggregator
    {
        /// <summary>
        /// Register a save slot.
        ///
        /// <paramref name="key"/> is the stable id written into the save file.
        /// Once you ship a build with a key, NEVER change it without a
        /// migration — old saves will think the slot vanished.
        ///
        /// <paramref name="getter"/> is called at save time to obtain the
        /// current value to persist. Should be cheap; do NOT do heavy work here.
        ///
        /// <paramref name="setter"/> is called at load time with the decoded
        /// value. May be called with default/null when the save file lacks
        /// this slot — implementations should handle that gracefully.
        ///
        /// The codec must be able to round-trip <typeparamref name="T"/>;
        /// for MessagePack-Contractless that's any class with public fields
        /// or properties.
        /// </summary>
        ISaveAggregator Register<T>(string key, Func<T> getter, Action<T> setter);

        /// <summary>Remove a previously registered slot. Returns true if removed.</summary>
        bool Unregister(string key);

        /// <summary>True iff a slot with this key is currently registered.</summary>
        bool IsRegistered(string key);

        /// <summary>
        /// Walk every registered slot, invoke its getter, encode the value via
        /// the codec, and assemble a SaveEnvelope. May reuse cached encoded
        /// bytes for slots that have not been marked dirty since the last
        /// capture (an optional optimization — implementations may always
        /// re-encode and stay correct).
        /// </summary>
        SaveEnvelope Capture(ISaveCodec codec);

        /// <summary>
        /// For each registered slot whose key appears in the envelope, decode
        /// the bytes against the registered type and invoke the setter. Slots
        /// in the envelope with no matching registration are NOT discarded —
        /// implementations should retain them so the next Capture round-trips
        /// unknown data verbatim (forward-compat).
        /// </summary>
        void Apply(SaveEnvelope envelope, ISaveCodec codec);

        /// <summary>
        /// Mark a slot as needing re-encoding on the next Capture. Game code
        /// calls this when it mutates the underlying data. Calling this is
        /// optional — if the aggregator does not implement dirty caching, it
        /// just always re-encodes and ignores the call.
        /// </summary>
        void MarkDirty(string key);
    }
}
