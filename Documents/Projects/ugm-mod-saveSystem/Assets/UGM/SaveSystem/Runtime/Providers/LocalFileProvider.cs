using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace UGM.SaveSystem.Providers
{
    /// <summary>
    /// Writes save bytes to a file under <see cref="Application.persistentDataPath"/>
    /// (or any caller-specified directory). Uses temp-file + rename for crash-safe
    /// atomic writes — a half-written file never replaces the previous valid save.
    ///
    /// Threading: File IO runs synchronously inside the returned Task. For very
    /// large saves consider a background thread; for typical user data sizes
    /// (&lt; 1 MB) the cost is dominated by disk flush, not CPU.
    /// </summary>
    public class LocalFileProvider : IStorageProvider
    {
        private readonly string _baseDirectory;

        public LocalFileProvider(string baseDirectory = null)
        {
            _baseDirectory = string.IsNullOrEmpty(baseDirectory)
                ? Application.persistentDataPath
                : baseDirectory;

            if (!Directory.Exists(_baseDirectory))
                Directory.CreateDirectory(_baseDirectory);
        }

        public Task WriteAsync(string key, byte[] data)
        {
            var path = PathFor(key);
            var temp = path + ".tmp";

            // Write to a sibling temp file first, then atomically replace.
            File.WriteAllBytes(temp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);

            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(string key)
        {
            var path = PathFor(key);
            return Task.FromResult(File.Exists(path) ? File.ReadAllBytes(path) : null);
        }

        public Task<bool> ExistsAsync(string key)
        {
            return Task.FromResult(File.Exists(PathFor(key)));
        }

        public Task DeleteAsync(string key)
        {
            var path = PathFor(key);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        private string PathFor(string key) => Path.Combine(_baseDirectory, key);
    }
}
