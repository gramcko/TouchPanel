namespace TouchPanel.Config.Migrations;

/// <summary>
/// Raised when configuration migration cannot proceed or a migration step fails (RF-076). The
/// caller (<c>JsonConfigStore</c>) treats this exactly like a corrupt source: it preserves the
/// original file for diagnosis and recovers from the backup or generated defaults, never
/// overwriting the source with unvalidated content.
/// </summary>
public sealed class ConfigMigrationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ConfigMigrationException"/> class.</summary>
    /// <param name="message">The failure description.</param>
    public ConfigMigrationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ConfigMigrationException"/> class.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The step failure that caused this migration to fail.</param>
    public ConfigMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
