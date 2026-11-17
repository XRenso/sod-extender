using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SodExtender
{
    [BepInPlugin("sod.extender", "SOD Extender", Version)]
    public class Plugin : BasePlugin
    {
        public const string Version = "0.2.0";

        /// <summary>Config sod.extender: the "SOD Extender" button on the Mods screen.</summary>
        internal static bool PanelEnabled { get; private set; }

        /// <summary>Config sod.extender [Guard] DeferUnpatch, default true since 2026-11-17: see PatchGuard.Tick.</summary>
        internal static bool DeferUnpatch { get; private set; }

        public override void Load()
        {
            Extender.Init(Log);
            PanelEnabled = Config.Bind("Panel", "Enabled", true,
                "Show the SOD Extender status button on the Mods screen.").Value;
            DeferUnpatch = Config.Bind("Guard", "DeferUnpatch", true,
                "Remove the patches of a mod turned off at run time only after 5 s of calm gameplay, not at once. " +
                "An immediate unpatch during a save load could freeze the game. Set false for the old behaviour.").Value;
            new Harmony("sod.extender").PatchAll(typeof(Plugin).Assembly);
            ClassInjector.RegisterTypeInIl2Cpp<ExtenderBehaviour>();
            var host = new GameObject("SodExtender");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<ExtenderBehaviour>();
            Log.LogInfo("SOD Extender " + Version + " ready");
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
