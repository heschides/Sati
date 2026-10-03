using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Data;
using Sati.Services;
using System.Windows;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Runs the shared WPF Application smoke test in the same collection as other
/// rendered views. StabilizationTests retains the separate PDF collection.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class FeatureViewSmokeTests
{
    [Fact]
    public void ParameterlessFeatureViewsCanOpenRenderAndCloseOnAnStaThread()
    {
        string? currentType = null;
        var exercisedTypes = new List<string>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionService, SessionService>();
                services.AddSingleton<IComprehensiveAssessmentService,
                    StabilizationTests.SmokeAssessmentService>();
                services.AddSingleton<IPersonCenteredPlanSourceService,
                    StabilizationTests.SmokePlanSourceService>();
                services.AddSingleton<IConsumerProviderService,
                    StabilizationTests.SmokeConsumerProviderService>();
                services.AddSingleton<IProviderService,
                    StabilizationTests.SmokeProviderService>();
            })
            .Build();

        WpfUiHarness.RunWithHost(host, () =>
        {
            var viewTypes = typeof(App).Assembly.GetTypes()
                .Where(type => type.IsPublic && !type.IsAbstract)
                .Where(type => type.Namespace?.StartsWith("Sati.Views", StringComparison.Ordinal) == true)
                .Where(type => typeof(FrameworkElement).IsAssignableFrom(type))
                .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();

            foreach (var type in viewTypes)
            {
                currentType = type.FullName;
                try
                {
                    var element = Assert.IsAssignableFrom<FrameworkElement>(
                        Activator.CreateInstance(type));
                    if (element is Window window)
                    {
                        window.Show();
                        window.UpdateLayout();
                        window.Close();
                    }
                    else
                    {
                        element.Measure(new Size(1280, 720));
                        element.Arrange(new Rect(0, 0, 1280, 720));
                        element.UpdateLayout();
                        element.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                        element.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    }

                    exercisedTypes.Add(type.FullName!);
                    currentType = null;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Feature view '{type.FullName}' could not open, render, and close.", ex);
                }
            }

            if (viewTypes.Count < 20)
                throw new InvalidOperationException($"Only {viewTypes.Count} feature views were discovered.");
        }, TimeSpan.FromSeconds(60));

        Assert.True(exercisedTypes.Count >= 20,
            $"Expected at least 20 feature views, exercised {exercisedTypes.Count}. " +
            $"Current view: {currentType ?? "none"}.");
    }
}
