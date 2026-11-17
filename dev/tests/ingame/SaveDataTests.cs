using System.Collections;
using System.IO;
using System.Linq;
using SodExtender;
using UnityEngine;
using static SodDevBridge.Api;

/// <summary>Needs fixtures TestSaveData and TestThrowSave deployed. Uses only a temp folder under
/// persistentDataPath/SodExtender/test-saves, never the game's Save folder.
/// Not verified here: the real CaptureSaveStateAsync path, Steam Cloud, the real DeleteSave.</summary>
public static class SaveDataTests
{
    private static string Dir()
    {
        var d = Path.Combine(Application.persistentDataPath, "SodExtender", "test-saves");
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Cleanup()
    {
        var d = Path.Combine(Application.persistentDataPath, "SodExtender", "test-saves");
        if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private static ModRecord Find(string name) => Extender.Mods.FirstOrDefault(m => m.Name == name);

    public static void TestPathMapping()
    {
        Check(SavePaths.ToModsPath(@"C:\S\a.sodb") == @"C:\S\a.sodmods", ".sodb");
        Check(SavePaths.ToModsPath(@"C:\S\a.sod") == @"C:\S\a.sodmods", ".sod");
        Check(SavePaths.ToModsPath(@"C:\S\A.SODB") == @"C:\S\A.sodmods", "upper case");
        Check(SavePaths.ToModsPath("Benjamin Delgado Franklyn 2023-30-11-18-17-23.30.sodb") ==
              "Benjamin Delgado Franklyn 2023-30-11-18-17-23.30.sodmods", "dots and spaces");
        Check(SavePaths.ToModsPath(@"C:\S\Сохранение. Иван.sodb") == @"C:\S\Сохранение. Иван.sodmods", "cyrillic");
        Check(SavePaths.ToModsPath(@"C:\S\a.txt") == null, "other extension");
        Check(SavePaths.ToModsPath(@"C:\S\a") == null, "no extension");
        Check(SavePaths.ToModsPath("") == null && SavePaths.ToModsPath(null) == null, "empty");
        Check(SavePaths.NormalizeGamePath("C:/S/a.sod") == @"C:\S\a.sod", "slashes");
    }

    public static void TestRoundTrip()
    {
        var mod = Find("TestSaveData");
        Check(mod != null && mod.State == ModState.Loaded, "fixture TestSaveData is not loaded");
        try
        {
            var save = Path.Combine(Dir(), "Иван Test 1.30.sodb");
            mod.Instance.SaveData.Clear();
            mod.Instance.SaveData.Set("text", "привет");
            mod.Instance.SaveData.Set("list", new[] { 1, 2, 3 });
            SaveStore.OnSave(save);

            var file = Path.ChangeExtension(save, ".sodmods");
            Check(File.Exists(file), "no .sodmods file");
            Check(!File.Exists(file + ".tmp"), "temp file left behind");
            var text = File.ReadAllText(file);
            Check(text.Contains("\"format\": 1") && text.Contains("\"TestSaveData\""), "bad file: " + text);

            mod.Instance.SaveData.Clear();
            Check(!mod.Instance.SaveData.Has("answer"), "Clear failed");
            SaveStore.OnLoad(save);
            Check(mod.Instance.SaveData.Get("answer", 0) == 42, "answer not restored (set by OnBeforeSave)");
            Check(mod.Instance.SaveData.Get("text", "") == "привет", "text not restored");
            Check(mod.Instance.SaveData.Get("list", new int[0]).Sum() == 6, "list not restored");
            Check(mod.Instance.SaveData.Get("missing", 7) == 7, "fallback");
            Check(mod.Instance.SaveData.Get("text", 5) == 5, "wrong type must give the fallback");

            // New game and missing file give empty data.
            SaveStore.OnLoad(null);
            Check(!mod.Instance.SaveData.Has("answer"), "new game must be empty");
            SaveStore.OnLoad(Path.Combine(Dir(), "nothing.sodb"));
            Check(!mod.Instance.SaveData.Has("answer"), "missing file must be empty");
        }
        finally { mod.Instance.SaveData.Clear(); Cleanup(); }
    }

    public static void TestUnknownSectionSurvives()
    {
        var mod = Find("TestSaveData");
        Check(mod != null, "fixture TestSaveData is not loaded");
        try
        {
            var save = Path.Combine(Dir(), "ghost.sod");
            File.WriteAllText(Path.ChangeExtension(save, ".sodmods"),
                "{\"format\":1,\"mods\":{\"GhostMod\":{\"level\":9,\"name\":\"x\"}}}");
            SaveStore.OnLoad(save);
            SaveStore.OnSave(save);
            var text = File.ReadAllText(Path.ChangeExtension(save, ".sodmods"));
            Check(text.Contains("\"GhostMod\"") && text.Contains("\"level\": 9"), "ghost section lost: " + text);
            Check(text.Contains("\"TestSaveData\""), "own section missing: " + text);
        }
        finally { SaveStore.OnLoad(null); Cleanup(); }
    }

    public static IEnumerator TestThrowingOnBeforeSave()
    {
        var thrower = Find("TestThrowSave");
        Check(thrower != null && thrower.State == ModState.Loaded, "fixture TestThrowSave must be Loaded at start");
        var type = thrower.Instance.GetType();
        var before = (int)type.GetField("Calls").GetValue(null);
        var save = Path.Combine(Dir(), "throw.sodb");
        try
        {
            for (var i = 0; i < 3; i++) SaveStore.OnSave(save);
            Check((int)type.GetField("Calls").GetValue(null) == before + 3, "thrower was not called 3 times");
            var text = File.ReadAllText(Path.ChangeExtension(save, ".sodmods"));
            Check(text.Contains("\"TestSaveData\""), "file not written for the other mod: " + text);
            yield return Wait(0.5f); // PatchGuard.Tick turns the mod off from Update
            Check(thrower.State == ModState.Failed, "thrower was not charged and turned off");
            Check(Find("TestSaveData").State == ModState.Loaded, "other mod was affected");
        }
        finally { SaveStore.OnLoad(null); Cleanup(); }
    }

    public static void TestDeleteRemovesFileOnlyIfSaveGone()
    {
        try
        {
            var save = Path.Combine(Dir(), "del.sodb");
            var mods = Path.ChangeExtension(save, ".sodmods");
            File.WriteAllText(save, "x");
            File.WriteAllText(mods, "{}");
            SaveStore.OnDeleted(save);
            Check(File.Exists(mods), "mod data deleted while the save still exists");
            File.Delete(save);
            SaveStore.OnDeleted(save);
            Check(!File.Exists(mods), "mod data not deleted after the save is gone");
            SaveStore.OnDeleted(save); // no file: no error
        }
        finally { Cleanup(); }
    }

    public static void TestCorruptFileIsIgnored()
    {
        try
        {
            var save = Path.Combine(Dir(), "bad.sodb");
            File.WriteAllText(Path.ChangeExtension(save, ".sodmods"), "not json {{");
            SaveStore.OnLoad(save); // must not throw
            File.WriteAllText(Path.ChangeExtension(save, ".sodmods"), "{\"format\":99,\"mods\":{}}");
            SaveStore.OnLoad(save);
            Check(true, "");
        }
        finally { SaveStore.OnLoad(null); Cleanup(); }
    }
}
