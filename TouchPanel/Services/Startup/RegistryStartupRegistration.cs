using Microsoft.Win32;
using Serilog;
using TouchPanel.Services.Logging;

namespace TouchPanel.Services.Startup;

/// <summary>
/// Production <see cref="IStartupRegistration"/> (DD-11): manages the per-user
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> value named <c>TouchPanel</c>,
/// whose command is the quoted current executable path. The mechanism is per-user and never
/// requires elevation (RF-003, RF-086). Every registry failure is caught, logged with a
/// redaction-safe message, and returned as an unsuccessful result, so <see cref="Apply"/> and
/// <see cref="Read"/> never throw (RF-004).
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    /// <summary>Registry subkey that holds the per-user logon startup commands (DD-4).</summary>
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Value name of the TouchPanel startup entry (DD-4).</summary>
    public const string ValueName = "TouchPanel";

    private readonly IStartupRegistry _registry;
    private readonly string? _executablePath;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryStartupRegistration"/> class.
    /// </summary>
    /// <param name="registry">The registry seam; production passes <see cref="HkcuRunRegistry"/>.</param>
    /// <param name="executablePath">
    /// Full path of the executable the entry must point at (production:
    /// <see cref="Environment.ProcessPath"/>). <see langword="null"/> or whitespace means the
    /// path is unavailable; enabling then fails gracefully with a notification instead of
    /// registering a wrong command.
    /// </param>
    /// <param name="logger">Logger for self-heal warnings and failures; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is <see langword="null"/>.</exception>
    public RegistryStartupRegistration(
        IStartupRegistry registry,
        string? executablePath,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
        _executablePath = executablePath;
        _logger = (logger ?? Serilog.Log.Logger).ForContext<RegistryStartupRegistration>();
    }

    /// <inheritdoc />
    public StartupRegistrationSnapshot Read()
    {
        var expected = BuildExpectedCommand();

        try
        {
            var command = _registry.ReadCommand();
            return new StartupRegistrationSnapshot
            {
                Succeeded = true,
                Exists = command is not null,
                Command = command,
                IsCurrent = expected is not null && IsCurrentCommand(command, expected),
            };
        }
        catch (Exception ex)
        {
            var message = DescribeFailure("The per-user startup entry could not be read", ex);
            _logger.Error(ex, "Startup registration: reading the entry failed. {Message}", message);
            return new StartupRegistrationSnapshot
            {
                Succeeded = false,
                Exists = false,
                IsCurrent = false,
                FailureMessage = message,
            };
        }
    }

    /// <inheritdoc />
    public StartupRegistrationResult Apply(bool startWithWindows)
    {
        var expected = BuildExpectedCommand();

        if (startWithWindows && expected is null)
        {
            const string message =
                "The current executable path is unavailable; the start-with-Windows setting was not applied.";
            _logger.Error("Startup registration: {Message}", message);
            return StartupRegistrationResult.Failure("startup.path.unavailable", message);
        }

        var snapshot = Read();
        if (!snapshot.Succeeded)
        {
            return StartupRegistrationResult.Failure(
                "startup.read.failed",
                snapshot.FailureMessage
                    ?? "The per-user startup entry could not be read; the start-with-Windows setting was not applied.");
        }

        return startWithWindows
            ? EnsureRegistered(expected!, snapshot)
            : EnsureRemoved(snapshot);
    }

    private StartupRegistrationResult EnsureRegistered(string expected, StartupRegistrationSnapshot snapshot)
    {
        if (snapshot.IsCurrent)
        {
            _logger.Debug("Startup registration: the entry already points at the current executable; no change needed");
            return StartupRegistrationResult.NoChange();
        }

        // RF-004 self-heal: the configuration enables auto-start but the actual registration is
        // missing or stale; warn and re-register the current executable path.
        if (snapshot.Exists)
        {
            _logger.Warning(
                "Startup registration: enabled in configuration but the entry is stale; re-registering. Registered={RegisteredCommand} Expected={ExpectedCommand}",
                snapshot.Command,
                expected);
        }
        else
        {
            _logger.Warning(
                "Startup registration: enabled in configuration but the entry is missing; registering. Expected={ExpectedCommand}",
                expected);
        }

        try
        {
            _registry.WriteCommand(expected);
            _logger.Information(
                "Startup registration: the per-user startup entry now points at the current executable. Command={Command}",
                expected);
            return StartupRegistrationResult.Registered();
        }
        catch (Exception ex)
        {
            var message = DescribeFailure("The per-user startup entry could not be created or updated", ex);
            _logger.Error(ex, "Startup registration: writing the entry failed. {Message}", message);
            return StartupRegistrationResult.Failure("startup.write.failed", message);
        }
    }

    private StartupRegistrationResult EnsureRemoved(StartupRegistrationSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            _logger.Debug("Startup registration: disabled in configuration and no entry exists; no change needed");
            return StartupRegistrationResult.NoChange();
        }

        _logger.Information("Startup registration: disabled in configuration; removing the per-user startup entry");

        try
        {
            _registry.RemoveCommand();
            _logger.Information("Startup registration: the per-user startup entry was removed");
            return StartupRegistrationResult.Removed();
        }
        catch (Exception ex)
        {
            var message = DescribeFailure("The per-user startup entry could not be removed", ex);
            _logger.Error(ex, "Startup registration: removing the entry failed. {Message}", message);
            return StartupRegistrationResult.Failure("startup.remove.failed", message);
        }
    }

    /// <summary>
    /// Builds the command this build registers: the executable path quoted so paths with spaces
    /// stay a single token in the Run value (DD-4). Returns <see langword="null"/> when no usable
    /// path is available.
    /// </summary>
    private string? BuildExpectedCommand() =>
        string.IsNullOrWhiteSpace(_executablePath) ? null : "\"" + _executablePath.Trim() + "\"";

    /// <summary>
    /// Whether a registered command is exactly the expected command. Compared case-insensitively
    /// because Windows paths are; surrounding whitespace is ignored, but a missing quote pair or a
    /// different target counts as stale so the entry is normalized by a re-register (RF-004).
    /// </summary>
    private static bool IsCurrentCommand(string? command, string expected) =>
        !string.IsNullOrWhiteSpace(command)
        && string.Equals(command.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string DescribeFailure(string context, Exception ex) =>
        LogRedactor.RedactText($"{context} ({ex.GetType().Name}: {ex.Message}).");
}

