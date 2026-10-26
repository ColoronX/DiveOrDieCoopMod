#if STEAMWORKS_NET
using System;
using System.Text;
using BepInEx.Logging;
using Steamworks;
using UnityEngine;

namespace DiveOrDieCoop.Networking.Steam
{
    [DisallowMultipleComponent]
    public sealed class SteamRuntime : MonoBehaviour
    {
        public static SteamRuntime Instance { get; private set; }
        public static bool IsInitialized => Instance != null && Instance._initialized;

        private static ManualLogSource _log;
        private bool _initialized;
        private SteamAPIWarningMessageHook_t _warningMessageHook;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".Steam");

            try
            {
                if (!Packsize.Test())
                {
                    _log.LogError("[Steamworks.NET] Packsize Test failed. Wrong architecture of Steamworks.NET.");
                }

                if (!DllCheck.Test())
                {
                    _log.LogError("[Steamworks.NET] DllCheck Test failed. Steamworks native binaries missing or version mismatch.");
                }

                _initialized = SteamAPI.Init();
                if (!_initialized)
                {
                    _log.LogWarning("[Steamworks.NET] SteamAPI.Init() failed. Ensure Steam is running and steam_appid.txt exists in the game folder.");
                    return;
                }

                _warningMessageHook = new SteamAPIWarningMessageHook_t(SteamAPIDebugHook);
                SteamClient.SetWarningMessageHook(_warningMessageHook);

                CSteamID steamId = SteamUser.GetSteamID();
                string personaName = SteamFriends.GetPersonaName();
                _log.LogInfo($"Steam initialized successfully as {steamId} ({personaName}).");
            }
            catch (Exception ex)
            {
                _initialized = false;
                _log.LogError($"Exception during Steam initialization: {ex}");
            }
        }

        private void Update()
        {
            if (!_initialized) return;

            SteamAPI.RunCallbacks();
            NetworkManager.Instance?.PumpSteamTransport();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            if (_initialized)
            {
                _initialized = false;
                try
                {
                    SteamAPI.Shutdown();
                }
                catch { }
            }

            Instance = null;
        }

        private static void SteamAPIDebugHook(int severity, StringBuilder debugText)
        {
            _log?.LogWarning($"[Steam Debug {severity}] {debugText}");
        }
    }
}
#endif