using System.Collections;
using System.Linq;
using SodExtender;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SodDevBridge.Api;

/// <summary>The status panel on the game's Mods screen. Needs the fixtures ExampleMod, TestBrokenLoad and
/// TestOverlap deployed and sod.extender.cfg [Panel] Enabled = true. Works on the object tree, so it runs
/// from the menu and from gameplay; it does not switch the visible menu screen.</summary>
public static class PanelTests
{
    private static string AllText(Transform root) =>
        string.Join("\n", root.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text));

    private static System.Collections.Generic.List<GameObject> Children(Transform t)
    {
        var list = new System.Collections.Generic.List<GameObject>();
        for (var i = 0; i < t.childCount; i++) list.Add(t.GetChild(i).gameObject);
        return list;
    }

    private static IEnumerator OpenPanel()
    {
        yield return WaitUntil(() => StatusPanel.FindButton() != null, 5f);
        Check(StatusPanel.FindButton() != null, "no SOD Extender button on the Mods screen (is [Panel] Enabled true?)");
        StatusPanel.FindButton().GetComponent<Button>().onClick.Invoke();
        yield return Wait(0.3f);
        Check(StatusPanel.FindPanel() != null, "the button did not open the panel");
    }

    private static IEnumerator ClosePanel()
    {
        var back = StatusPanel.FindPanel().Find("ButtonArea/Back").GetComponent<Button>();
        back.onClick.Invoke();
        yield return Wait(0.3f);
    }

    public static void TestPanelTextRules()
    {
        var failed = PanelText.ModBlock("Bad", false, "Exception: <b>x</b>", 0);
        Check(failed.Contains("Failed") && failed.Contains(PanelText.FailedColour), "failed state is not red");
        Check(failed.Contains("<noparse>Exception: <b>x</b></noparse>"), "error text is not escaped");
        var ok = PanelText.ModBlock("Good", true, null, 2);
        Check(ok.Contains("Loaded") && ok.Contains(PanelText.LoadedColour) && ok.Contains("settings: 2"), "loaded line");
        Check(PanelText.OverlapLines(new string[0]).Single().Contains("No overlaps"), "empty overlap list");
        Check(PanelText.ToggleLabel("A / B", true).EndsWith("ON"), "toggle label");
    }

    public static IEnumerator TestButtonAndPanelList()
    {
        yield return OpenPanel();
        var text = AllText(StatusPanel.FindPanel());
        Check(text.Contains("SOD Extender " + Plugin.Version), "version missing");
        Check(text.Contains("ExampleMod"), "ExampleMod missing");
        Check(text.Contains("TestBrokenLoad"), "TestBrokenLoad missing");
        Check(text.Contains("TestOverlap"), "TestOverlap missing");
        Check(text.Contains("boom in OnLoad"), "error text of the failed mod missing");
        Check(text.Contains(PanelText.FailedColour) && text.Contains(PanelText.LoadedColour), "state colours missing");
        Check(text.Contains("Player.Update"), "overlap line missing");
        Check(text.Contains("EnabledLogLine"), "setting of ExampleMod missing");
        yield return ClosePanel();
        Check(StatusPanel.FindPanel() == null, "panel is still there after Back");
    }

    public static IEnumerator TestGameModsScreenIntact()
    {
        var host = StatusPanel.FindModsPanel();
        Check(host != null, "no Mods screen");
        var before = Children(host).Where(g => g.name != StatusPanel.PanelName)
            .ToDictionary(g => g.GetInstanceID(), g => g.activeSelf);
        yield return OpenPanel();
        // while open, the game's own parts are hidden; after closing every one is back as it was
        yield return ClosePanel();
        foreach (var g in Children(host))
            if (before.TryGetValue(g.GetInstanceID(), out var was))
                Check(g.activeSelf == was, "Mods screen part changed: " + g.name);
        foreach (var name in new[] { "SettingsButtonsArea/RefreshButton", "SettingsButtonsArea/BrowseSteamWorkshopButton", "ButtonArea/Back" })
        {
            var t = host.Find(name);
            Check(t != null && t.gameObject.activeSelf, "game button missing or hidden: " + name);
            Check(t.GetComponent<Button>().onClick.GetPersistentEventCount() > 0, "game button lost its action: " + name);
        }
        Check(ModController.Instance != null && ModController.Instance.applyButton != null && ModController.Instance.workshopButton != null,
            "ModController buttons are gone");
        Check(host.Find("ButtonArea/Back").GetComponent<Button>().onClick.GetPersistentEventCount() == 2,
            "the game's Back button was rewired");
    }

    public static IEnumerator TestBoolSettingToggle()
    {
        var entry = (BepInEx.Configuration.ConfigEntry<bool>)Extender.Mods.First(m => m.Name == "ExampleMod").Config["General", "EnabledLogLine"];
        var start = entry.Value;
        yield return OpenPanel();
        var toggle = StatusPanel.FindPanel().GetComponentsInChildren<ButtonController>(true).First(b => b.name == "Toggle");
        toggle.button.onClick.Invoke();
        Check(entry.Value == !start, "toggle did not flip the setting");
        Check(toggle.text.text.EndsWith(start ? "OFF" : "ON"), "toggle label not updated: " + toggle.text.text);
        toggle.button.onClick.Invoke();
        Check(entry.Value == start, "second click did not restore the setting");
        yield return ClosePanel();
    }
}
