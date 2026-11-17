using System.Collections.Generic;
using SodExtender;

namespace SodOffline.Fakes
{
    /// <summary>Keeps every line, for asserts. Lines start with "I ", "W " or "E ".</summary>
    public sealed class FakeLog : IModLog
    {
        public readonly List<string> Lines = new();
        public void Info(string message) => Lines.Add("I " + message);
        public void Warning(string message) => Lines.Add("W " + message);
        public void Error(string message) => Lines.Add("E " + message);
    }
}
