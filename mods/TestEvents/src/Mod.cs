using System.Collections.Generic;
using SodExtender;

namespace TestEvents
{
    // Test fixture: records every event callback.
    public class Mod : SodMod
    {
        public static readonly List<string> Seen = new();
        public override void OnGameStarted(bool isNewGame) => Seen.Add("GameStarted:" + isNewGame);
        public override void OnMainMenu() => Seen.Add("MainMenu");
        public override void OnHourChanged(int hour) => Seen.Add("Hour:" + hour);
        public override void OnDayChanged() => Seen.Add("Day");
        public override void OnPauseChanged(bool paused) => Seen.Add("Pause:" + paused);
    }
}
