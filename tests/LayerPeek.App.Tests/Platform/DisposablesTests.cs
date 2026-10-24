using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

public sealed class DisposablesTests
{
    [Fact]
    public void EveryItemIsDisposedEvenWhenSomeFail()
    {
        List<string> disposed = [];
        List<Exception> failures = [];
        InvalidOperationException first = new("first");
        ObjectDisposedException second = new("second");

        Disposables.DisposeAll(
            [
                new Tracked("a", disposed),
                new Tracked("b", disposed, first),
                null,
                new Tracked("c", disposed, second),
                new Tracked("d", disposed),
            ],
            failures.Add);

        Assert.Equal(["a", "b", "c", "d"], disposed);
        Assert.Equal<Exception>([first, second], failures);
    }

    [Fact]
    public void FailingFailureHandlerDoesNotStopTheRest()
    {
        List<string> disposed = [];

        Disposables.DisposeAll(
            [new Tracked("a", disposed, new InvalidOperationException()), new Tracked("b", disposed)],
            _ => throw new ObjectDisposedException("LoggerFactory"));

        Assert.Equal(["a", "b"], disposed);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Disposables.DisposeAll(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => Disposables.DisposeAll([], null!));
    }

    private sealed class Tracked(string name, List<string> disposed, Exception? failure = null) : IDisposable
    {
        public void Dispose()
        {
            disposed.Add(name);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
