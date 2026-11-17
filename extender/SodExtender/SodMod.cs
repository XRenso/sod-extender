using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SodExtender
{
    /// <summary>Base class of a code mod. A mod DLL has exactly one subclass.
    /// The extender applies all [HarmonyPatch] classes of the mod before OnLoad.</summary>
    public abstract class SodMod
    {
        /// <summary>Mod name from ModSettings.txt.</summary>
        public string Name { get; internal set; }

        /// <summary>Unique key of this mod in the session (see ModRecord.Key). Equals the name for most mods.</summary>
        public string Key { get; internal set; }

        /// <summary>Folder of the mod (local or Workshop). Use it to find the mod's own files.</summary>
        public string Directory { get; internal set; }

        /// <summary>Log source named after the mod.</summary>
        public ManualLogSource Log { get; internal set; }

        /// <summary>Harmony instance of the mod. The extender removes its patches if the mod fails to load.</summary>
        public Harmony Harmony { get; internal set; }

        /// <summary>Settings file of the mod: BepInEx/config/SodMods/&lt;mod name&gt;.cfg. Created before OnLoad.
        /// Declare settings with Config.Bind or Setting(). Changes are saved to disk at once.</summary>
        public ConfigFile Config { get; internal set; }

        /// <summary>Declares a setting. Reads the saved value, or writes the default into the .cfg file.</summary>
        protected ConfigEntry<T> Setting<T>(string key, T defaultValue, string description, string section = "General")
            => Config.Bind(section, key, defaultValue, description);

        /// <summary>Save data of this mod, stored in a .sodmods file next to the game's save file.
        /// It is loaded before OnGameStarted; it is empty in a new game. Other mods cannot see it.</summary>
        public ModSaveData SaveData => SaveStore.For(Key ?? Name);

        /// <summary>Runs once when the game has listed its mods. An exception here marks the mod as Failed.</summary>
        public virtual void OnLoad() { }

        /// <summary>Gameplay is ready, after a save load or a new game.</summary>
        public virtual void OnGameStarted(bool isNewGame) { }

        /// <summary>The current game ends: the player loads another save or starts a new game from the menu.
        /// OnGameStarted for the next game follows. Opening the menu (Esc) during play does NOT call this:
        /// the game is still alive behind the menu. Use it to drop per-game state.</summary>
        public virtual void OnMainMenu() { }

        /// <summary>The in-game hour changed (0-23). Not called for the first frame after the game starts.</summary>
        public virtual void OnHourChanged(int hour) { }

        /// <summary>The in-game date changed. Not called for the first frame after the game starts.</summary>
        public virtual void OnDayChanged() { }

        /// <summary>The game was paused (true) or resumed (false). Called only on a real change.</summary>
        public virtual void OnPauseChanged(bool paused) { }

        /// <summary>The game is about to save. Put your state into SaveData. The extender writes the file
        /// at this moment, just before the game's own save finishes (there is no reliable "after save" point).
        /// An exception here counts against the mod; the save itself is not affected.</summary>
        public virtual void OnBeforeSave() { }
    }
}
