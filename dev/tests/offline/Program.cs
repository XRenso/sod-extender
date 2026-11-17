using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace SodOffline
{
    /// <summary>Test runner with the same convention as the in-game suite: a public static class,
    /// public static void Test*() methods with no parameters. Order is by name, so runs are repeatable.
    /// Args: [--filter TEXT] [--list]. Exit code 0 = all passed, 1 = a failure or no tests.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string filter = null;
            var listOnly = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--filter" && i + 1 < args.Length) filter = args[++i];
                else if (args[i] == "--list") listOnly = true;
            }

            var tests = typeof(Program).Assembly.GetTypes()
                .Where(t => t.IsAbstract && t.IsSealed && t.IsPublic) // public static class
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.Name.StartsWith("Test") && m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                .Select(m => (Name: m.DeclaringType.FullName + "." + m.Name, Method: m))
                .Where(t => filter == null || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

            if (listOnly)
            {
                foreach (var t in tests) Console.WriteLine(t.Name);
                return 0;
            }

            var failed = 0;
            var total = Stopwatch.StartNew();
            foreach (var (name, method) in tests)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    method.Invoke(null, null);
                    Console.WriteLine($"PASS {name} ({sw.Elapsed.TotalMilliseconds:0.0} ms)");
                }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    var inner = ex.InnerException;
                    Console.WriteLine($"FAIL {name}");
                    Console.WriteLine("  " + (inner is Assert.Failure ? inner.Message : inner?.ToString()));
                }
            }
            Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed in {total.Elapsed.TotalMilliseconds:0} ms");
            return failed == 0 && tests.Count > 0 ? 0 : 1;
        }
    }
}
