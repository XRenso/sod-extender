using HarmonyLib;

namespace TestBadPatch
{
    // Test fixture: the same failure a game update causes when it removes a method.
    [HarmonyPatch(typeof(Player), "MethodRemovedByGameUpdate")]
    public static class MissingMethodPatch
    {
        public static void Postfix() { }
    }
}
