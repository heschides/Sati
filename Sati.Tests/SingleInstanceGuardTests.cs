using Sati.Services;
using Xunit;

namespace Sati.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void ASecondThreadCannotStartWhileTheFirstOwnsTheDesktopGuard()
    {
        var name = @"Global\SatiLogica.Sati.Tests." + Guid.NewGuid().ToString("N");
        using var first = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(first);

        SingleInstanceGuard? second = null;
        Exception? failure = null;
        var contender = new Thread(() =>
        {
            try { second = SingleInstanceGuard.TryAcquire(name); }
            catch (Exception exception) { failure = exception; }
        });

        contender.Start();
        Assert.True(contender.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Null(second);

        first.Dispose();
        using var afterExit = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(afterExit);
    }

    [Fact]
    public void ALaunchCanContinueAfterThePreviousOwnerExitsWithoutReleasing()
    {
        var name = @"Global\SatiLogica.Sati.Tests." + Guid.NewGuid().ToString("N");
        Mutex? abandonedOwner = null;
        bool acquired = false;
        Exception? failure = null;
        try
        {
            var owner = new Thread(() =>
            {
                try
                {
                    abandonedOwner = new Mutex(initiallyOwned: false, name);
                    acquired = abandonedOwner.WaitOne(0);
                }
                catch (Exception exception) { failure = exception; }
            });

            owner.Start();
            Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.True(acquired);

            using var nextLaunch = SingleInstanceGuard.TryAcquire(name);
            Assert.NotNull(nextLaunch);
        }
        finally { abandonedOwner?.Dispose(); }
    }
}
