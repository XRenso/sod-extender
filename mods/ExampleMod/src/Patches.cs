using HarmonyLib;

namespace ExampleMod
{
    // The whole mod: one patch class. The extender applies it, so the mod needs no setup code.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    public static class PlayerUpdatePatch
    {
        public static int Calls;
        public static void Postfix() => Calls++;
    }
}
