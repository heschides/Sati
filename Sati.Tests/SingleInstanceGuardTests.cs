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

            // Windows abandons the mutex when the owner's OS thread terminates, which
            // can trail Thread.Join on a loaded machine. Wait for that, bounded: a guard
            // that mishandled abandonment would still return null for the whole wait.
            SingleInstanceGuard? nextLaunch = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((nextLaunch = SingleInstanceGuard.TryAcquire(name)) is null &&
                   DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }

            using var _ = nextLaunch;
            Assert.NotNull(nextLaunch);
        }
        finally { abandonedOwner?.Dispose(); }
    }
}
