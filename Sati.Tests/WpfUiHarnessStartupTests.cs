using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class WpfUiHarnessStartupTests
{
    [Fact]
    public void PumpingTheHarnessCannotStartTheProductionLoginOrServiceHost()
    {
        WpfUiHarness.Run(() =>
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var application = Assert.IsAssignableFrom<App>(Application.Current);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.Null(typeof(App).GetField("_host", fields)!.GetValue(application));
            Assert.Null(typeof(App).GetField("_singleInstanceGuard", fields)!.GetValue(application));
            Assert.False((bool)typeof(App).GetField("_globalFailureHandlersRegistered", fields)!
                .GetValue(application)!);
            Assert.Equal(ShutdownMode.OnExplicitShutdown, application.ShutdownMode);
            Assert.NotEmpty(application.Resources.MergedDictionaries);
        });
    }
}
