using Serilog.Core;
using Serilog.Events;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// In-memory Serilog sink that records log events so tests can assert logging behavior
/// without touching the file system.
/// </summary>
internal sealed class RecordingSink : ILogEventSink
{
    private readonly List<LogEvent> _events = new();

    /// <summary>Gets a snapshot of the events recorded so far.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        lock (_events)
        {
            _events.Add(logEvent);
        }
    }
}
