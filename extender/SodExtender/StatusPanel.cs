using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SodExtender
{
    /// <summary>The "SOD Extender" button on the game's Mods screen and the status panel it opens.
    /// The button is a clone of the game's Back button; the panel is a clone of the game's Mods panel,
    /// so fonts and sprites match. Every entry point catches and logs: a failure here must never
    /// break the game's own Mods screen.</summary>
    public static class StatusPanel
    {
        public const string ButtonName = "SodExtenderButton";
        public const string PanelName = "SodExtenderPanel";
        private const float CheckSeconds = 1f;

        private static readonly List<GameObject> _hidden = new();
        private static float _nextCheck;
        private static bool _broken;

        /// <summary>Called every frame; adds the button once the Mods screen exists.</summary>
        internal static void Tick()
        {
            if (_broken || !Plugin.PanelEnabled || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckSeconds;
            try
            {
                EnsureButton();
            }
            catch (Exception ex)
            {
                _broken = true; // do not retry every second
                Extender.LogError("Status panel button failed, panel is off: " + ex);
            }
        }

        public static RectTransform FindModsPanel()
        {
            var menu = MainMenuController.Instance;
            if (menu == null || menu.components == null) return null;
            for (var i = 0; i < menu.components.Count; i++)
            {
                var c = menu.components[i];
                if (c != null && c.component == MainMenuController.Component.mods) return c.rect;
            }
            return null;
        }

        /// <summary>The button object, or null if it is not there.</summary>
        public static Transform FindButton()
        {
            var panel = FindModsPanel();
            var area = panel == null ? null : panel.Find("ButtonArea");
            return area == null ? null : area.Find(ButtonName);
        }

        public static Transform FindPanel()
        {
            var panel = FindModsPanel();
            return panel == null ? null : panel.Find(PanelName);
        }

        private static void EnsureButton()
        {
            var panel = FindModsPanel();
            if (panel == null) return;
            var area = panel.Find("ButtonArea");
            var back = area == null ? null : area.Find("Back");
            if (back == null || area.Find(ButtonName) != null) return;

            var go = UnityEngine.Object.Instantiate(back.gameObject, area);
            go.name = ButtonName;
            var rect = go.GetComponent<RectTransform>();
            var backRect = back.GetComponent<RectTransform>();
            rect.anchoredPosition = backRect.anchoredPosition + new Vector2(backRect.sizeDelta.x + 16f, 0f);
            Rewire(go, "SOD Extender", OpenPanel);
        }

        /// <summary>Turns a cloned game button into ours: no inherited actions, our label, our click.</summary>
        private static void Rewire(GameObject go, string label, Action onClick)
        {
            var bc = go.GetComponent<ButtonController>();
            var button = go.GetComponent<Button>();
            for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            button.onClick.RemoveAllListeners();
            bc.useAutomaticText = false;
            bc.text.text = label;
            button.onClick.AddListener((UnityAction)(() =>
            {
                try { onClick(); }
                catch (Exception ex) { Extender.LogError("Status panel click failed: " + ex); }
            }));
        }

        public static void OpenPanel()
        {
            var host = FindModsPanel();
            if (host == null) return;
            ClosePanel();

            var clone = UnityEngine.Object.Instantiate(host.gameObject, host);
            clone.name = PanelName;
            clone.GetComponent<RectTransform>().anchoredPosition = Vector2.zero; // the copy kept the host's offset
            try
            {
                Build(clone.transform);
                // The game's own parts of the screen go away while ours is open; ClosePanel brings them back.
                for (var i = 0; i < host.childCount; i++)
                {
                    var child = host.GetChild(i).gameObject;
                    if (child == clone || !child.activeSelf) continue;
                    child.SetActive(false);
                    _hidden.Add(child);
                }
            }
            catch
            {
                UnityEngine.Object.Destroy(clone); // never leave a half-built panel over the game's screen
                throw;
            }
        }

        public static void ClosePanel()
        {
            foreach (var g in _hidden)
                if (g != null) g.SetActive(true);
            _hidden.Clear();
            var old = FindPanel();
            if (old != null)
            {
                old.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(old.gameObject);
            }
        }

        private static void Build(Transform root)
        {
            // The clone is a full copy of the Mods screen: drop what is not ours.
            var ownButton = root.Find("ButtonArea/" + ButtonName);
            if (ownButton != null) UnityEngine.Object.Destroy(ownButton.gameObject);
            var nested = root.Find(PanelName);
            if (nested != null) UnityEngine.Object.Destroy(nested.gameObject);
            var side = root.Find("SettingsButtonsArea");
            if (side != null) side.gameObject.SetActive(false);

            SetText(root.Find("Header/PanelTitle"), PanelText.Title(Plugin.Version));
            var info = SetText(root.Find("Header/ModsInfoText"), "");
            Rewire(root.Find("ButtonArea/Back").gameObject, "Back", ClosePanel);

            var content = root.Find("Components/Scroll View/Viewport/Content");
            for (var i = content.childCount - 1; i >= 0; i--)
            {
                content.GetChild(i).gameObject.SetActive(false);
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            }
            // The game's list leaves room for its side buttons; ours uses the full width.
            var area = root.Find("Components").GetComponent<RectTransform>();
            area.anchoredPosition = new Vector2(0f, area.anchoredPosition.y);
            area.sizeDelta = new Vector2(-128f, area.sizeDelta.y);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, contentRect.sizeDelta.y);
            contentRect.anchoredPosition = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 8f;
                layout.padding = new RectOffset(16, 16, 8, 8);
            }
            if (content.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            var mods = Extender.Mods;
            var failed = mods.Count(m => m.State == ModState.Failed);
            info.text = PanelText.Summary(mods.Count - failed, failed, Extender.Conflicts.Count);
            var back = root.Find("ButtonArea/Back");

            foreach (var m in mods)
            {
                var loaded = m.State == ModState.Loaded;
                var count = m.Config == null ? 0 : m.Config.Keys.Count();
                AddRow(content, info, PanelText.ModBlock(m.Name, loaded, m.Error, count), 22f);
                if (loaded && m.Config != null)
                    foreach (var def in m.Config.Keys)
                    {
                        var entry = m.Config[def];
                        if (entry.BoxedValue is bool)
                            AddToggle(content, back, def.Section + " / " + def.Key, entry);
                        else
                            AddRow(content, info, "<color=" + PanelText.DimColour + ">    " + PanelText.Safe(def.Section + " / " + def.Key + " = " + entry.BoxedValue) + "</color>", 18f);
                    }
            }
            AddRow(content, info, "<b>Patch overlaps</b>", 22f);
            foreach (var line in PanelText.OverlapLines(Extender.Conflicts))
                AddRow(content, info, line, 18f);
        }

        /// <summary>A bool setting as a game button; a click flips it and BepInEx saves the .cfg at once.</summary>
        private static void AddToggle(Transform content, Transform template, string label, BepInEx.Configuration.ConfigEntryBase entry)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, content);
            go.name = "Toggle";
            var le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredHeight = 40f;
                le.minHeight = 40f;
            }
            var bc = go.GetComponent<ButtonController>();
            Action refresh = () => bc.text.text = PanelText.ToggleLabel(label, (bool)entry.BoxedValue);
            Rewire(go, "", () =>
            {
                entry.BoxedValue = !(bool)entry.BoxedValue;
                refresh();
            });
            refresh();
        }

        private static TMP_Text SetText(Transform t, string text)
        {
            var auto = t.GetComponent<MenuAutoTextController>();
            if (auto != null)
            {
                auto.enabled = false;
                UnityEngine.Object.Destroy(auto);
            }
            var tmp = t.GetComponent<TMP_Text>();
            tmp.text = text;
            return tmp;
        }

        /// <summary>A text row with the game's font, taken from an existing label.</summary>
        private static void AddRow(Transform content, TMP_Text style, string text, float size)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(content, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = style.font;
            tmp.fontSharedMaterial = style.fontSharedMaterial;
            tmp.fontSize = size;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.text = text;
        }
    }
}
