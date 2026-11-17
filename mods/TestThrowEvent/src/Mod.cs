using System;
using SodExtender;

namespace TestThrowEvent
{
    // Test fixture: loads well, then throws in every pause event.
    public class Mod : SodMod
    {
        public static int Calls;
        public override void OnPauseChanged(bool paused)
        {
            Calls++;
            throw new Exception("boom in event");
        }
    }
}
