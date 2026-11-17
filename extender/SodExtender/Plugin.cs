using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SodExtender
{
    /// <summary>The extender core. Not a BepInEx plugin itself: the loader (SodExtender.Loader, GUID sod.extender)
    /// picks this DLL (bundled or from the Workshop) and calls <see cref="Start"/>. See extender-v1.md "Core from the Workshop".</summary>
    public static class Plugin
    {
        public const string Version = "0.3.0";

        /// <summary>The loader contract: Start(BasePlugin). Change only with a new loader; core.json "api" must match.</summary>
        public const int ApiMajor = 1;

        /// <summary>Config sod.extender: the "SOD Extender" button on the Mods screen.</summary>
        internal static bool PanelEnabled { get; private set; }

        /// <summary>Config sod.extender [Guard] DeferUnpatch, default true since 2026-11-17: see PatchGuard.Tick.</summary>
        internal static bool DeferUnpatch { get; private set; }

        /// <summary>Where this core came from, for the report ("bundled" or "workshop ..."). Set by the loader.</summary>
        public static string Source { get; private set; } = "bundled";

        private static bool _started;

        /// <summary>Called once by the loader from its BasePlugin.Load. Uses the host's config file and log.</summary>
        public static void Start(BasePlugin host, string source)
        {
            if (_started) return;
            _started = true;
            Source = source ?? "bundled";
            Extender.Init(host.Log);
            PanelEnabled = host.Config.Bind("Panel", "Enabled", true,
                "Show the SOD Extender status button on the Mods screen.").Value;
            DeferUnpatch = host.Config.Bind("Guard", "DeferUnpatch", true,
                "Remove the patches of a mod turned off at run time only after 5 s of calm gameplay, not at once. " +
                "An immediate unpatch during a save load could freeze the game. Set false for the old behaviour.").Value;
            new Harmony("sod.extender").PatchAll(typeof(Plugin).Assembly);
            ClassInjector.RegisterTypeInIl2Cpp<ExtenderBehaviour>();
            var go = new GameObject("SodExtender");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ExtenderBehaviour>();
            host.Log.LogInfo($"SOD Extender {Version} ready (core: {Source})");
        }
    }

    /// <summary>Runs when the game has finished listing local, mod.io and Workshop mods.</summary>
    [HarmonyPatch(typeof(ModLoader), nameof(ModLoader.GetModsComplete))]
    internal static class GetModsCompletePatch
    {
        private static void Postfix()
        {
            try
            {
                Extender.ScanGameMods();
            }
            catch (Exception ex)
            {
                // The extender must never break the game's own mod list.
                BepInEx.Logging.Logger.CreateLogSource("SOD Extender").LogError(ex);
            }
        }
    }
}
