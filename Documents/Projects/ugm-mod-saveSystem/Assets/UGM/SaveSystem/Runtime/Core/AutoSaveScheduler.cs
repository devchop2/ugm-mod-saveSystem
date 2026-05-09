using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Drop-in MonoBehaviour that calls <see cref="SaveManager.SaveAsync"/> on
    /// a fixed interval. Tuned for "autosave every 30s ~ 1min" gameplay loops:
    /// the heavy work (encode + disk IO) runs off the main thread inside
    /// SaveAsync, so a successful tick costs only a slot getter pass on the
    /// main thread.
    ///
    /// Lifecycle hooks save once on app pause and once on app quit so a player
    /// who suspends or kills the game between ticks doesn't lose progress.
    /// Disable in inspector if your game already handles those cases.
    /// </summary>
    public class AutoSaveScheduler : MonoBehaviour
    {
        [Tooltip("Save period in seconds. Set to 0 or negative to disable the timer (manual saves only).")]
        [SerializeField] private float intervalSeconds = 30f;

        [Tooltip("Optional file name override. If blank, SaveManager.DefaultFileName is used.")]
        [SerializeField] private string fileName = "";

        [Tooltip("If true, also save when the application pauses (mobile background).")]
        [SerializeField] private bool saveOnPause = true;

        [Tooltip("If true, also save on application quit. No effect on platforms that don't fire OnApplicationQuit (e.g. iOS background-kill).")]
        [SerializeField] private bool saveOnQuit = true;

        private float _accum;
        private bool _saving;
        private bool _hasSavedOnce;

        /// <summary>Fired right before the auto-save begins. Game can MarkDirty here.</summary>
        public event Action BeforeSave;

        /// <summary>Fired after a successful auto-save returns from disk write.</summary>
        public event Action AfterSave;

        /// <summary>Fired when an auto-save throws. The exception is forwarded; subscribe to log/report.</summary>
        public event Action<Exception> SaveFailed;

        /// <summary>True if at least one save completed since this scheduler was enabled.</summary>
        public bool HasSavedOnce => _hasSavedOnce;

        private void Update()
        {
            if (intervalSeconds <= 0f || _saving) return;

            _accum += Time.unscaledDeltaTime;
            if (_accum >= intervalSeconds)
            {
                _accum = 0f;
                _ = TrySaveAsync();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && saveOnPause && !_saving)
                _ = TrySaveAsync();
        }

        private void OnApplicationQuit()
        {
            if (!saveOnQuit) return;
            // Best-effort synchronous wait — we're tearing down the app anyway.
            try
            {
                BeforeSave?.Invoke();
                SaveManager.SaveAsync(EffectiveFileName()).GetAwaiter().GetResult();
                AfterSave?.Invoke();
                _hasSavedOnce = true;
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(ex);
                Debug.LogException(ex);
            }
        }

        /// <summary>Force an immediate save. Coalesces with any in-flight auto-save.</summary>
        [ContextMenu("Save Now")]
        public async void SaveNow() => await TrySaveAsync();

        private async Task TrySaveAsync()
        {
            _saving = true;
            try
            {
                BeforeSave?.Invoke();
                await SaveManager.SaveAsync(EffectiveFileName());
                AfterSave?.Invoke();
                _hasSavedOnce = true;
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(ex);
                Debug.LogException(ex);
            }
            finally
            {
                _saving = false;
            }
        }

        private string EffectiveFileName() => string.IsNullOrEmpty(fileName) ? null : fileName;
    }
}
