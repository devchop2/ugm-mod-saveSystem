using System;
using MessagePack;
using MessagePack.Resolvers;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Default codec for the SaveSystem. Encodes envelope + per-slot payloads
    /// with MessagePack-CSharp using a contractless resolver, so user data
    /// classes need NO attributes — public fields and properties are picked
    /// up automatically.
    ///
    /// Resolver chain (first match wins):
    ///   1. <see cref="UnityResolver"/>      — Vector2/3/4, Quaternion, Color, Color32
    ///   2. <see cref="StandardResolver"/>   — primitives, strings, common BCL types
    ///   3. <see cref="ContractlessStandardResolver"/> — anything else with public members
    ///
    /// IL2CPP note: ContractlessStandardResolver uses runtime IL emit which
    /// is unavailable on IL2CPP. Mobile / WebGL builds should switch to a
    /// StaticCompositeResolver populated by mpc-generated formatters, OR
    /// register types ahead of build with [MessagePackObject].
    /// </summary>
    public class MessagePackCodec : ISaveCodec
    {
        public const ushort CodecIdValue = 0x0001;

        public ushort CodecId => CodecIdValue;
        public string Name    => "messagepack";

        private readonly MessagePackSerializerOptions _options;

        public MessagePackCodec()
        {
            // Build the resolver chain once and cache it. StaticCompositeResolver
            // is intentionally avoided here so users can still tweak the chain
            // by providing their own MessagePackCodec(options) instance.
            var resolver = CompositeResolver.Create(
                UnityResolver.Instance,
                StandardResolver.Instance,
                ContractlessStandardResolver.Instance);

            _options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
        }

        /// <summary>Provide a custom options instance — useful for adding LZ4 compression or extra resolvers.</summary>
        public MessagePackCodec(MessagePackSerializerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public byte[] EncodeEnvelope(SaveEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            return MessagePackSerializer.Serialize(envelope, _options);
        }

        public SaveEnvelope DecodeEnvelope(byte[] body)
        {
            if (body == null || body.Length == 0)
                return new SaveEnvelope();
            return MessagePackSerializer.Deserialize<SaveEnvelope>(body, _options);
        }

        public byte[] EncodeSlot(object slotData, Type type)
        {
            if (slotData == null) return Array.Empty<byte>();
            // Non-generic Serialize uses the runtime type; pass the registered
            // type explicitly so derived runtime types don't sneak extra fields
            // into the wire format.
            return MessagePackSerializer.Serialize(type, slotData, _options);
        }

        public object DecodeSlot(byte[] bytes, Type type)
        {
            if (bytes == null || bytes.Length == 0) return null;
            return MessagePackSerializer.Deserialize(type, bytes, _options);
        }
    }
}
