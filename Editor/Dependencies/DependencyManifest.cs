using System;
using System.Collections.Generic;

namespace UGM.SaveSystem.EditorTools.Dependencies
{
    /// <summary>
    /// JsonUtility-friendly model of <c>dependencies.json</c>.
    ///
    /// The schema is intentionally one flat list with a <c>kind</c> discriminator
    /// rather than separate lists per source, so adding a new source type later
    /// (direct URL, Git submodule helper, etc.) is one new <c>kind</c> string and
    /// one new branch in the installer — not a schema change.
    ///
    /// Fields not relevant to a dependency's <c>kind</c> are simply left empty
    /// (JsonUtility round-trips empty strings/lists without complaint).
    /// </summary>
    [Serializable]
    public class DependencyManifest
    {
        public int schemaVersion = 1;
        public List<DependencyEntry> dependencies = new List<DependencyEntry>();
    }

    [Serializable]
    public class DependencyEntry
    {
        /// <summary>Stable human ID — used in dialogs, logs, and version sidecars.</summary>
        public string id;

        /// <summary>Pinned version. Reinstall is triggered when the on-disk sidecar disagrees.</summary>
        public string version;

        /// <summary>"nuget" | "github-release" — see DependencyInstaller for branches.</summary>
        public string kind;

        // ── nuget fields (single-platform DLL) ──────────────────────────────
        /// <summary>NuGet package id, e.g. "Google.FlatBuffers".</summary>
        public string package;
        /// <summary>Path inside the .nupkg to extract, e.g. "lib/netstandard2.1/Google.FlatBuffers.dll".</summary>
        public string extract;
        /// <summary>Destination path under the project root, e.g. "Assets/Plugins/Google.FlatBuffers.dll".</summary>
        public string destination;

        // ── github-release fields (per-platform binaries) ───────────────────
        /// <summary>"owner/name", e.g. "google/flatbuffers".</summary>
        public string repo;
        /// <summary>Release tag, e.g. "v23.5.26".</summary>
        public string tag;
        /// <summary>One entry per host platform we support.</summary>
        public List<DependencyPlatform> platforms = new List<DependencyPlatform>();
    }

    [Serializable]
    public class DependencyPlatform
    {
        /// <summary>Match against UnityEditor.Application.platform.ToString() — "WindowsEditor", "OSXEditor", "LinuxEditor".</summary>
        public string name;

        /// <summary>Asset filename inside the GitHub release.</summary>
        public string asset;

        /// <summary>Path inside the asset archive to extract.</summary>
        public string extract;

        /// <summary>Destination path under the project root.</summary>
        public string destination;
    }
}
