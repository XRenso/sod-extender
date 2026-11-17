using System;
using HarmonyLib;

namespace TestThrowPatch
{
    // Test fixture: a mod that loads well, then throws inside a patch at run time.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    public static class ThrowingPatch
    {
        public static int Calls;
        public static void Postfix()
        {
            Calls++;
            throw new Exception("boom in patch");
        }
    }
}
