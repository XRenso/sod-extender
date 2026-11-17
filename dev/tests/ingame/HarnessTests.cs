using System.Collections;
using UnityEngine;
using static SodDevBridge.Api;

/// <summary>Checks that the harness itself works. Every public static Test* method is one test.
/// A test is void, or returns IEnumerator to wait across frames (yield return Wait(1f)).</summary>
public static class HarnessTests
{
    public static void TestRunInBackgroundIsOn()
    {
        Check(Application.runInBackground, "runInBackground must be on while the harness drives the game");
    }

    public static void TestGameIsMuted()
    {
        FMOD.Studio.Bus master;
        FMODUnity.RuntimeManager.StudioSystem.getBus("bus:/", out master);
        bool muted;
        master.getMute(out muted);
        Check(muted, "the game must stay silent during tests");
    }

    public static IEnumerator TestCoroutineAdvancesFrames()
    {
        var start = Time.frameCount;
        yield return Wait(0.5f);
        Check(Time.frameCount > start, "no frames passed");
    }
}
