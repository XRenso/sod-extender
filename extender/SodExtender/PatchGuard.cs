using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SodExtender
{
    /// <summary>Catches exceptions from mod patches at run time. A patch that throws every frame
    /// would otherwise flood the log and skip the other mods' postfixes on the same method.</summary>
    internal static class PatchGuard
    {
        internal const int MaxErrors = 3;

        private sealed class Owner
        {
            public ModRecord Record;
            public Harmony Harmony;
            public int Errors;
            public string LastError;
        }

        private static readonly Dictionary<Assembly, Owner> _owners = new();
        private static readonly Dictionary<ModRecord, Owner> _byRecord = new();
        private static readonly HashSet<MethodBase> _guarded = new();
        private static readonly Queue<Owner> _toTurnOff = new();
        private static readonly Harmony _guard = new("sod.extender.guard");

        internal static void Register(Assembly assembly, ModRecord record, Harmony harmony)
        {
            if (!_byRecord.TryGetValue(record, out var owner))
                _byRecord[record] = owner = new Owner { Record = record, Harmony = harmony };
            _owners[assembly] = owner;
        }

        /// <summary>Adds the finalizer to each method the mod patched, once per method.</summary>
        internal static void Protect(Harmony harmony)
        {
            var finalizer = new HarmonyMethod(typeof(PatchGuard), nameof(Finalizer));
            foreach (var method in harmony.GetPatchedMethods())
                if (_guarded.Add(method))
                    _guard.Patch(method, finalizer: finalizer);
        }

        /// <summary>Returns null to swallow a mod's exception. An exception of the game itself passes through.</summary>
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            var owner = FindOwner(__exception);
            if (owner == null) return __exception;

            Count(owner, __exception);
            return null;
        }

        /// <summary>Counts an error against a mod in the shared budget. The event dispatcher uses it too.
        /// The turn-off itself waits for Tick, outside the callback.</summary>
        internal static void ReportError(ModRecord record, Exception exception)
        {
            if (record == null || exception == null) return;
            if (!_byRecord.TryGetValue(record, out var owner))
            {
                if (record.Instance?.Harmony == null) return;
                _byRecord[record] = owner = new Owner { Record = record, Harmony = record.Instance.Harmony };
            }
            Count(owner, exception);
        }

        private static void Count(Owner owner, Exception exception)
        {
            if (owner.Record.State == ModState.Failed) return;
            owner.Errors++;
            owner.LastError = exception.GetType().Name + ": " + exception.Message;
            if (owner.Errors <= MaxErrors)
                Extender.LogError($"Mod {owner.Record.Name}, error {owner.Errors}/{MaxErrors}: {exception}");
            if (owner.Errors == MaxErrors)
                _toTurnOff.Enqueue(owner);
        }

        private static Owner FindOwner(Exception exception)
        {
            var frames = new StackTrace(exception).GetFrames();
            if (frames == null) return null;
            foreach (var frame in frames)
            {
                var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
                if (assembly != null && _owners.TryGetValue(assembly, out var owner)) return owner;
            }
            return null;
        }

        // Flag [Guard] DeferUnpatch: turned-off mods wait here until the game is calm.
        private static readonly Queue<Owner> _toUnpatch = new();
        private static readonly StableGate _gate = new();

        /// <summary>Unpatching a method while it runs is unsafe, so this runs from a separate Unity Update.</summary>
        internal static void Tick()
        {
            while (_toTurnOff.Count > 0)
            {
                var owner = _toTurnOff.Dequeue();
                var reason = $"{MaxErrors} errors in patches or events, last: {owner.LastError}";
                if (Plugin.DeferUnpatch)
                {
                    // The mod is off now (State = Failed: no events, the finalizer swallows its errors
                    // silently). Its patches stay until the game is calm, see StableGate.
                    Extender.TurnOff(owner.Record, null, reason);
                    _toUnpatch.Enqueue(owner);
                }
                else
                {
                    Extender.TurnOff(owner.Record, owner.Harmony, reason);
                }
            }

            if (_toUnpatch.Count == 0) return;
            var started = SessionData.Instance != null && SessionData.Instance.startedGame;
            if (!_gate.Update(started, Time.unscaledDeltaTime)) return;
            while (_toUnpatch.Count > 0)
            {
                var owner = _toUnpatch.Dequeue();
                Extender.FinishTurnOff(owner.Record, owner.Harmony);
            }
        }
    }

    public class ExtenderBehaviour : MonoBehaviour
    {
        public ExtenderBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            PatchGuard.Tick();
            Events.Tick();
            StatusPanel.Tick();
        }
    }
}
