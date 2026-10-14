using TouchPanel.Config;

namespace TouchPanel.Services.Appearance;

/// <summary>
/// Mutable application-level appearance state (M3-RF-844/M3-RF-845): owns the active built-in
/// icon-pack id and announces changes so the realized widget view models re-resolve their glyphs
/// without a rebuild. The composition root updates it from the appearance apply step (M3-RF-848);
/// it is WPF-free and raises <see cref="AppearanceChanged"/> only when the canonical id actually
/// changes, so repeated applies are no-ops.
/// </summary>
public sealed class AppearanceState : IAppearanceSource
{
    private string _iconPackId = IconPackIds.Default;

    /// <inheritdoc />
    public string ActiveIconPackId => _iconPackId;

    /// <inheritdoc />
    public event EventHandler? AppearanceChanged;

    /// <summary>
    /// Sets the active icon pack from a configuration id, normalizing unknown/missing ids to
    /// <see cref="IconPackIds.Default"/> (M3-RF-847). Raises <see cref="AppearanceChanged"/> only when
    /// the canonical id changed, isolating a misbehaving subscriber (M3-RNF-138).
    /// </summary>
    /// <param name="iconPackId">The requested built-in pack id (case-insensitive, may be null).</param>
    public void SetIconPack(string? iconPackId)
    {
        var normalized = IconPackIds.Normalize(iconPackId);
        if (string.Equals(_iconPackId, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _iconPackId = normalized;
        RaiseAppearanceChanged();
    }

    /// <summary>Applies the icon pack of a whole appearance section (M3-RF-848).</summary>
    /// <param name="appearance">The validated appearance section, or <see langword="null"/> for defaults.</param>
    public void SetAppearance(AppearanceConfig? appearance) => SetIconPack(appearance?.IconPack);

    private void RaiseAppearanceChanged()
    {
        var handler = AppearanceChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // A subscriber failure must not prevent the other subscribers or the caller from
            // completing; the state is already updated (M3-RNF-138).
        }
    }
}