/// <summary>
/// Production <see cref="IStartupRegistry"/>: the thin <c>Microsoft.Win32.Registry</c> wrapper
/// over the per-user Run key (DD-4/DD-11). All startup registry access lives here; unit tests
/// inject a fake seam instead so they never write to the real user hive (RNF-006).
/// </summary>
internal sealed class HkcuRunRegistry : IStartupRegistry
{
    private readonly string _valueName;

    /// <summary>
    /// Initializes a new instance of the <see cref="HkcuRunRegistry"/> class.
    /// </summary>
    /// <param name="valueName">Value name to manage; defaults to <see cref="RegistryStartupRegistration.ValueName"/>.</param>
    public HkcuRunRegistry(string valueName = RegistryStartupRegistration.ValueName)
    {
        _valueName = valueName;
    }

    /// <inheritdoc />
    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryStartupRegistration.RegistryPath, writable: false);

        // A missing key or value both mean "not registered"; a non-string value is treated as
        // absent so Apply can replace it with the expected REG_SZ command.
        return key?.GetValue(_valueName) as string;
    }

    /// <inheritdoc />
    public void WriteCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryStartupRegistration.RegistryPath, writable: true)
            ?? throw new InvalidOperationException("The per-user startup registry key could not be opened for writing.");
        key.SetValue(_valueName, command, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void RemoveCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryStartupRegistration.RegistryPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}
