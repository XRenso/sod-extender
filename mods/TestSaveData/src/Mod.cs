using SodExtender;

namespace TestSaveData
{
    // Test fixture: writes a fixed value into its section when the game saves.
    public class Mod : SodMod
    {
        public override void OnBeforeSave() => SaveData.Set("answer", 42);
    }
}
