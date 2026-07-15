using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramChainBot.Services;

public sealed class TelegramMessageSyncService
{
    private sealed class RefCountedSemaphore
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount { get; set; } = 1;
    }

    private readonly ConcurrentDictionary<string, RefCountedSemaphore> _locks = new();

    public async Task ExecuteLockedAsync(
        string key,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        RefCountedSemaphore refSemaphore;

        lock (_locks)
        {
            if (_locks.TryGetValue(key, out var existing))
            {
                existing.RefCount++;
                refSemaphore = existing;
            }
            else
            {
                refSemaphore = new RefCountedSemaphore();
                _locks[key] = refSemaphore;
            }
        }

        try
        {
            await refSemaphore.Semaphore.WaitAsync(cancellationToken);
            await action();
        }
        finally
        {
            refSemaphore.Semaphore.Release();

            lock (_locks)
            {
                refSemaphore.RefCount--;
                if (refSemaphore.RefCount <= 0)
                {
                    _locks.TryRemove(key, out _);
                    refSemaphore.Semaphore.Dispose();
                }
            }
        }
    }

    public async Task ExecuteLockedAsync(
        long chatId,
        long messageId,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        await ExecuteLockedAsync($"{chatId}:{messageId}", action, cancellationToken);
    }
}
