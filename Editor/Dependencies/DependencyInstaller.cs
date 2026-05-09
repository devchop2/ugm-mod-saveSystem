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
    /// Reads <c>dependencies.json</c> from the package root on editor load and
    /// downloads any missing or version-mismatched binaries (MessagePack DLLs).
    /// Both NuGet packages and GitHub release assets are handled by the same
    /// code path, so adding a new dependency is just one more entry in the JSON.
    ///
    /// <para>
    /// Package-root discovery uses the Editor asmdef as anchor — no hardcoded
    /// path. Works whether the module is installed as a UPM Git package
    /// (Packages/com.chopchopgames.ugm.savesystem/) or embedded inside the
    /// project's Assets folder.
    /// </para>
    ///
    /// <para>
    /// All downloaded binaries land in <c>Assets/Plugins/UGM/SaveSystem/</c>
    /// (a mutable, user-side location). The package folder itself is immutable
    /// when installed via UPM Git URL, so writing dependencies inside the
    /// package would silently fail on real user setups.
    /// </para>
    ///
    /// <para>
    /// On-disk install state is tracked with a sidecar file next to each
    /// destination — e.g. <c>MessagePack.dll.version</c> contains the version
    /// string. When the manifest version changes, the sidecar mismatches and
    /// the installer reinstalls. No central state file, nothing to gitignore
    /// per-dev — it just works the same on every machine.
    /// </para>
    ///
    /// First-run UX: a single confirmation dialog summarizes everything that
    /// will be downloaded; user approves once, all installs run, AssetDatabase
    /// refreshes. Subsequent loads with an up-to-date checkout are a no-op.
    /// </summary>
    [InitializeOnLoad]
    public static class DependencyInstaller
    {
        const string LogTag       = "[UGM.SaveSystem]";
        const string EditorAsmdef = "UGM.SaveSystem.Editor.asmdef";

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
            summary.AppendLine($"\n저장 위치: {DefaultDestinationRoot}");

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

        // ── Package-root + manifest discovery ────────────────────────────────

        /// <summary>
        /// Where downloaded DLLs land — a mutable, user-side location that
        /// survives UPM upgrades and is auto-imported by Unity.
        /// </summary>
        public const string DefaultDestinationRoot = "Assets/Plugins/UGM/SaveSystem";

        /// <summary>
        /// Find the package root by locating our Editor asmdef. Returns null if
        /// the asmdef can't be found (project not yet imported, or someone
        /// renamed the asmdef).
        /// </summary>
        static string FindPackageRoot()
        {
            var guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/" + EditorAsmdef, StringComparison.Ordinal)
                    || path.EndsWith("\\" + EditorAsmdef, StringComparison.Ordinal))
                {
                    var editorDir   = Path.GetDirectoryName(path);          // <root>/Editor
                    var packageRoot = Path.GetDirectoryName(editorDir);     // <root>
                    return packageRoot?.Replace('\\', '/');
                }
            }
            return null;
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
            var root = FindPackageRoot();
            if (root == null)
            {
                // Don't spam — early in import the asmdef may not be visible yet.
                return null;
            }
            var manifestPath = root + "/dependencies.json";
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"{LogTag} Manifest not found at {manifestPath}; skipping dependency install.");
                return null;
            }
            try
            {
                var json = File.ReadAllText(manifestPath);
                return JsonUtility.FromJson<DependencyManifest>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} Failed to parse {manifestPath}: {e.Message}");
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
                    if (string.IsNullOrEmpty(dep.package))
                        throw new Exception($"nuget entry '{dep.id}' missing 'package' field.");
                    return new ResolvedInstall
                    {
                        Entry        = dep,
                        DownloadUrl  = $"https://www.nuget.org/api/v2/package/{dep.package}/{dep.version}",
                        ExtractPath  = dep.extract,
                        Destination  = ResolveDestination(dep.destination, dep.extract),
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
                        Destination  = ResolveDestination(pf.destination, pf.extract),
                    };

                default:
                    Debug.LogError($"{LogTag} Unknown dependency kind '{dep.kind}' for entry '{dep.id}'.");
                    return null;
            }
        }

        /// <summary>
        /// Take the manifest's destination string. If empty, fall back to
        /// <see cref="DefaultDestinationRoot"/> + the file name from the
        /// extract path. This lets manifests omit per-entry destination
        /// boilerplate when the default location is fine.
        /// </summary>
        static string ResolveDestination(string declared, string extractPath)
        {
            if (!string.IsNullOrEmpty(declared)) return declared;
            var fileName = Path.GetFileName(extractPath);
            if (string.IsNullOrEmpty(fileName))
                throw new Exception("Cannot infer destination — both 'destination' and 'extract' are empty.");
            return DefaultDestinationRoot + "/" + fileName;
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
                    wc.Headers[HttpRequestHeader.UserAgent] = "UGM.SaveSystem/DependencyInstaller";
                    wc.DownloadFile(r.DownloadUrl, temp);
                }

                // Both .nupkg and the GitHub release archives are zip files.
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

            // Filename match anywhere in the archive (handles "release-folder/leaf.dll").
            var leaf = Path.GetFileName(wanted);
            return zip.Entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FullName), leaf, StringComparison.OrdinalIgnoreCase));
        }
    }
}
