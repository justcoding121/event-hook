using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace EventHook.Helpers
{
    /// <summary>
    /// Concurrent queue with async dequeue for a single consumer.
    /// </summary>
    internal class AsyncConcurrentQueue<T>
    {
        private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private readonly CancellationToken cancellationToken;

        internal AsyncConcurrentQueue(CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
        }

        internal void Enqueue(T value)
        {
            queue.Enqueue(value);
            signal.Release();
        }

        internal async Task<T> DequeueAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (queue.TryDequeue(out var result))
                {
                    return result;
                }

                await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
