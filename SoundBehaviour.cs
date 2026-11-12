using System;
using UnityEngine;

namespace CustomKillSoundFramework
{
    /// <summary>
    /// Persistent component attached through BepInEx's BasePlugin.AddComponent&lt;T&gt;().
    /// All mutable state is static, keeping IL2CPP type injection simple (see fov-mod).
    /// </summary>
    public class SoundBehaviour : MonoBehaviour
    {
        public SoundBehaviour(IntPtr ptr) : base(ptr) { }

        private void Awake()
        {
            // Pre-decode all wav files so the first shot has no loading hitch.
            // SoundBank.EnsureReady() still works as a lazy fallback.
            if (Diag.Enabled)
                Diag.Log("Plugin component alive; pre-decoding sounds");
            try { SoundBank.Initialize(); }
            catch (Exception e) { CksfPlugin.Logger?.LogWarning("[CKSF] Preload failed: " + e.Message); }
        }

        private void LateUpdate()
        {
            try { SoundEvents.Tick(); }
            catch (Exception e) { Hooks.Warn("Tick", e); }
        }
    }
}
