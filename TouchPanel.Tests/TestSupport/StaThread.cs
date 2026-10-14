using System.Windows.Threading;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Owns a background STA thread with a running <see cref="Dispatcher"/> so tests can exercise
/// WPF window hosting (the <c>HwndSource</c>-based <c>MessageWindow</c>) without depending on the
/// xUnit worker thread's apartment state. Window work is marshalled through
/// <see cref="InvokeAsync(Action)"/>; failed operations surface as task exceptions.
/// </summary>
internal sealed class StaThread : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public StaThread()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            ready.SetResult(dispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "TouchPanel.Tests.STA",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        _dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread and completes when it finished.</summary>
    /// <param name="action">The operation to run.</param>
    /// <returns>A task that faults when the operation throws.</returns>
    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

    /// <summary>Runs <paramref name="func"/> on the STA thread and returns its result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="func">The operation to run.</param>
    /// <returns>A task with the operation result; faults when the operation throws.</returns>
    public Task<T> InvokeAsync<T>(Func<T> func) => _dispatcher.InvokeAsync(func).Task;

    /// <summary>Shuts down the dispatcher loop and joins the thread.</summary>
    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
