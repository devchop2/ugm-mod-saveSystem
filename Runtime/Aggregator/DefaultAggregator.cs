using System;
using System.Collections.Generic;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Default implementation of <see cref="ISaveAggregator"/>: a flat list of
    /// registered slots, with optional per-slot dirty-byte caching so unchanged
    /// slots skip re-encoding on subsequent saves.
    ///
    /// Designed for the autosave-every-30s case: capturing 100 unchanged slots
    /// + 1 changed slot does only 1 encode pass.
    ///
    /// Thread-safety: register/unregister/capture/apply are NOT safe to call
    /// concurrently. SaveManager serializes them in practice.
    /// </summary>
    public class DefaultAggregator : ISaveAggregator
    {
        private class Slot
        {
            public string Key;
            public Type DataType;
            public Func<object> Getter;
            public Action<object> Setter;

            public bool Dirty = true;        // first capture must encode at least once
            public byte[] CachedBytes;       // last encoded body (may be null until first capture)
        }

        // Registration order is preserved (List), but lookup by key is O(1) (Dictionary).
        private readonly List<Slot> _orderedSlots = new List<Slot>();
        private readonly Dictionary<string, Slot> _byKey = new Dictionary<string, Slot>(StringComparer.Ordinal);

        // Slots present in the last loaded envelope but NOT registered locally.
        // We hold their raw bytes so the next Capture round-trips them verbatim
        // (keeps unknown slots from a future module version safe).
        private readonly Dictionary<string, byte[]> _unknownSlots = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public ISaveAggregator Register<T>(string key, Func<T> getter, Action<T> setter)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key must be non-empty.", nameof(key));
            if (getter == null) throw new ArgumentNullException(nameof(getter));
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            if (_byKey.ContainsKey(key))
                throw new InvalidOperationException($"Save slot '{key}' is already registered.");

            var slot = new Slot
            {
                Key = key,
                DataType = typeof(T),
                Getter = () => (object)getter(),
                Setter = obj => setter(obj is T t ? t : default),
            };

            _byKey[key] = slot;
            _orderedSlots.Add(slot);

            // If a previous Apply stashed bytes for this key as "unknown",
            // promote them to the cache so a Save right after Register doesn't
            // lose the data even before the user mutates anything.
            if (_unknownSlots.TryGetValue(key, out var pending))
            {
                slot.CachedBytes = pending;
                slot.Dirty = false;
                _unknownSlots.Remove(key);
            }

            return this;
        }

        public bool Unregister(string key)
        {
            if (!_byKey.TryGetValue(key, out var slot)) return false;
            _byKey.Remove(key);
            _orderedSlots.Remove(slot);
            return true;
        }

        public bool IsRegistered(string key) => _byKey.ContainsKey(key);

        public void MarkDirty(string key)
        {
            if (_byKey.TryGetValue(key, out var slot))
                slot.Dirty = true;
        }

        /// <summary>Mark every registered slot as needing re-encoding.</summary>
        public void MarkAllDirty()
        {
            foreach (var s in _orderedSlots) s.Dirty = true;
        }

        public SaveEnvelope Capture(ISaveCodec codec)
        {
            if (codec == null) throw new ArgumentNullException(nameof(codec));

            var env = new SaveEnvelope
            {
                Version = 1,
                SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            foreach (var slot in _orderedSlots)
            {
                if (slot.Dirty || slot.CachedBytes == null)
                {
                    var value = slot.Getter();
                    slot.CachedBytes = codec.EncodeSlot(value, slot.DataType);
                    slot.Dirty = false;
                }
                env.Slots[slot.Key] = slot.CachedBytes;
            }

            // Forward-compat: keep unknown slots verbatim so they survive a save
            // round-trip on a build that hasn't registered them yet.
            foreach (var kv in _unknownSlots)
                if (!env.Slots.ContainsKey(kv.Key))
                    env.Slots[kv.Key] = kv.Value;

            return env;
        }

        public void Apply(SaveEnvelope envelope, ISaveCodec codec)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (codec == null) throw new ArgumentNullException(nameof(codec));

            _unknownSlots.Clear();

            foreach (var kv in envelope.Slots)
            {
                if (_byKey.TryGetValue(kv.Key, out var slot))
                {
                    var value = codec.DecodeSlot(kv.Value, slot.DataType);
                    slot.Setter(value);

                    // Refresh the cache so a Capture immediately after Apply
                    // (without any mutation) writes identical bytes back.
                    slot.CachedBytes = kv.Value;
                    slot.Dirty = false;
                }
                else
                {
                    // Unknown to this build — preserve for round-trip.
                    _unknownSlots[kv.Key] = kv.Value;
                }
            }
        }
    }
}
