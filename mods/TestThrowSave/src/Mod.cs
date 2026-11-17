using System;
using SodExtender;

namespace TestThrowSave
{
    // Test fixture: loads well, then throws in every OnBeforeSave.
    public class Mod : SodMod
    {
        public static int Calls;
        public override void OnBeforeSave()
        {
            Calls++;
            throw new Exception("boom in save");
        }
    }
}
