using System.Reflection;

using Avalonia.Controls.Templates;

using LayerPeek.App.Cameras.Overlay.Widgets;
using LayerPeek.App.Composition;
using LayerPeek.Core.Cameras.Overlay;

namespace LayerPeek.App.Tests.Cameras.Overlay.Widgets;

/// <summary>
/// Tests that reset the cached card template of the Status widget (T-131). They run alone, after all parallel tests,
/// because every other overlay test reads <see cref="StatusWidgetTemplate.Card"/>; the original template is restored.
/// </summary>
[CollectionDefinition(nameof(StatusWidgetTemplateGroup), DisableParallelization = true)]
public sealed class StatusWidgetTemplateGroup;

/// <summary>
/// T-131: the first use of <see cref="StatusWidgetTemplate.Card"/> from several threads at once (parallel test classes)
/// must hand every caller the same template; a plain <c>field ??= …</c> let a caller keep a second, unpublished one.
/// </summary>
[Collection(nameof(StatusWidgetTemplateGroup))]
public sealed class StatusWidgetTemplateRaceTests
{
    private const int Threads = 8;
    private const int Rounds = 20;

    // Test-only reflection on the compiler-generated backing field of the 'field' keyword (test code is not AOT).
    private static readonly FieldInfo CardField =
        typeof(StatusWidgetTemplate).GetField("<Card>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("The backing field of StatusWidgetTemplate.Card was not found.");

    [Fact]
    public void FirstUseFromManyThreadsAtOnceGivesEveryoneTheSameTemplate()
    {
        object? original = CardField.GetValue(null);
        try
        {
            for (int round = 0; round < Rounds; round++)
            {
                CardField.SetValue(null, null);
                IDataTemplate?[] seen = new IDataTemplate?[Threads];
                IDataTemplate?[] registered = new IDataTemplate?[Threads];
                Exception?[] failures = new Exception?[Threads];
                using Barrier start = new(Threads);

                Thread[] threads = [.. Enumerable.Range(0, Threads).Select(index => new Thread(() =>
                {
                    // An exception escaping a raw thread would terminate the test host, so it is reported instead.
                    try
                    {
                        start.SignalAndWait(TestContext.Current.CancellationToken);
                        // Half of the threads go through the registry (as the parallel widget tests do), half directly.
                        if (index % 2 == 0)
                        {
                            registered[index] = OverlayWidgets.CreateRegistry().Find(OverlayWidgetIds.Status)!.CardTemplate;
                        }

                        seen[index] = StatusWidgetTemplate.Card;
                    }
                    catch (Exception exception)
                    {
                        failures[index] = exception;
                    }
                })
                { IsBackground = true })];
                foreach (Thread thread in threads)
                {
                    thread.Start();
                }

                foreach (Thread thread in threads)
                {
                    Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "A thread did not finish.");
                }

                Assert.All(failures, failure => Assert.Null(failure));
                IDataTemplate published = StatusWidgetTemplate.Card;
                for (int index = 0; index < Threads; index++)
                {
                    Assert.Same(published, seen[index]);
                    if (index % 2 == 0)
                    {
                        Assert.Same(published, registered[index]);
                    }
                }
            }
        }
        finally
        {
            // Leave the template the earlier tests (and their registries) used.
            CardField.SetValue(null, original);
        }
    }
}
