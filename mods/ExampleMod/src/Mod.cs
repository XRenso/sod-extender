using BepInEx.Configuration;
using ExampleMod.Rules;
using SodExtender;

namespace ExampleMod
{
    // The extender applies every [HarmonyPatch] class of this DLL, then calls OnLoad.
    public class Mod : SodMod
    {
        // A setting: saved in BepInEx/config/SodMods/ExampleMod.cfg, changed in the Mods menu.
        public static ConfigEntry<bool> LogLine;

        public override void OnLoad()
        {
            LogLine = Setting("EnabledLogLine", true, "Write a line to the log when the mod loads.");
            if (LogLine.Value) Log.LogInfo("ExampleMod loaded");
        }

        // Save data: count how many times this save was started. SaveData is loaded before OnGameStarted.
        // Thin adapter: the rule is in src/Rules/StartCounter.cs and has offline tests (sod.py unit).
        public override void OnGameStarted(bool isNewGame)
        {
            var starts = StartCounter.Record(SaveData);
            Log.LogInfo(StartCounter.Message(starts));
        }
    }
}
