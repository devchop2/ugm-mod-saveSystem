using System.Threading.Tasks;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Pluggable destination for save bytes. Covers local filesystem, cloud
    /// services (Firebase Storage, Firestore, S3, custom REST), and any other
    /// byte sink. SaveManager hands a finished FlatBuffer byte[] to a provider
    /// and never asks what's behind the interface.
    ///
    /// Implementations SHOULD be safe to call from a background thread but are
    /// not required to be re-entrant (SaveManager serializes calls).
    /// </summary>
    public interface IStorageProvider
    {
        /// <summary>Persist bytes under a stable key (typically a filename).</summary>
        Task WriteAsync(string key, byte[] data);

        /// <summary>Return previously written bytes, or null if absent.</summary>
        Task<byte[]> ReadAsync(string key);

        /// <summary>True iff a value previously written under <paramref name="key"/> still exists.</summary>
        Task<bool> ExistsAsync(string key);

        /// <summary>Remove the value under <paramref name="key"/>. No-op if absent.</summary>
        Task DeleteAsync(string key);
    }
}
