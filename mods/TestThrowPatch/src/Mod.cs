using SodExtender;

namespace TestThrowPatch
{
    // The extender applies every [HarmonyPatch] class of this DLL, then calls OnLoad.
    public class Mod : SodMod
    {
        public override void OnLoad()
        {
            Log.LogInfo("TestThrowPatch loaded");
        }
    }
}
