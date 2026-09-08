using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EventHook.Helpers
{
    /// <summary>
    /// Bounded, non-blocking offload from an OS hook callback to a single consumer.
    /// Callbacks must only <see cref="TryWrite"/>; never wait or allocate heavy work.
    /// </summary>
    internal sealed class EventOffload<T> : IDisposable
    {
        private readonly Channel<T> channel;
        private readonly Func<T, bool> isCoalesceCandidate;
        private readonly Func<T, T, T> coalesce;
        private long dropped;
        private int pendingCoalesce;
        private T coalesceSlot;
        private bool disposed;

        internal EventOffload(
            int capacity = 1024,
            Func<T, bool> isCoalesceCandidate = null,
            Func<T, T, T> coalesce = null)
        {
            this.isCoalesceCandidate = isCoalesceCandidate;
            this.coalesce = coalesce;
            channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        internal long DroppedEventCount => Interlocked.Read(ref dropped);

        /// <summary>
        /// Non-blocking write for OS hook callbacks. Never waits.
        /// </summary>
        internal bool TryWrite(T item)
        {
            if (disposed)
            {
                return false;
            }

            if (isCoalesceCandidate != null && isCoalesceCandidate(item))
            {
                // Keep only the latest coalesce candidate in a single slot when the channel is full.
                if (channel.Writer.TryWrite(item))
                {
                    return true;
                }

                lock (channel)
                {
                    if (pendingCoalesce != 0 && coalesce != null)
                    {
                        coalesceSlot = coalesce(coalesceSlot, item);
                    }
                    else
                    {
                        coalesceSlot = item;
                        pendingCoalesce = 1;
                    }
                }

                Interlocked.Increment(ref dropped);
                return false;
            }

            if (channel.Writer.TryWrite(item))
            {
                return true;
            }

            Interlocked.Increment(ref dropped);
            return false;
        }

        internal async ValueTask<T> ReadAsync(CancellationToken cancellationToken)
        {
            // Prefer a coalesced move that was dropped while the channel was full.
            if (pendingCoalesce != 0)
            {
                lock (channel)
                {
                    if (pendingCoalesce != 0)
                    {
                        pendingCoalesce = 0;
                        return coalesceSlot;
                    }
                }
            }

            return await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        internal void Complete()
        {
            channel.Writer.TryComplete();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Complete();
        }
    }
}
