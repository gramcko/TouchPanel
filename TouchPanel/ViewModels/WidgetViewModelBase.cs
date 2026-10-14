using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using TouchPanel.Actions;
using TouchPanel.Config;
using TouchPanel.Config.Validation;
using TouchPanel.Services.Logging;

namespace TouchPanel.ViewModels;

/// <summary>
/// Common presentation state and behavior for every config-driven widget (T-10, RF-032/RF-034):
/// identity, absolute geometry in DIPs, label, enablement/disabled reason, a transient error state
/// driven by <see cref="TimeProvider"/> (RF-036), and the shared action-invocation helper that
/// calls the T-09 <see cref="IActionExecutor"/> and surfaces failures as the error state plus a
/// structured log entry (RF-050). The host binds geometry and enablement directly, so templates
/// carry no per-instance logic (DD-6).
/// </summary>
/// <remarks>
/// <para><b>Interaction (RF-030/RF-035).</b> <see cref="ActivateAsync"/> represents one completed
/// tap (WPF buttons fire on release inside the control); Button/Toggle override the protected
/// <see cref="ActivateCoreAsync"/> hook. Disabled widgets (RF-047/RF-079) never execute and never
/// flip state.</para>
/// <para><b>Edit mode (T-12/RF-039).</b> <see cref="EditModeService"/> mutates geometry through
/// <see cref="ApplyGeometry"/> and selection through <see cref="SetSelected"/> on the UI thread;
/// both raise change notifications so the Canvas container follows drags and resizes live. The
/// service owns the clamping, so the widgets store already-validated geometry.</para>
/// <para><b>Error state (RF-036).</b> <see cref="HasError"/>/<see cref="ErrorMessage"/> are set on
/// failure and cleared again after <see cref="ErrorDisplayDuration"/> (~2 s) via a one-shot
/// <see cref="ITimer"/>; a new failure restarts the window. Interaction is never blocked while the
/// indicator is visible.</para>
/// <para><b>Threading (RNF-004).</b> All state mutation happens through <see cref="PostToUi"/>,
/// which uses the injected dispatcher delegate in production and runs inline when none is
/// supplied (unit tests). Action execution awaits the executor off the UI thread.</para>
/// </remarks>
public abstract class WidgetViewModelBase : INotifyPropertyChanged, IDisposable
{
    /// <summary>
    /// How long a failed action stays visibly flagged before the error state clears (RF-036,
    /// approximately two seconds).
    /// </summary>
    public static readonly TimeSpan ErrorDisplayDuration = TimeSpan.FromSeconds(2);

