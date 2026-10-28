using System;
using System.Collections.Generic;

namespace STBCVoiceCrew
{
    /// <summary>Reicht Arbeit von Hintergrund-Threads in den Unity-Hauptthread (Plugin.Update).</summary>
    public static class MainThread
    {
        static readonly Queue<Action> Queue = new Queue<Action>();

        public static void Post(Action a)
        {
            lock (Queue) Queue.Enqueue(a);
        }

        public static void Drain(Action<Exception> onError)
        {
            while (true)
            {
                Action a;
                lock (Queue)
                {
                    if (Queue.Count == 0) return;
                    a = Queue.Dequeue();
                }
                try { a(); }
                catch (Exception e) { onError(e); }
            }
        }
    }
}
