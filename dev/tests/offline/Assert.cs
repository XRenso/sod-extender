using System;
using System.Collections.Generic;

namespace SodOffline
{
    /// <summary>Asserts for offline tests. Use: using static SodOffline.Assert;</summary>
    public static class Assert
    {
        public sealed class Failure : Exception { public Failure(string m) : base(m) { } }

        public static void Check(bool condition, string message)
        {
            if (!condition) throw new Failure(message);
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Failure($"{message ?? "not equal"}: expected <{expected}>, got <{actual}>");
        }

        public static TEx Throws<TEx>(Action action, string message = null) where TEx : Exception
        {
            try { action(); }
            catch (TEx ex) { return ex; }
            catch (Exception ex) { throw new Failure($"{message ?? "wrong exception"}: expected {typeof(TEx).Name}, got {ex.GetType().Name}"); }
            throw new Failure($"{message ?? "no exception"}: expected {typeof(TEx).Name}");
        }
    }
}
