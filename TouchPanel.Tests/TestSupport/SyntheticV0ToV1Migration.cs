using TouchPanel.Config;
using TouchPanel.Config.Migrations;

namespace TouchPanel.Tests.TestSupport;

/// <summary>
/// Synthetic v0 → v1 migration used only by tests (no real v0 schema ever shipped; plan §7). It
/// offsets every widget position by <see cref="MarkerOffset"/> so tests can observe that the
/// migration ran and can produce a deliberate validation finding when configured to exceed the
/// page bounds.
/// </summary>
internal sealed class SyntheticV0ToV1Migration : IConfigMigration
{
    /// <summary>Horizontal offset added to every widget so migration is observable.</summary>
    public const double MarkerOffset = 7;

    /// <inheritdoc />
    public int FromVersion => 0;

    /// <inheritdoc />
    public int ToVersion => 1;

    /// <summary>Gets or sets a value indicating whether the migration throws instead of succeeding.</summary>
    public bool Fail { get; set; }

    /// <inheritdoc />
    public PanelConfig Migrate(PanelConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (Fail)
        {
            throw new InvalidOperationException("synthetic migration failure");
        }

        foreach (var page in config.Pages)
        {
            foreach (var widget in page.Widgets)
            {
                widget.X += MarkerOffset;
            }
        }

        return config;
    }
}