    private readonly IActionExecutor? _actionExecutor;
    private readonly TimeProvider _timeProvider;
    private readonly Action<Action>? _dispatch;
    private readonly ILogger _logger;
    private ITimer? _errorTimer;
    private int _errorGeneration;
    private bool _hasError;
    private string? _errorMessage;
    private bool _isSelected;
    private bool _isEditMode;
    private bool _disposed;
    private Brush? _backgroundBrush;
    private Brush? _foregroundBrush;
    private Brush? _accentBrush;
    private IAppearanceSource? _appearanceSource;
    private string? _iconId;

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetViewModelBase"/> class by projecting the
    /// validated config (RF-034).
    /// </summary>
    /// <param name="config">The validated widget config; must not be <see langword="null"/>.</param>
    /// <param name="templateKey">The template resource key the host resolves for this widget type.</param>
    /// <param name="actionExecutor">The action boundary; <see langword="null"/> for display-only widgets.</param>
    /// <param name="timeProvider">Clock used for the ~2 s error window; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="dispatch">Marshals state updates to the UI thread; <see langword="null"/> runs inline (tests).</param>
    /// <param name="logger">Logger for widget-level failures; defaults to the global Serilog logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="templateKey"/> is empty.</exception>
    protected WidgetViewModelBase(
        WidgetConfig config,
        string templateKey,
        IActionExecutor? actionExecutor = null,
        TimeProvider? timeProvider = null,
        Action<Action>? dispatch = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        WidgetId = config.Id;
        WidgetType = config.Type;
        TemplateKey = templateKey;
        Label = config.Label;
        X = NormalizeCoordinate(config.X);
        Y = NormalizeCoordinate(config.Y);
        Width = NormalizeExtent(config.Width);
        Height = NormalizeExtent(config.Height);
        IsEnabled = config.IsEnabled;
        DisabledReason = config.DisabledReason;
        BoundAction = config.Action;
        ApplyAppearance(config);

        _actionExecutor = actionExecutor;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dispatch = dispatch;
        _logger = logger ?? Log.Logger;

        ActivateCommand = new AsyncRelayCommand(() => ActivateAsync());
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the widget id from configuration; used in action logs (RF-051).</summary>
    public string WidgetId { get; }

    /// <summary>Gets the config <c>type</c> discriminator.</summary>
    public string WidgetType { get; }

    /// <summary>Gets the <c>DataTemplate</c> resource key the host selects for this widget type.</summary>
    public string TemplateKey { get; }

    /// <summary>
    /// Gets the configured label text, or <see langword="null"/> when unset. The edit-mode property
    /// editor updates it live through <see cref="ApplyConfig"/> without rebuilding the widget.
    /// </summary>
    public string? Label { get; private set; }

    /// <summary>
    /// Gets the glyph of the configured built-in icon, or <see langword="null"/> when no icon is set
    /// (RF-043/M2-RF-435). Resolved through the active icon pack (M3-RF-844/M3-RF-845) via
    /// <see cref="IconPackCatalog"/> — a missing pack falls back to the default pack glyph — and
    /// refreshed live when the active pack changes; templates render it with the bundled icon font.
    /// </summary>
    public string? IconGlyph { get; private set; }

    /// <summary>Gets a value indicating whether a built-in icon is configured for this widget.</summary>
    public bool HasIcon => IconGlyph is not null;

    /// <summary>Gets the configured background-color override, or <see langword="null"/> for the theme default.</summary>
    public Brush? BackgroundBrush => _backgroundBrush;

    /// <summary>Gets the configured foreground-color override, or <see langword="null"/> for the theme default.</summary>
    public Brush? ForegroundBrush => _foregroundBrush;

    /// <summary>Gets the configured accent-color override, or <see langword="null"/> for the theme default.</summary>
    public Brush? AccentBrush => _accentBrush;

    /// <summary>Gets the configured font-size override in DIPs, or <see langword="null"/> for the theme default.</summary>
    public double? FontSize { get; private set; }

    /// <summary>Gets the configured opacity override in <c>[0,1]</c>, or <see langword="null"/> for the theme default.</summary>
    public double? Opacity { get; private set; }

    /// <summary>Gets the horizontal position in DIPs (RF-032); edit mode updates it live (RF-039).</summary>
    public double X { get; private set; }

    /// <summary>Gets the vertical position in DIPs (RF-032); edit mode updates it live (RF-039).</summary>
    public double Y { get; private set; }

    /// <summary>Gets the width in DIPs (RF-032); validator-normalized values pass through unchanged.</summary>
    public double Width { get; private set; }

    /// <summary>Gets the height in DIPs (RF-032); validator-normalized values pass through unchanged.</summary>
    public double Height { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this widget is the current edit-mode selection (T-12). The
    /// overlay highlights the selected frame so touch selection gives visible feedback and arrow
    /// nudging has an obvious target (RF-027).
    /// </summary>
    public bool IsSelected => _isSelected;

    /// <summary>
    /// Gets a value indicating whether the panel is currently in edit mode (T-12/RF-039,
    /// M2-RF-439). Set by <see cref="EditModeService"/> on the widgets of the edited page; while
    /// <see langword="true"/> the activation path short-circuits so a tap that reaches the widget
    /// command never executes its bound action or shows a transient error badge.
    /// </summary>
    public bool IsEditMode => _isEditMode;

    /// <summary>
    /// Gets a value indicating whether edit mode may move and resize this widget (RF-039). Unknown
    /// widget types preserve their raw JSON untouched, so they opt out of geometry editing.
    /// </summary>
    public virtual bool CanEditGeometry => true;

    /// <summary>
    /// Gets the enablement decided by validation (RF-047/RF-079): a widget with an unknown type or
    /// invalid action binding stays visible but disabled with <see cref="DisabledReason"/>.
    /// </summary>
    public bool IsEnabled { get; protected set; }

    /// <summary>Gets the human-readable reason a widget was disabled, shown by the error indicator.</summary>
    public string? DisabledReason { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether the persistent disabled indicator must be visible (RF-047/
    /// RF-079). A disabled widget (or one carrying a disabled reason) shows an always-visible
    /// corner badge in the interactive templates, because hover-only feedback such as the tooltip
    /// is not available on a touchscreen (RF-025). The transient failure badge stays separate
    /// (RF-036).
    /// </summary>
    public bool ShowsDisabledIndicator => !IsEnabled || !string.IsNullOrWhiteSpace(DisabledReason);

    /// <summary>Gets a value indicating whether the widget has a bound action and may execute one.</summary>
    public bool IsInteractable => IsEnabled && BoundAction is not null;

    /// <summary>Gets a value indicating whether the transient failure indicator is currently visible (RF-036).</summary>
    public bool HasError => _hasError;

    /// <summary>Gets the redaction-safe failure text shown alongside the indicator, or <see langword="null"/>.</summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>Gets the command the templates bind to; one execution represents one completed tap.</summary>
    public ICommand ActivateCommand { get; }

    /// <summary>
    /// Gets the action bound to this widget, when any (RF-034/RF-047). It is refreshed live by
    /// <see cref="ApplyConfig"/> when the edit-mode action picker changes the binding (M2-RF-434),
    /// so <see cref="IsInteractable"/> and the next execution use the chosen action without a
    /// rebuild.
    /// </summary>
    protected ActionConfig? BoundAction { get; private set; }

    /// <summary>
    /// Executes one completed tap (RF-035/RF-030). Disabled widgets no-op (RF-047); everything else
    /// is contained so an exception can never escape an interaction (RF-050).
    /// </summary>
    /// <param name="cancellationToken">Cancels the in-flight action (for example during shutdown).</param>
    /// <returns>A task that completes when the interaction finished.</returns>
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        // M2-RF-439: edit mode owns taps (drag/resize/select); a tap that still reaches the widget
        // command must not execute the action or surface an error badge.
        if (!IsEnabled || IsEditMode)
        {
            return;
        }

        try
        {
            await ActivateCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure (RF-055 semantics); no error indicator.
        }
        catch (Exception exception)
        {
            _logger.Warning(
                exception,
                "Widget {WidgetId}: activation failed unexpectedly",
                WidgetId);
            ShowTransientError("The action failed.");
        }
    }

    /// <summary>
    /// Applies an edited configuration to the live view model without rebuilding it, so property
    /// edits made in the T-13 property editor are reflected immediately (RF-043/M2-RF-433). The
    /// base implementation refreshes the shared pieces (label and enablement); type-specific view
    /// models override it to refresh their own projected state (range, sensor, style, …). Must run
    /// on the UI thread.
    /// </summary>
    /// <param name="config">The edited widget config; must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is <see langword="null"/>.</exception>
    public virtual void ApplyConfig(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        ApplyAppearance(config);
        ApplyLabel(config.Label);
        ApplyBoundAction(config.Action);
        ApplyEnablement(config);
    }

    /// <summary>
    /// Projects the label icon and appearance overrides onto the live view model (RF-043/
    /// M2-RF-435/M2-RF-436) without a rebuild, so edit-mode changes are reflected immediately. Null
    /// values mean "use the theme default" and are rendered through <c>TargetNullValue</c> fallbacks.
    /// Raises a change notification only for properties that actually changed.
    /// </summary>
    /// <param name="config">The edited widget config; must not be <see langword="null"/>.</param>
    private void ApplyAppearance(WidgetConfig config)
    {
        _iconId = config.Icon;
        UpdateIconGlyph();

        SetBrush(ref _backgroundBrush, nameof(BackgroundBrush), AppearanceBrushFactory.Create(config.Background));
        SetBrush(ref _foregroundBrush, nameof(ForegroundBrush), AppearanceBrushFactory.Create(config.Foreground));
        SetBrush(ref _accentBrush, nameof(AccentBrush), AppearanceBrushFactory.Create(config.Accent));

        var fontSize = WidgetAppearance.NormalizeFontSize(config.FontSize);
        if (!Nullable.Equals(FontSize, fontSize))
        {
            FontSize = fontSize;
            OnPropertyChanged(nameof(FontSize));
        }

        var opacity = WidgetAppearance.NormalizeOpacity(config.Opacity);
        if (!Nullable.Equals(Opacity, opacity))
        {
            Opacity = opacity;
            OnPropertyChanged(nameof(Opacity));
        }
    }

    /// <summary>
    /// Attaches the application-level appearance source so <see cref="IconGlyph"/> resolves through
    /// the active icon pack and re-resolves live when the pack changes (M3-RF-844/M3-RF-845). Owned
    /// by the widget registry, which attaches the shared source right after construction; detaches
    /// the previous source so re-attachment never leaks a handler. Must run on the UI thread.
    /// </summary>
    /// <param name="source">The active appearance source, or <see langword="null"/> for the default pack.</param>
    internal void AttachAppearanceSource(IAppearanceSource? source)
    {
        if (ReferenceEquals(_appearanceSource, source))
        {
            return;
        }

        if (_appearanceSource is not null)
        {
            _appearanceSource.AppearanceChanged -= OnAppearanceChanged;
        }

        _appearanceSource = source;
        if (source is not null)
        {
            source.AppearanceChanged += OnAppearanceChanged;
        }

        UpdateIconGlyph();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => PostToUi(UpdateIconGlyph);

    private void UpdateIconGlyph()
    {
        var glyph = IconPackCatalog.GlyphFor(_appearanceSource?.ActiveIconPackId, _iconId);
        if (string.Equals(IconGlyph, glyph, StringComparison.Ordinal))
        {
            return;
        }

        IconGlyph = glyph;
        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(HasIcon));
        OnIconGlyphChanged();
    }

    /// <summary>
    /// Called after <see cref="IconGlyph"/> changed (initially or from a live icon-pack swap). The
    /// base implementation is empty; widgets that project the glyph into additional properties (for
    /// example the ImageButton's <c>DisplayGlyph</c>) override it to raise those change
    /// notifications without a rebuild.
    /// </summary>
    protected virtual void OnIconGlyphChanged()
    {
    }

    private void SetBrush(ref Brush? field, string propertyName, Brush? value)
    {
        if (ReferenceEquals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void ApplyLabel(string? label)
    {
        if (string.Equals(Label, label, StringComparison.Ordinal))
        {
            return;
        }

        Label = label;
        OnPropertyChanged(nameof(Label));
    }

    /// <summary>
    /// Refreshes the live action binding after an edit-mode action change (M2-RF-434) and raises
    /// <see cref="IsInteractable"/> when the presence of an action changed. The widget executes the
    /// new action on its next interaction without a rebuild.
    /// </summary>
    /// <param name="action">The edited action configuration, or <see langword="null"/> for none.</param>
    private void ApplyBoundAction(ActionConfig? action)
    {
        if (ReferenceEquals(BoundAction, action))
        {
            return;
        }

        BoundAction = action;
        OnPropertyChanged(nameof(IsInteractable));
    }

    private void ApplyEnablement(WidgetConfig config)
    {
        if (IsEnabled != config.IsEnabled)
        {
            IsEnabled = config.IsEnabled;
            OnPropertyChanged(nameof(IsEnabled));
        }

        if (!string.Equals(DisabledReason, config.DisabledReason, StringComparison.Ordinal))
        {
            DisabledReason = config.DisabledReason;
            OnPropertyChanged(nameof(DisabledReason));
        }

        OnPropertyChanged(nameof(ShowsDisabledIndicator));
        OnPropertyChanged(nameof(IsInteractable));
    }

    /// <summary>
    /// Applies edit-mode geometry already clamped by <see cref="EditModeService"/> (T-12/RF-039)
    /// and raises a change notification per property that actually changed, so the host's Canvas
    /// container follows a drag or resize live. Must run on the UI thread.
    /// </summary>
    /// <param name="x">The clamped horizontal position in DIPs.</param>
    /// <param name="y">The clamped vertical position in DIPs.</param>
    /// <param name="width">The clamped width in DIPs.</param>
    /// <param name="height">The clamped height in DIPs.</param>
    internal void ApplyGeometry(double x, double y, double width, double height)
    {
        if (X != x)
        {
            X = x;
            OnPropertyChanged(nameof(X));
        }

        if (Y != y)
        {
            Y = y;
            OnPropertyChanged(nameof(Y));
        }

        if (Width != width)
        {
            Width = width;
            OnPropertyChanged(nameof(Width));
        }

        if (Height != height)
        {
            Height = height;
            OnPropertyChanged(nameof(Height));
        }
    }

    /// <summary>
    /// Updates the edit-mode selection visual state (T-12). Owned by <see cref="EditModeService"/>;
    /// raises a change notification only when the value actually changed.
    /// </summary>
    /// <param name="selected">Whether this widget is the current selection.</param>
    internal void SetSelected(bool selected)
    {
        if (_isSelected == selected)
        {
            return;
        }

        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }

    /// <summary>
    /// Sets the edit-mode state owned by <see cref="EditModeService"/> (T-12/RF-039/M2-RF-439),
    /// suppressing action execution for as long as the panel is being edited. Raises a change
    /// notification only when the value actually changed. Must run on the UI thread.
    /// </summary>
    /// <param name="isEditMode">Whether the panel is currently in edit mode.</param>
    internal void SetEditMode(bool isEditMode)
    {
        if (_isEditMode == isEditMode)
        {
            return;
        }

        _isEditMode = isEditMode;
        OnPropertyChanged(nameof(IsEditMode));
    }

    /// <summary>
    /// The type-specific interaction behavior for one completed tap. The base implementation is a
    /// no-op for display-only widgets; Button executes its bound action and Toggle flips state then
    /// executes (RF-035).
    /// </summary>
    /// <param name="cancellationToken">Cancels the in-flight action.</param>
    /// <returns>A task that completes when the interaction finished.</returns>
    protected virtual Task ActivateCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Invokes the bound action through <see cref="IActionExecutor"/> and surfaces failures as the
    /// persistent log entry the executor already wrote plus a ~2 s transient widget error state
    /// (RF-036/RF-050). Success and cancellation leave the widget state untouched.
    /// </summary>
    /// <param name="cancellationToken">Cancels the in-flight execution.</param>
    /// <returns>A task that completes when the outcome was applied.</returns>
    protected Task ExecuteBoundActionAsync(CancellationToken cancellationToken = default)
    {
        var action = BoundAction;
        return action is null ? Task.CompletedTask : ExecuteActionAsync(action, cancellationToken);
    }

    /// <summary>
    /// Invokes one action through <see cref="IActionExecutor"/> and surfaces failures as the
    /// persistent log entry the executor already wrote plus a ~2 s transient widget error state
    /// (RF-036/RF-050). This is the shared path for widgets whose action is fixed
    /// (<see cref="ExecuteBoundActionAsync"/>) as well as value-driven widgets such as the Slider
    /// (RF-040), which builds a parameterized action per emitted value.
    /// </summary>
    /// <param name="action">The action to execute; must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the in-flight execution.</param>
    /// <returns>A task that completes when the outcome was applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    protected async Task ExecuteActionAsync(ActionConfig action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var executor = _actionExecutor;
        if (executor is null)
        {
            return;
        }

        ActionExecutionResult result;
        try
        {
            result = await executor.ExecuteAsync(action, WidgetId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            // Defensive: the executor contract says it never throws (RF-050); a custom
            // implementation that does still degrades to a transient error instead of a crash.
            _logger.Warning(
                exception,
                "Widget {WidgetId}: action {ActionType} threw outside the executor boundary",
                WidgetId,
                action.Type);
            ShowTransientError($"The action failed ({exception.GetType().Name}).");
            return;
        }

        if (result.Success || result.Canceled)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(result.Message) ? "The action failed." : result.Message;
        _logger.Warning(
            "Widget {WidgetId}: action {ActionType} failed and is shown as a transient error: {Reason}",
            WidgetId,
            action.Type,
            LogRedactor.RedactText(message));
        ShowTransientError(message);
    }

    /// <summary>
    /// Enters or restarts the transient error state (RF-036). The message is redacted defensively
    /// before it is displayed (RF-085) even though the executor already returns scrubbed text.
    /// </summary>
    /// <param name="message">The failure text; empty text maps to a generic message.</param>
    protected void ShowTransientError(string? message)
    {
        var safeMessage = LogRedactor.RedactText(message);
        if (string.IsNullOrWhiteSpace(safeMessage))
        {
            safeMessage = "The action failed.";
        }

        PostToUi(() => ApplyTransientError(safeMessage));
    }

    /// <summary>Raises <see cref="PropertyChanged"/>; intended for use from the UI thread.</summary>
    /// <param name="propertyName">The changed property; inferred from the caller when omitted.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread through the injected dispatcher delegate,
    /// or inline when none was supplied (unit tests). A dispatcher that is shutting down makes the
    /// transient error update droppable instead of fatal.
    /// </summary>
    /// <param name="action">The state update to run.</param>
    protected void PostToUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatch = _dispatch;
        if (dispatch is null)
        {
            action();
            return;
        }

        try
        {
            dispatch(action);
        }
        catch (Exception)
        {
            // The panel is closing; dropping a transient visual update is safe and must not
            // surface an exception on a timer thread.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnDispose();
        DetachAppearanceSource();
        _errorTimer?.Dispose();
        _errorTimer = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Unsubscribes from the appearance source (M3-RF-844). Runs after the type-specific
    /// <see cref="OnDispose"/> so disposal leaves no live pack-change handler regardless of whether a
    /// derived override calls the base.
    /// </summary>
    private void DetachAppearanceSource()
    {
        if (_appearanceSource is null)
        {
            return;
        }

        _appearanceSource.AppearanceChanged -= OnAppearanceChanged;
        _appearanceSource = null;
    }

    /// <summary>
    /// Releases type-specific resources before the base class clears the error timer. The default
    /// implementation is empty; widgets that subscribe to external events (for example the Gauge
    /// and the monitoring snapshot source) unsubscribe here so disposal leaves no live handlers.
    /// </summary>
    protected virtual void OnDispose()
    {
    }

    private void ApplyTransientError(string message)
    {
        if (_disposed)
        {
            return;
        }

        _errorMessage = message;
        if (!_hasError)
        {
            _hasError = true;
            OnPropertyChanged(nameof(HasError));
        }

        OnPropertyChanged(nameof(ErrorMessage));
        RestartErrorTimer();
    }

    private void RestartErrorTimer()
    {
        _errorTimer?.Dispose();

        // The generation token makes a callback from a superseded timer harmless: it neither
        // clears a newer error state nor disposes the current timer (the fired timer's callback is
        // already queued on the dispatcher and cannot be retracted).
        var generation = ++_errorGeneration;
        _errorTimer = _timeProvider.CreateTimer(
            _ => OnErrorTimerElapsed(generation),
            state: null,
            dueTime: ErrorDisplayDuration,
            period: Timeout.InfiniteTimeSpan);
    }

    private void OnErrorTimerElapsed(int generation) =>
        PostToUi(() =>
        {
            if (_disposed || generation != _errorGeneration)
            {
                return;
            }

            _errorTimer?.Dispose();
            _errorTimer = null;

            if (!_hasError)
            {
                return;
            }

            _hasError = false;
            _errorMessage = null;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
        });

    /// <summary>
    /// Keeps raw or unknown config geometry bindable: non-finite coordinates become 0 and
    /// non-finite/non-positive extents fall back to the 40 DIP touch-target minimum (D-9/RF-031).
    /// Validator-normalized widgets are always finite and at least 40 DIP, so their values pass
    /// through unchanged (RF-032 geometry mapping).
    /// </summary>
    private static double NormalizeCoordinate(double value) => double.IsFinite(value) ? value : 0;

    private static double NormalizeExtent(double value) =>
        double.IsFinite(value) && value > 0 ? value : ConfigValidator.MinWidgetSize;
}
