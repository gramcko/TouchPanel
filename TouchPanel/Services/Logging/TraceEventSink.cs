using System.Diagnostics;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace TouchPanel.Services.Logging;

/// <summary>
/// Minimal fallback sink used when the rolling file sink cannot be created (RF-010/F-19).
/// Writes to <see cref="Trace"/> so diagnostics remain available (debugger/DebugView) in
/// both Debug and Release builds. This sink must never throw.
/// </summary>
internal sealed class TraceEventSink : ILogEventSink
{
    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        try
        {
            var timestamp = logEvent.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var message = $"{timestamp} [{logEvent.Level}] {logEvent.RenderMessage(CultureInfo.InvariantCulture)}";
            if (logEvent.Exception is not null)
            {
                message += Environment.NewLine + logEvent.Exception;
            }

            Trace.WriteLine(message);
        }
        catch (Exception)
        {
            // Fallback logging must never be able to crash the application.
        }
    }
}
