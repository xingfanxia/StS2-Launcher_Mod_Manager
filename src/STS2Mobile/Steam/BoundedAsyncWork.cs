using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace STS2Mobile.Steam;

// Runs independent network work concurrently without allowing an unbounded
// request fan-out. Result ownership and persistence remain with the caller.
internal static class BoundedAsyncWork
{
    public static async Task ForEachAsync<T>(
        IReadOnlyList<T> items,
        int maxConcurrency,
        Func<T, Task> worker
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(worker);
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new Task[items.Count];
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            tasks[index] = RunOneAsync(item);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        async Task RunOneAsync(T item)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await worker(item).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
