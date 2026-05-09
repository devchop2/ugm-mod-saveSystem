# Changelog

All notable changes to this module are documented here.
Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-05-09

### Added
- Initial release of `com.chopchopgames.ugm.savesystem`.
- 3-stage pipeline: `ISaveAggregator` → `ISaveCodec` → `IStorageProvider`.
- `SaveManager` static class as the primary user entry point — `Register`, `SaveAsync`, `LoadAsync`, `DeleteAsync`, `ExistsAsync`, all accept an optional file name.
- `MessagePackCodec` (default) using ContractlessStandardResolver — no attributes required on user data classes for Mono builds.
- `UnityResolver` for Vector2/3/4, Quaternion, Color, Color32.
- `DefaultAggregator` with optional dirty-slot caching (skip re-encoding unchanged slots).
- `LocalFileProvider` writing to `Application.persistentDataPath` with crash-safe atomic temp+rename.
- `SaveFileHeader` — UGMS magic + format version + codec id + body length + CRC32 trailer for corruption detection and codec auto-detection.
- `AutoSaveScheduler` — drop-in MonoBehaviour for interval saves with `BeforeSave`/`AfterSave`/`SaveFailed` events.
- Forward-compat: unknown slots from a future module version are preserved on round-trip.
- `DependencyInstaller` — first-import dialog auto-downloads MessagePack DLLs from NuGet to `Assets/Plugins/UGM/SaveSystem/`.
- README contains the full quick-start (Inventory + PlayerStats + SaveBoot patterns) — no Samples~ folder needed.
