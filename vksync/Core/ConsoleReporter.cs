using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace vksync.Core
{
    public class ConsoleReporter<T>
    {
        T state = default(T);

        BlockingCollection<Action<T>> _updateQueue = new BlockingCollection<Action<T>>();
        bool isDirty = true;

        VirtualConsole console = new VirtualConsole();
        private readonly Func<T, IList<string>> Renderer;
        Stopwatch stopwatch = new Stopwatch();
        private TimeSpan lastrun;

        public ConsoleReporter(T initialState, Func<T, IList<string>> renderer)
        {
            this.state = initialState;
            this.Renderer = renderer;
            Task.Factory.StartNew(UpdateQueueWorker);
            Task.Factory.StartNew(RenderWorker);
            stopwatch.Start();
            lastrun = stopwatch.Elapsed;
        }

        public void Update(Action<T> cs)
        {
            _updateQueue.Add(cs);
        }

        internal void ScheduleRender()
        {
            isDirty = true;
        }

        private void RenderWorker()
        {
            while (true)
            {
                if (stopwatch.Elapsed - lastrun > TimeSpan.FromSeconds(1 / 30.0))
                {
                    Render();
                    lastrun = stopwatch.Elapsed;
                }
                Thread.SpinWait(10);
            }
        }

        private void UpdateQueueWorker()
        {
            foreach (var update in _updateQueue.GetConsumingEnumerable())
            {
                update(state);
                isDirty = true;
            }
        }

        public void Render()
        {
            if (!isDirty) return;
            var sb = Renderer(state);
            console.Render(sb);
            isDirty = false;
        }
    }
}