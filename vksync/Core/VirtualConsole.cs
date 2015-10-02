using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace vksync.Core
{
    public class VirtualConsole
    {
        public IImmutableList<string> State { get; set; } = ImmutableList<string>.Empty;

        int DefaultCursorPosition { get; } = Console.CursorTop;

        public void Render(IList<string> newState)
        {
            var max = Math.Max(newState.Count, State.Count);

            for (var i = 0; i < max; i++)
            {
                var newStateLine = i < newState.Count ? newState[i] ?? "" : "";
                var oldStateLine = i < State.Count ? State[i] ?? "" : "";

                if (newStateLine != oldStateLine)
                {
                    RenderLine(i, newStateLine);
                }
            }

            Console.SetCursorPosition(0, DefaultCursorPosition + max);

            State = ImmutableList<string>.Empty.AddRange(newState);
        }

        private void RenderLine(int row, string line)
        {
            Console.SetCursorPosition(0, DefaultCursorPosition + row);
            var maxWidth = Console.WindowWidth;

            var res = line.Substring(0, Math.Min(line.Length, maxWidth)).PadRight(maxWidth, ' ');
            Console.Write(res);
        }        
    }
}