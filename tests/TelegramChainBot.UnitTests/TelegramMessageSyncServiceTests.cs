using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelegramChainBot.Services;
using Xunit;

namespace TelegramChainBot.UnitTests;

public class TelegramMessageSyncServiceTests
{
    [Fact]
    public async Task ExecuteLockedAsync_SerializesConcurrentActions_ForSameKey()
    {
        // Arrange
        var syncService = new TelegramMessageSyncService();
        var activeCount = 0;
        var maxActiveCount = 0;
        var lockObj = new object();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(syncService.ExecuteLockedAsync(12345, 67890, async () =>
            {
                var current = Interlocked.Increment(ref activeCount);
                lock (lockObj)
                {
                    maxActiveCount = Math.Max(maxActiveCount, current);
                }

                await Task.Delay(50); // Simulate network or db delay

                Interlocked.Decrement(ref activeCount);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(1, maxActiveCount); // Concurrency should be strictly 1 because they share the same key
    }

    [Fact]
    public async Task ExecuteLockedAsync_AllowsParallelActions_ForDifferentKeys()
    {
        // Arrange
        var syncService = new TelegramMessageSyncService();
        var activeCount = 0;
        var maxActiveCount = 0;
        var lockObj = new object();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            var messageId = i;
            tasks.Add(syncService.ExecuteLockedAsync(12345, messageId, async () =>
            {
                var current = Interlocked.Increment(ref activeCount);
                lock (lockObj)
                {
                    maxActiveCount = Math.Max(maxActiveCount, current);
                }

                await Task.Delay(100);

                Interlocked.Decrement(ref activeCount);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.True(maxActiveCount > 1, $"Expected some concurrency since keys are different, got max: {maxActiveCount}");
    }
}
