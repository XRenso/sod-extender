using System;
using HarmonyLib;
using SodExtender;

namespace TestBrokenLoad
{
    // Test fixture: applies a patch, then fails. The extender must remove the patch.
    public class Mod : SodMod
    {
        public override void OnLoad() => throw new Exception("boom in OnLoad");
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    public static class MustBeRemovedPatch
    {
        public static int Calls;
        public static void Postfix() => Calls++;
    }
}
