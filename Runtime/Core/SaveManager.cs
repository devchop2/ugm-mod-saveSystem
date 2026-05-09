using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Static entry point for the save pipeline. Glues three pluggable stages:
    ///   1. <see cref="ISaveAggregator"/>  — picks game data into a SaveEnvelope
    ///   2. <see cref="ISaveCodec"/>       — encodes the envelope to bytes
    ///   3. <see cref="IStorageProvider"/> — persists the bytes
    ///
    /// <para>Typical usage — three lines at boot, two lines at runtime:</para>
    /// <code>
    ///   SaveManager.Register("Inventory.Items", () =&gt; inv.Items, v =&gt; inv.Items = v);
    ///   SaveManager.Register("Inventory.Coins", () =&gt; inv.Coins, v =&gt; inv.Coins = v);
    ///   await SaveManager.LoadAsync();
    ///   ...
    ///   await SaveManager.SaveAsync();              // → DefaultFileName ("save.dat")
    ///   await SaveManager.SaveAsync("slot1.dat");   // → "slot1.dat"
    /// </code>
    ///
    /// <para>
    /// Multi-slot games don't need multiple SaveManagers — pass a different
    /// fileName to Save/Load/Delete each call. The slot registration set is
    /// shared across all files (load slot 1 → registered objects updated;
    /// save to slot 2 → those same objects' current state written to slot 2).
    /// </para>
    ///
    /// <para>
    /// File name handling: every Save/Load/Delete/Exists accepts an optional
    /// <c>fileName</c>. If null/empty, <see cref="DefaultFileName"/> is used.
    /// The actual on-disk path is the provider's responsibility — the default
    /// <see cref="Providers.LocalFileProvider"/> resolves names against
    /// <see cref="Application.persistentDataPath"/>.
    /// </para>
    ///
    /// <para>
    /// First touch must happen on the Unity main thread — the lazily-created
    /// default <see cref="Providers.LocalFileProvider"/> reads
    /// <c>Application.persistentDataPath</c>, which is only safe from the
    /// main thread.
    /// </para>
    /// </summary>
    public static class SaveManager
    {
        // ── State (lazily initialized) ───────────────────────────────────────

        private static ISaveAggregator  _aggregator;
        private static ISaveCodec       _codec;
        private static IStorageProvider _provider;
        private static string _defaultFileName = "save.dat";

        /// <summary>
        /// The Stage 1 component. Default = <see cref="DefaultAggregator"/>.
        /// Replace if you want attribute-based discovery, ECS-aware capture, etc.
        /// </summary>
        public static ISaveAggregator Aggregator
        {
            get => _aggregator ?? (_aggregator = new DefaultAggregator());
            set => _aggregator = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The Stage 2 component. Default = <see cref="MessagePackCodec"/>.
        /// Replace with JsonCodec for debugging, FlatBufferCodec for legacy, etc.
        /// </summary>
        public static ISaveCodec Codec
        {
            get => _codec ?? (_codec = new MessagePackCodec());
            set => _codec = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The Stage 3 component. Default = <see cref="Providers.LocalFileProvider"/>
        /// rooted at <see cref="Application.persistentDataPath"/>. Replace with an
        /// EncryptedProvider, FirebaseStorageProvider, etc.
        /// </summary>
        public static IStorageProvider Provider
        {
            get => _provider ?? (_provider = new Providers.LocalFileProvider());
            set => _provider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// File name used when Save/Load/Delete/Exists are called without an
        /// explicit fileName. Defaults to "save.dat". Setting null/empty
        /// reverts to "save.dat".
        /// </summary>
        public static string DefaultFileName
        {
            get => _defaultFileName;
            set => _defaultFileName = string.IsNullOrEmpty(value) ? "save.dat" : value;
        }

        // ── Diagnostics ──────────────────────────────────────────────────────

        public static int  LastLoadedFormatVersion { get; private set; }
        public static long LastSavedAtUnix         { get; private set; }
        public static bool HasLoaded               { get; private set; }

        // ── Slot registration ────────────────────────────────────────────────

        /// <summary>
        /// Register a save slot.
        ///
        /// <paramref name="key"/> is the stable id written into the save file —
        /// once you ship a build with a key, never change it without a
        /// migration.
        ///
        /// <paramref name="getter"/> is called at save time to obtain the
        /// current value to persist (cheap, do not do heavy work here).
        ///
        /// <paramref name="setter"/> is called at load time with the decoded
        /// value (may be null when the save file lacks this slot — handle
        /// gracefully).
        /// </summary>
        public static void Register<T>(string key, Func<T> getter, Action<T> setter)
            => Aggregator.Register(key, getter, setter);

        /// <summary>Remove a previously registered slot. Returns true if removed.</summary>
        public static bool Unregister(string key) => Aggregator.Unregister(key);

        /// <summary>True iff a slot with this key is currently registered.</summary>
        public static bool IsRegistered(string key) => Aggregator.IsRegistered(key);

        /// <summary>Mark a slot as needing re-encoding on the next Save (optional optimization signal).</summary>
        public static void MarkDirty(string key) => Aggregator.MarkDirty(key);

        // ── Save / Load / Delete / Exists ────────────────────────────────────

        /// <summary>
        /// Capture every registered slot, encode, and write to disk.
        /// File name defaults to <see cref="DefaultFileName"/> if null/empty.
        /// </summary>
        public static async Task SaveAsync(string fileName = null)
        {
            // Capture happens on the calling thread (usually main thread): the
            // getters touch Unity objects, which is unsafe off the main thread.
            var envelope = Aggregator.Capture(Codec);

            // Encoding + disk IO can run wherever — push onto the thread pool
            // so a 30s autosave loop on a big envelope doesn't hitch the frame.
            var bodyBytes = await Task.Run(() => Codec.EncodeEnvelope(envelope)).ConfigureAwait(false);
            var fileBytes = SaveFileHeader.Wrap(bodyBytes, Codec.CodecId);

            await Provider.WriteAsync(ResolveFileName(fileName), fileBytes).ConfigureAwait(false);

            LastSavedAtUnix = envelope.SavedAtUnix;
        }

        /// <summary>
        /// Load and apply the file. Returns false if the file doesn't exist;
        /// throws on corruption or codec mismatch.
        /// </summary>
        public static async Task<bool> LoadAsync(string fileName = null)
        {
            var name = ResolveFileName(fileName);
            if (!await Provider.ExistsAsync(name).ConfigureAwait(false)) return false;

            var fileBytes = await Provider.ReadAsync(name).ConfigureAwait(false);
            if (fileBytes == null || fileBytes.Length == 0) return false;

            var header = SaveFileHeader.Unwrap(fileBytes);

            if (header.CodecId != Codec.CodecId)
                throw new InvalidDataException(
                    $"Save file '{name}' was written by codec id 0x{header.CodecId:X4}, " +
                    $"but current codec is '{Codec.Name}' (id 0x{Codec.CodecId:X4}). " +
                    "Switch codecs before loading or run a migration.");

            var body = SaveFileHeader.ExtractBody(fileBytes, header);

            // Decoding off the main thread keeps a cold-load from blocking the frame.
            var envelope = await Task.Run(() => Codec.DecodeEnvelope(body)).ConfigureAwait(false);

            // Apply runs back on the caller (main thread in typical Unity flow):
            // setters often touch scene objects.
            Aggregator.Apply(envelope, Codec);

            LastLoadedForma