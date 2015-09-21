using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace vksync.Core
{
    public class VirtualConsole
    {
        public IImmutableList<string> State { get; set; } = ImmutableList<string>.Empty;
        int DefaultCursorPosition { get; set; } = Console.CursorTop;

        int maxWidth = 80;

        public VirtualConsole()
        {
            maxWidth = Console.WindowWidth;
        }

        public void Render(IList<string> newState)
        {
            for (var i = 0; i < Math.Max(newState.Count, State.Count); i++)
            {
                var newStateLine = i < newState.Count ? newState[i] ?? "" : "";
                var oldStateLine = i < State.Count ? State[i] ?? "" : "";

                if (newStateLine != oldStateLine)
                {
                    RenderLine(i, newStateLine);
                }
            }

            Console.SetCursorPosition(0, DefaultCursorPosition + Math.Max(newState.Count, State.Count));

            State = ImmutableList<string>.Empty.AddRange(newState);
        }

        private void RenderLine(int row, string line)
        {
            Console.SetCursorPosition(0, DefaultCursorPosition + row);
            var res = line.Substring(0, Math.Min(line.Length, maxWidth)).PadRight(maxWidth, ' ');
            Console.Write(res);
        }        
    }
}