using HarmonyLib;

namespace TestOverlap
{
    // Test fixture: a healthy second mod on the same method as ExampleMod, so the overlap report has a stable case.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    public static class SecondPatch
    {
        public static void Postfix() { }
    }
}
