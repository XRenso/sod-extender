using SodExtender;

namespace ExampleMod.Rules
{
    // PURE FILE (src/Rules): only SodExtender's pure interfaces, no game or BepInEx types.
    // The offline tests compile this folder: py dev/tools/sod.py unit
    public static class StartCounter
    {
        public const string Key = "GameStarts";

        /// <summary>Counts one more start of this save and returns the new count.</summary>
        public static int Record(IKeyValueStore saveData)
        {
            var starts = saveData.Get(Key, 0) + 1;
            saveData.Set(Key, starts);
            return starts;
        }

        public static string Message(int starts) => $"This save was started {starts} time(s)";
    }
}
