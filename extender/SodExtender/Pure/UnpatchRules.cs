// PURE FILE: see Pure/SaveRules.cs.
namespace SodExtender
{
    /// <summary>Plain rule: when is the game stable enough to remove the patches of a turned-off mod.
    /// Removing patches of a hot method (Player.Update) at the start of a save load blocked the
    /// main thread in 3 of 11 loads (2026-11-08). So the unpatch waits for calm gameplay:
    /// the game is started, and no frame was long (a load blocks the main thread) for StableSeconds.</summary>
    public sealed class StableGate
    {
        public const float StableSeconds = 5f;
        public const float LongFrameSeconds = 0.5f;

        private float _calm;

        /// <summary>Seconds of calm gameplay so far.</summary>
        public float CalmSeconds => _calm;

        /// <summary>Call once per frame. True when the game was started and calm for StableSeconds.</summary>
        public bool Update(bool startedGame, float unscaledDeltaTime)
        {
            if (!startedGame || unscaledDeltaTime >= LongFrameSeconds || unscaledDeltaTime < 0f)
            {
                _calm = 0f;
                return false;
            }
            _calm += unscaledDeltaTime;
            return _calm >= StableSeconds;
        }
    }
}
