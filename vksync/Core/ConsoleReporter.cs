using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace vksync.Core
{
    public class ConsoleReporter<T>
    {
        T _state;

        readonly BlockingCollection<Func<T,T>> _updateQueue = new BlockingCollection<Func<T, T>>();
        bool _isDirty = true;

        readonly VirtualConsole _console = new VirtualConsole();
        private readonly Func<T, IList<string>> _renderer;

        public ConsoleReporter(T initialState, Func<T, IList<string>> renderer)
        {
            _state = initialState;
            _renderer = renderer;

            Task.Factory.StartNew(UpdateQueueWorker);
            RunEvery(Render, 1000 / 30.0);
        }

        public void Update(Func<T, T> cs)
        {
            _updateQueue.Add(cs);
        }

        // shim.
        public void Update(Action<T> update)
        {
            Update(state =>
            {
                update(state);
                return state;
            });
        }

        internal void ScheduleRender()
        {
            _isDirty = true;
        }

        private void RunEvery(Action action, double ms)
        {
            var timer = new Timer(ms);
            bool isActing = false;
            
            timer.Elapsed += (sender, args) =>
            {
                if (Volatile.Read(ref isActing)) return;
                try
                {
                    Volatile.Write(ref isActing, true);
                    action();
                }
                finally
                {
                    Volatile.Write(ref isActing, false);
                }
            };

            timer.Start();
        }

        private void UpdateQueueWorker()
        {
            foreach (var update in _updateQueue.GetConsumingEnumerable())
            {
                _state = update(_state);
                _isDirty = true;
            }
        }

        public void Render()
        {
            if (!_isDirty) return;
            var sb = _renderer(_state);
            _console.Render(sb);
            _isDirty = false;
        }
    }
}