using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UGM.SaveSystem.EditorTools.Dependencies
{
    /// <summary>
    /// Reads <c>Assets/UGM/SaveSystem/dependencies.json</c> on editor load and
    /// downloads any missing or version-mismatched binaries (Google.FlatBuffers
    /// DLL, flatc executable, …). Both NuGet packages and GitHub release assets
    /// are handled by the same code path, so adding a third dependency is just
    /// one more entry in the JSON.
    ///
    /// On-disk install state is tracked with a sidecar file next to each
    /// destination — e.g. <c>Google.FlatBuffers.dll.version</c> contains
    /// "23.5.26". When the manifest version changes, the sidecar mismatches and
    /// the installer reinstalls. No central state file, nothing to gitignore
    /// per-dev — it just works the same on every machine.
    ///
    /// First-run UX: a single confirmation dialog summarizes everything that
    /// will be downloaded; user approves once, all installs run, AssetDatabase
    /// refreshes. Subsequent loads with an up-to-date checkout are a no-op.
    /// </summary>
    [InitializeOnLoad]
    public static class DependencyInstaller
    {
        const string ManifestPath = "Assets/UGM/SaveSystem/dependencies.json";
        const string LogTag       = "[UGM.SaveSystem]";

        static DependencyInstaller()
        {
            // Defer until the current import settles — InitializeOnLoad runs
            // mid-import on first project open, when AssetDatabase is in flux.
            EditorApplication.delayCall += AutoInstallIfNeeded;
        }

        // ── Public entry points ──────────────────────────────────────────────

        [MenuItem("UGM/SaveSystem/Reinstall Dependencies", priority = 300)]
        public static void ReinstallAllMenu()
        {
            var manifest = LoadManifest();
            if (manifest == null) return;
            InstallMissing(manifest, force: true);
        }

        [MenuItem("UGM/SaveSystem/Check Dependencies", priority = 301)]
        public static void CheckMenu()
        {
            var manifest = LoadManifest();
            if (manifest == null) return;
            var missing = ResolveMissing(manifest);
            if (missing.Count == 0)
            {
                Debug.Log($"{LogTag} All dependencies up to date.");
                return;
            }
            var msg = new StringBuilder("Missing or out of date:\n");
            foreach (var m in missing) msg.AppendLine($"  • {m.Entry.id} {m.Entry.version}  →  {m.Destination}");
            Debug.LogWarning($"{LogTag} {msg}");
        }

        // ── Auto-install on editor load ──────────────────────────────────────

        static void AutoInstallIfNeeded()
        {
            var manifest = LoadManifest();
            if (manifest == null) return;

            var missing = ResolveMissing(manifest);
            if (missing.Count == 0) return;

            var summary = new StringBuilder("이 모듈은 다음 의존성이 필요하지만 누락되어 있습니다:\n\n");
            foreach (var m in missing)
                summary.AppendLine($"• {m.Entry.id} {m.Entry.version}");
            summary.AppendLine("\n지금 자동으로 다운로드할까요?");

            var ok = EditorUtility.DisplayDialog(
                "UGM.SaveSystem — Dependencies",
                summary.ToString(),
                "다운로드", "건너뛰기");
            if (!ok)
            {
                Debug.LogWarning(
                    $"{LogTag} Skipped dependency install. Run \"UGM > SaveSystem > Reinstall Dependencies\" later, " +
                    "or some features may not compile.");
                return;
            }

            InstallEntries(missing);
        }

        // ── Resolution ──────────────────────────────────────────────────────

        class ResolvedInstall
        {
            public DependencyEntry Entry;
            public DependencyPlatform Platform;   // null for single-platform entries (nuget)
            public string DownloadUrl;
            public string ExtractPath;
            public string Destination;
        }

        static DependencyManifest LoadManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                Debug.LogWarning($"{LogTag} Manifest not found at {ManifestPath}; skipping dependency install.");
                return null;
            }
            try
            {
                var json = File.ReadAllText(ManifestPath);
                return JsonUtility.FromJson<DependencyManifest>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} Failed to parse {ManifestPath}: {e.Message}");
                return null;
            }
        }

        static List<ResolvedInstall> ResolveMissing(DependencyManifest manifest)
        {
            var result = new List<ResolvedInstall>();
            foreach (var dep in manifest.dependencies)
            {
                var resolved = ResolveForHost(dep);
                if (resolved == null) continue;       // unsupported on this OS — silently skip
                if (IsUpToDate(resolved)) continue;
                result.Add(resolved);
            }
            return result;
        }

        /// <summary>
        /// Compute the actual download URL + destination for the current host
        /// platform. Returns null if the entry doesn't apply here (e.g. a
        /// platform-specific binary on an unsupported OS).
        /// </summary>
        static ResolvedInstall ResolveForHost(DependencyEntry dep)
        {
            switch (dep.kind)
            {
                case "nuget":
                    if (string.IsNullOrEmpty(dep.package) || string.IsNullOrEmpty(dep.destination))
                        throw new Exception($"nuget entry '{dep.id}' missing package/destination.");
                    return new ResolvedInstall
                    {
                        Entry        = dep,
                        DownloadUrl  = $"https://www.nuget.org/api/v2/package/{dep.package}/{dep.version}",
                        ExtractPath  = dep.extract,
                        Destination  = dep.destination,
                    };

                case "github-release":
                    var hostName = Application.platform.ToString();
                    var pf = dep.platforms.Find(p => p.name == hostName);
                    if (pf == null) return null;
                    return new ResolvedInstall
                    {
                        Entry        = dep,
                        Platform     = pf,
                        DownloadUrl  = $"https://github.com/{dep.repo}/releases/download/{dep.tag}/{pf.asset}",
                        ExtractPath  = pf.extract,
                        Destination  = pf.destination,
                    };

                default:
                    Debug.LogError($"{LogTag} Unknown dependency kind '{dep.kind}' for entry '{dep.id}'.");
                    return null;
            }
        }

        static bool IsUpToDate(ResolvedInstall r)
        {
            if (!File.Exists(r.Destination)) return false;
            var sidecar = r.Destination + ".version";
            if (!File.Exists(sidecar)) return false;          // file present but installer didn't put it — treat as stale
            var onDisk = File.ReadAllText(sidecar).Trim();
            return string.Equals(onDisk, r.Entry.version, StringComparison.Ordinal);
        }

        // ── Install ─────────────────────────────────────────────────────────

        static void InstallMissing(DependencyManifest manifest, bool force)
        {
            var entries = force
                ? manifest.dependencies
                    .Select(ResolveForHost)
                    .Where(r => r != null)
                    .ToList()
                : ResolveMissing(manifest);
            InstallEntries(entries);
        }

        static void InstallEntries(List<ResolvedInstall> entries)
        {
            if (entries.Count == 0)
            {
                Debug.Log($"{LogTag} Nothing to install.");
                return;
            }

            // NuGet and GitHub release downloads both require modern TLS.
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

            int ok = 0, failed = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var r = entries[i];
                var label = r.Platform != null
                    ? $"{r.Entry.id} {r.Entry.version} ({r.Platform.name})"
                    : $"{r.Entry.id} {r.Entry.version}";

                EditorUtility.DisplayProgressBar(
                    "UGM.SaveSystem — Installing dependencies",
                    $"({i + 1}/{entries.Count}) {label}",
                    (float)i / entries.Count);

                try
                {
                    InstallOne(r);
                    Debug.Log($"{LogTag} Installed {label} → {r.Destination}");
                    ok++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogTag} {label} install failed: {e.Message}\n{e}");
                    failed++;
                }
            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            var summary = $"{ok} installed, {failed} failed.";
            if (failed > 0)
                EditorUtility.DisplayDialog("UGM.SaveSystem — Dependencies", summary, "OK");
            Debug.Log($"{LogTag} {summary}");
        }

        static void InstallOne(ResolvedInstall r)
        {
            var dir = Path.GetDirectoryName(r.Destination);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var temp = Path.Combine(Path.GetTempPath(), $"ugm-savesystem-{Guid.NewGuid():N}.archive");
            try
            {
                using (var wc = new WebClient())
                {
                    // GitHub redirects releases to S3; WebClient follows redirects by default.
                    wc.Headers[HttpRequestHeader.UserAgent] = "UGM.SaveSystem/DependencyInstaller";
                    wc.DownloadFile(r.DownloadUrl, temp);
                }

                // Both .nupkg and the flatc release archives are zip files.
                using (var zip = ZipFile.OpenRead(temp))
                {
                    var entry = FindEntry(zip, r.ExtractPath);
                    if (entry == null)
                    {
                        var available = string.Join(", ", zip.Entries.Take(5).Select(e => e.FullName));
                        throw new FileNotFoundException(
                            $"Asset '{r.ExtractPath}' not found in archive. " +
                            $"First entries: {available}{(zip.Entries.Count > 5 ? ", ..." : "")}");
                    }

                    if (File.Exists(r.Destination)) File.Delete(r.Destination);
                    entry.ExtractToFile(r.Destination, overwrite: true);
                }

                // Sidecar version stamp — drives subsequent IsUpToDate checks.
                File.WriteAllText(r.Destination + ".version", r.Entry.version);

                // Best-effort: mark binary as executable on POSIX.
                #if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                try { System.Diagnostics.Process.Start("chmod", $"+x \"{r.Destination}\""); } catch {}
                #endif
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch {}
            }
        }

        /// <summary>
        /// Match either an exact archive entry path or the file at the root of
        /// the archive (some release zips have a top-level folder, some don't).
        /// </summary>
        static ZipArchiveEntry FindEntry(ZipArchive zip, string wanted)
        {
            // Exact match first.
            var exact = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, wanted, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Filename match anywhere in the archive (handles "release-folder/flatc.exe").
            var leaf = Path.GetFileName(wanted);
            return zip.Entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FullName), leaf, StringComparison.OrdinalIgnoreCase));
        }
    }
}
