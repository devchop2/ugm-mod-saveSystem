using System;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Formatters;
using UnityEngine;

namespace UGM.SaveSystem
{
    /// <summary>
    /// MessagePack formatter set for the small handful of Unity value types
    /// you'll actually want in a save file: Vector2/3/4, Quaternion, Color,
    /// Color32. Each is encoded as a tiny fixed-length array of primitives.
    ///
    /// Why not let ContractlessStandardResolver auto-handle them? Vector3 et
    /// al. expose internal struct layout that varies by Unity version (e.g.
    /// magnitude / normalized properties have backing fields on some versions
    /// and not others), so reflection-based serialization would emit unstable
    /// wire formats. Encoding [x,y,z] explicitly is small, fast, and version-
    /// stable.
    ///
    /// To extend: write another IMessagePackFormatter&lt;T&gt; and add it to
    /// the dictionary in the static constructor.
    /// </summary>
    public class UnityResolver : IFormatterResolver
    {
        public static readonly UnityResolver Instance = new UnityResolver();

        private static readonly Dictionary<Type, object> Formatters = new Dictionary<Type, object>
        {
            { typeof(Vector2),    new Vector2Formatter()    },
            { typeof(Vector3),    new Vector3Formatter()    },
            { typeof(Vector4),    new Vector4Formatter()    },
            { typeof(Quaternion), new QuaternionFormatter() },
            { typeof(Color),      new ColorFormatter()      },
            { typeof(Color32),    new Color32Formatter()    },
        };

        private UnityResolver() { }

        public IMessagePackFormatter<T> GetFormatter<T>() => FormatterCache<T>.Formatter;

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter =
                Formatters.TryGetValue(typeof(T), out var f) ? (IMessagePackFormatter<T>)f : null;
        }

        // ── Vector2 ──────────────────────────────────────────────────────────
        private sealed class Vector2Formatter : IMessagePackFormatter<Vector2>
        {
            public void Serialize(ref MessagePackWriter w, Vector2 v, MessagePackSerializerOptions options)
            {
                w.WriteArrayHeader(2);
                w.Write(v.x); w.Write(v.y);
            }

            public Vector2 Deserialize(ref MessagePackReader r, MessagePackSerializerOptions options)
            {
                if (r.TryReadNil()) return default;
                var len = r.ReadArrayHeader();
                if (len < 2) throw new MessagePackSerializationException("Vector2 expects array length >= 2.");
                var x = r.ReadSingle(); var y = r.ReadSingle();
                for (int i = 2; i < len; i++) r.Skip();
                return new Vector2(x, y);
            }
        }

        // ── Vector3 ──────────────────────────────────────────────────────────
        private sealed class Vector3Formatter : IMessagePackFormatter<Vector3>
        {
            public void Serialize(ref MessagePackWriter w, Vector3 v, MessagePackSerializerOptions options)
            {
                w.WriteArrayHeader(3);
                w.Write(v.x); w.Write(v.y); w.Write(v.z);
            }

            public Vector3 Deserialize(ref MessagePackReader r, MessagePackSerializerOptions options)
            {
                if (r.TryReadNil()) return default;
                var len = r.ReadArrayHeader();
                if (len < 3) throw new MessagePackSerializationException("Vector3 expects array length >= 3.");
                var x = r.ReadSingle(); var y = r.ReadSingle(); var z = r.ReadSingle();
                for (int i = 3; i < len; i++) r.Skip();
                return new Vector3(x, y, z);
            }
        }

        // ── Vector4 ──────────────────────────────────────────────────────────
        private sealed class Vector4Formatter : IMessagePackFormatter<Vector4>
        {
            public void Serialize(ref MessagePackWriter w, Vector4 v, MessagePackSerializerOptions options)
            {
                w.WriteArrayHeader(4);
                w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w);
            }

            public Vector4 Deserialize(ref MessagePackReader r, MessagePackSerializerOptions options)
            {
                if (r.TryReadNil()) return default;
                var len = r.ReadArrayHeader();
                if (len < 4) throw new MessagePackSerializationException("Vector4 expects array length >= 4.");
                var x = r.ReadSingle(); var y = r.ReadSingle(); var z = r.ReadSingle(); var w_ = r.ReadSingle();
                for (int i = 4; i < len; i++) r.Skip();
                return new Vector4(x, y, z, w_);
            }
        }

        // ── Quaternion ───────────────────────────────────────────────────────
        private sealed class QuaternionFormatter : IMessagePackFormatter<Quaternion>
        {
            public void Serialize(ref MessagePackWriter w, Quaternion q, MessagePackSerializerOptions options)
            {
                w.WriteArrayHeader(4);
                w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w);
            }

            public Quaternion Deserialize(ref MessagePackReader r, MessagePackSerializerOptions options)
            {
                if (r.TryReadNil()) return default;
                var len = r.ReadArrayHeader();
                if (len < 4) throw new MessagePackSerializationException("Quaternion expects array length >= 4.");
                var x = r.ReadSingle(); var y = r.ReadSingle(); var z = r.ReadSingle(); var w_ = r.ReadSingle();
                for (int i = 4; i < len; i++) r.Skip();
                return new Quaternion(x, y, z, w_);
            }
        }

        // ── Color (float RGBA) ───────────────────────────────────────────────
        private sealed class ColorFormatter : IMessagePackFormatter<Color>
        {
            public void Serialize(ref MessagePackWriter w, Color c, MessagePackSerializerOptions options)
            {
                w.WriteArrayHeader(4);
                w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
            }

            public Color Deserialize(ref MessagePackReader r, MessagePackSerializerOptions options)
            {
                if (r.TryReadNil()) return default;
                var len = r.ReadArrayHeader();
                if (len < 4) throw new MessagePackSerializationException("Color expects array length >= 4.");
                var rr = r.ReadSingle(); var gg = r.ReadSingle(); var bb = r.ReadSingle(); var aa = r.ReadSingle();
                for (int i = 4; i < len; i++) r.Skip();
                return new Color(rr, gg, bb, aa);
            }
        }

        // ── Color32 (byte RGBA) ──────────────────────────────────────────────
        private sealed class Color32Formatter : IMessagePackFormatter<Color32>
        {
            public void Serialize(ref MessagePackWriter w, Color32 c, MessagePackSerializerOptions options)
            {
                w.WriteArrayHeader(4);
                w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
            }

            public Color32 Deserialize(ref MessagePackReader r, MessagePackSerializerOptions options)
            {
                if (r.TryReadNil()) return default;
                var len = r.ReadArrayHeader();
                if (len < 4) throw new MessagePackSerializationException("Color32 expects array length >= 4.");
                var rr = r.ReadByte(); var gg = r.ReadByte(); var bb = r.ReadByte(); var aa = r.ReadByte();
                for (int i = 4; i < len; i++) r.Skip();
                return new Color32(rr, gg, bb, aa);
            }
        }
    }
}
