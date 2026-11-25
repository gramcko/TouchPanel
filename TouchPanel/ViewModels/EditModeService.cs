using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Serilog;
using TouchPanel.Config;
using TouchPanel.Config.Json;
using TouchPanel.Config.Validation;
using TouchPanel.Monitoring;
using TouchPanel.Services.Notifications;
using TouchPanel.Services.Time;
using TouchPanel.Theme;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.ViewModels;

/// <summary>
/// One widget's geometry in page DIPs (RF-032/RF-039), produced by the edit-mode clamping
/// helpers so moves and resizes are always finite, within the page bounds, and at least
/// <see cref="EditModeService.MinimumWidgetSize"/> DIP.
/// </summary>
/// <param name="X">Horizontal position in DIPs (0 is the page's left edge).</param>
/// <param name="Y">Vertical position in DIPs (0 is the page's top edge).</param>
/// <param name="Width">Width in DIPs.</param>
/// <param name="Height">Height in DIPs.</param>
public readonly record struct WidgetGeometry(double X, double Y, double Width, double Height);

/// <summary>
/// Outcome of an edit-mode save or exit attempt (RF-026/RF-077).
/// </summary>
public sealed record EditModeSaveResult
{
    /// <summary>Gets a value indicating whether a dirty layout was written through <c>IConfigStore</c>.</summary>
    public required bool Attempted { get; init; }

    /// <summary>
    /// Gets a value indicating whether the layout is persisted. When nothing was dirty the result
    /// is a success without an attempted save.
    /// </summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// Gets a value indicating whether the save was canceled. The layout stays dirty and is
    /// retried on the next exit or shutdown.
    /// </summary>
    public bool Canceled { get; init; }

    /// <summary>Result for the common "the layout was not dirty" case.</summary>
    public static EditModeSaveResult NothingToSave { get; } = new() { Attempted = false, Succeeded = true };
}

/// <summary>
/// Edit-mode state machine for the M1 panel (T-12, RF-026/RF-027/RF-039, DD-7): enter/exit,
/// per-widget drag/resize with live clamping against the page bounds and the 40 DIP minimum
/// (<see cref="ConfigValidator.MinWidgetSize"/>, D-9), selection for keyboard nudging, dirty
/// tracking, and save-on-exit through the injected <see cref="IConfigStore"/> (atomic; only when
/// dirty). The service mutates the widget view models (so the Canvas follows live) and, at save
/// time, writes their geometry back into the corresponding validated <see cref="WidgetConfig"/>
/// entries before persisting.
/// </summary>
/// <remarks>
/// <para><b>Focus semantics (D-6/RF-018).</b> Entering edit mode is a deliberate user action and
/// the composition root may activate the panel; <see cref="IsEditMode"/> flips back to
/// <see langword="false"/> synchronously on <see cref="ExitAsync"/> so non-activating behavior is
/// restored before any save I/O completes.</para>
/// <para><b>Failure behavior (RF-077).</b> A failed save keeps <see cref="IsDirty"/> set, leaves
/// both the view-model and configuration geometry intact in memory, is logged, is surfaced through
/// the optional <see cref="IUserNotifier"/>, and is retried on the next exit or shutdown.</para>
/// <para><b>Threading (RNF-004).</b> Geometry and state mutations happen on the UI thread; the
/// store call is awaited asynchronously and never blocks the dispatcher.</para>
/// </remarks>
public sealed class EditModeService : INotifyPropertyChanged
{
    /// <summary>Minimum width/height of a widget in edit mode (40 DIP, D-9/RF-031).</summary>
    public static readonly double MinimumWidgetSize = ConfigValidator.MinWidgetSize;

    /// <summary>
    /// Fixed offset applied to a duplicated widget so the copy is visibly distinct from its source
    /// (16 DIP, M2-RF-432). Shared with the T-13/T-15 edit UI.
    /// </summary>
    public const double DuplicateOffset = 16;

    /// <summary>
    /// Fixed default width of a newly added widget in DIPs (M2-RF-431), used unless the type's
    /// registered default is larger.
    /// </summary>
    public const double DefaultWidgetWidth = 160;

    /// <summary>
    /// Fixed default height of a newly added widget in DIPs (M2-RF-431), used unless the type's
    /// registered default is larger.
    /// </summary>
    public const double DefaultWidgetHeight = 120;

    /// <summary>
    /// Placement grid step in DIPs used when scanning for the first free position (M2-RF-431,
    /// referencing the M2-RF-441 grid). It matches the default edit grid size (8 DIP); the
    /// configurable grid arrives with T-15.
    /// </summary>
    private const double PlacementGridStep = 8;

    private readonly PanelConfig _config;
    private PageViewModel _page;
    private readonly IConfigStore _configStore;
    private readonly IWidgetViewModelFactory? _factory;
    private readonly IWidgetPaletteSource? _palette;
    private readonly Func<PanelConfig, PageViewModel?>? _pageRebuilder;
    private readonly EditHistory _history = new();
    private readonly ILogger _logger;
    private readonly IUserNotifier? _notifier;
    private bool _isEditMode;
    private bool _isDirty;
    private bool _isSnapEnabled = true;
    private bool _isEditMenuVisible = true;
    private double _gridSize = GridSnap.DefaultGridSize;
    private IReadOnlyList<GuideLine> _activeGuides = [];
    private WidgetViewModelBase? _selectedWidget;
    private PropertyEditorViewModel? _propertyEditor;
    private WidgetGeometry? _lastAddedGeometry;
    private readonly IExecutablePicker? _executablePicker;
    private readonly IClockCultureSource? _clockCulture;
    private readonly ISensorCatalog? _sensorCatalog;
    private readonly IImageFilePicker? _imageFilePicker;
    private readonly AppPresetDetector _appPresetDetector;
    private bool _isDeleteConfirmationVisible;
    private double _pageWidth;
    private double _pageHeight;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditModeService"/> class for the active page of
    /// the loaded configuration.
    /// </summary>
    /// <param name="config">The validated configuration the page belongs to; must not be <see langword="null"/>.</param>
    /// <param name="page">The rendered active page whose widgets are edited; must not be <see langword="null"/>.</param>
    /// <param name="configStore">The single-writer configuration boundary used to persist the layout (RF-080).</param>
    /// <param name="logger">Logger for save outcomes; defaults to the global Serilog logger.</param>
    /// <param name="notifier">Notification boundary for save failures (RF-077); optional in tests.</param>
    /// <param name="factory">Widget factory used to materialize an added/duplicated widget's view model; optional, so display-only callers degrade add/duplicate to a no-op.</param>
    /// <param name="palette">Catalog of addable built-in widget types (M2-RF-430/M2-RF-431); optional, so callers that do not edit show an empty palette.</param>
    /// <param name="executablePicker">Executable browse seam for the run-command action picker (M2-RF-434); optional, so display-only callers can hand-edit the path.</param>
    /// <param name="clockCulture">
    /// The canonical Clock display-culture source (M4-RF-951) supplied to the property editor so a
    /// custom Clock format is validated with the same rule and culture the load-time validator and
    /// the Clock widget use; when <see langword="null"/>, <see cref="ClockCultureSource.Default"/>
    /// is used.
    /// </param>
    /// <param name="sensorCatalog">
    /// The WPF-free sensor catalog (M5-RF-510) supplied to the property editor so gauge/sparkline/bar
    /// expose a selectable sensor list; when <see langword="null"/>, the editor falls back to a
    /// catalog without a snapshot source (the fixed aggregate keys).
    /// </param>
    /// <param name="imageFilePicker">
    /// The image-file browse seam supplied to the property editor so the Image widget's path can be
    /// chosen without hand-editing it (user-requested addition); optional, so display-only callers
    /// can hand-edit the path.
    /// </param>
    /// <param name="pageRebuilder">
    /// Optional composition-root seam used by undo/redo to adopt a freshly rebuilt page view model
    /// (window data context, active-page bookkeeping). When omitted, the service rebuilds the page
    /// itself through <paramref name="factory"/>; when neither is available, undo/redo restores the
    /// configuration but reports that the live page could not be rebuilt.
    /// </param>
    /// <param name="appPresetDetector">
    /// Injectable preset-app detector (user-requested Apps row) so tests can supply fake file and
    /// directory probes; when <see langword="null"/>, the real filesystem detector is used.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required dependency is <see langword="null"/>.</exception>
    public EditModeService(
        PanelConfig config,
        PageViewModel page,
        IConfigStore configStore,
        ILogger? logger = null,
        IUserNotifier? notifier = null,
        IWidgetViewModelFactory? factory = null,
        IWidgetPaletteSource? palette = null,
        IExecutablePicker? executablePicker = null,
        IClockCultureSource? clockCulture = null,
        ISensorCatalog? sensorCatalog = null,
        IImageFilePicker? imageFilePicker = null,
        Func<PanelConfig, PageViewModel?>? pageRebuilder = null,
        AppPresetDetector? appPresetDetector = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(configStore);

        _config = config;
        _page = page;
        _configStore = configStore;
        _pageRebuilder = pageRebuilder;
        _logger = (logger ?? Log.Logger).ForContext<EditModeService>();
        _notifier = notifier;
        _factory = factory;
        _palette = palette;
        _executablePicker = executablePicker;
        _clockCulture = clockCulture;
        _sensorCatalog = sensorCatalog;
        _imageFilePicker = imageFilePicker;
        _appPresetDetector = appPresetDetector ?? new AppPresetDetector();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets a value indicating whether the panel is currently in edit mode (RF-026).</summary>
    public bool IsEditMode => _isEditMode;

    /// <summary>
    /// Gets a value indicating whether the layout has unsaved geometry changes. It stays set when a
    /// save fails so the change is retried on the next exit or shutdown (RF-077).
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>Gets the widget view models of the edited page in configuration order (overlay items source).</summary>
    public IReadOnlyList<WidgetViewModelBase> Widgets => _page.Widgets;

    /// <summary>
    /// Gets the addable built-in widget types offered by the edit-mode palette (RF-043/
    /// M2-RF-430/M2-RF-431). Empty when no palette source was supplied; unknown types never appear
    /// here and are therefore not offered for add.
    /// </summary>
    public IReadOnlyList<WidgetPaletteItem> PaletteItems => _palette?.BuiltInWidgets ?? [];

    /// <summary>
    /// Gets the preset applications offered by the edit-mode Apps row (user-requested preset app
    /// buttons). The catalog is static data; tapping an entry resolves its executable (or URI
    /// fallback) and adds a normal image-button configuration through the same add path as the
    /// widget palette.
    /// </summary>
    public IReadOnlyList<AppPreset> AppPresets => AppPresetCatalog.All;

    /// <summary>
    /// Gets a value indicating whether the selected widget can be removed or duplicated right now
    /// (edit mode active with a selection). Drives the touch toolbar buttons' enabled state; the
    /// underlying commands re-check the same condition.
    /// </summary>
    public bool CanModifySelection => _isEditMode && _selectedWidget is not null;

    /// <summary>
    /// Gets a value indicating whether the current edit session has a change that can be undone
    /// (user-requested undo/redo). Always <see langword="false"/> outside edit mode; drives the
    /// toolbar buttons' enabled state and the <c>Ctrl+Z</c> local shortcut.
    /// </summary>
    public bool CanUndo => _isEditMode && _history.CanUndo;

    /// <summary>
    /// Gets a value indicating whether an undone change can be re-applied (user-requested
    /// undo/redo). Always <see langword="false"/> outside edit mode; drives the toolbar buttons'
    /// enabled state and the <c>Ctrl+Y</c> local shortcut.
    /// </summary>
    public bool CanRedo => _isEditMode && _history.CanRedo;

    /// <summary>
    /// Gets the number of undo steps available in the current edit session (bounded to
    /// <see cref="EditHistory.DefaultCapacity"/>). Exposed for deterministic tests and diagnostics.
    /// </summary>
    public int UndoDepth => _history.UndoCount;

    /// <summary>
    /// Gets the number of redo steps available in the current edit session.
    /// </summary>
    public int RedoDepth => _history.RedoCount;

    /// <summary>
    /// Gets a value indicating whether the inline "Are you sure?" delete confirmation is open
    /// (M5-RF-502). When <see langword="true"/> the edit overlay shows the confirm surface;
    /// confirming routes to <see cref="RemoveSelectedWidget"/>, cancelling changes nothing.
    /// </summary>
    public bool IsDeleteConfirmationVisible => _isDeleteConfirmationVisible;

    /// <summary>
    /// Gets a value indicating whether grid snapping and snap-to-alignment are enabled for the
    /// current edit session (RF-044/M2-RF-441/M2-RF-444). The default is <see langword="true"/>; it
    /// is a session toggle flipped by the fixed <c>Ctrl+G</c> local shortcut (RF-060/M2-RF-603), by
    /// the touch toggle in the overlay, and reset when a new edit session starts. When off, dragging
    /// is free positioning (M2-RF-445) and no guides are shown.
    /// </summary>
    public bool IsSnapEnabled => _isSnapEnabled;

    /// <summary>
    /// Gets a value indicating whether the edit-mode toolbar (action cluster, widget palette, and
    /// Apps row) is expanded. It is <see langword="true"/> by default and is a session-only,
    /// transient UI state: it is never persisted, resets to visible when a new edit session starts,
    /// and returns to visible on exit so it cannot leak into normal mode. Hiding it gives the whole
    /// page back while editing; a compact restore affordance stays reachable at the bottom.
    /// </summary>
    public bool IsEditMenuVisible => _isEditMenuVisible;

    /// <summary>
    /// Gets the inverse of <see cref="IsEditMenuVisible"/>, used by the compact restore affordance
    /// shown in the toolbar's bottom area while the edit menu is hidden. Kept in sync with
    /// <see cref="IsEditMenuVisible"/> through the same change notification.
    /// </summary>
    public bool IsEditMenuHidden => !_isEditMenuVisible;

    /// <summary>
    /// Gets the edit grid size in DIPs used by drag/resize snapping and grid nudging
    /// (RF-044/M2-RF-441). It is bounded to <c>[2, 64]</c> DIP and defaults to
    /// <see cref="GridSnap.DefaultGridSize"/> (8). The value is session-only: it is not part of the
    /// persisted configuration, which is unchanged by T-15.
    /// </summary>
    public double GridSize => _gridSize;

    /// <summary>
    /// Gets the alignment guides to render for the in-progress drag (RF-044/M2-RF-444). The list is
    /// empty outside an active snapping drag; guides are transient and never persisted. A
    /// non-positive page extent disables the page-edge/center targets for that axis.
    /// </summary>
    public IReadOnlyList<GuideLine> ActiveGuides => _activeGuides;

    /// <summary>Gets the widget selected for keyboard nudging, or <see langword="null"/> when none is selected.</summary>
    public WidgetViewModelBase? SelectedWidget => _selectedWidget;

    /// <summary>
    /// Gets the property editor for the current selection, or <see langword="null"/> when no
    /// editable widget is selected (RF-043/M2-RF-433). The WPF property panel binds to this and
    /// collapses while it is <see langword="null"/>; unknown widget types (which preserve their raw
    /// JSON) never get an editor.
    /// </summary>
    public PropertyEditorViewModel? PropertyEditor => _propertyEditor;

    /// <summary>Gets a value indicating whether a property editor is active for the current selection.</summary>
    public bool HasPropertyEditor => _propertyEditor is not null;

    /// <summary>Gets the current page width in DIPs (the horizontal clamp bound).</summary>
    public double PageWidth => _pageWidth;

    /// <summary>
    /// Gets the canonical id of the active theme (M5-RF-541), used to seed new widgets and to supply
    /// the property editor's inherited appearance values. It is read from the effective configuration
    /// so a live theme change is reflected on the next add/edit.
    /// </summary>
    public string ActiveThemeId => _config.Appearance.Theme;

    /// <summary>Gets the current page height in DIPs (the vertical clamp bound).</summary>
    public double PageHeight => _pageHeight;

    /// <summary>
    /// Enters edit mode and records the page size in DIPs used for clamping (RF-039). The caller
    /// (composition root) is responsible for the deliberate activation allowed by D-6 and for
    /// ensuring the panel is visible. Repeated calls only refresh the page bounds.
    /// </summary>
    /// <param name="pageWidthDip">The visible page width in DIPs; non-finite or non-positive values degrade to "no practical bound".</param>
    /// <param name="pageHeightDip">The visible page height in DIPs; non-finite or non-positive values degrade to "no practical bound".</param>
    public void Enter(double pageWidthDip, double pageHeightDip)
    {
        SetPageBounds(pageWidthDip, pageHeightDip);
        SelectWidget(null);
        CancelRemove();

        if (_isEditMode)
        {
            return;
        }

        _isEditMode = true;

        // User-requested hide/show edit menu: every new edit session starts with the toolbar
        // expanded so the affordances are discoverable and a hidden menu never survives a session.
        SetEditMenuVisibleCore(true);

        // M2-RF-439: edit mode owns taps; suppress widget activation for the edited page.
        foreach (var widget in _page.Widgets)
        {
            widget.SetEditMode(true);
        }

        // RF-044/M2-RF-441: snap is a per-session toggle that starts enabled in each edit session.
        _isSnapEnabled = true;
        SetGuides([]);
        OnPropertyChanged(nameof(IsSnapEnabled));
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(CanModifySelection));

        // User-requested undo/redo: history is per edit session, so entering starts from the
        // current page state and discards anything from a previous session.
        ResetHistory();
    }

    /// <summary>
    /// Refreshes the DIP page bounds used by the clamping helpers, for example after a live
    /// display/DPI change while edit mode is active (RF-022). Widget geometry is not moved; the
    /// new bounds apply to subsequent moves and resizes.
    /// </summary>
    /// <param name="pageWidthDip">The visible page width in DIPs.</param>
    /// <param name="pageHeightDip">The visible page height in DIPs.</param>
    public void SetPageBounds(double pageWidthDip, double pageHeightDip)
    {
        _pageWidth = NormalizePageExtent(pageWidthDip);
        _pageHeight = NormalizePageExtent(pageHeightDip);
    }

    /// <summary>
    /// Selects (or clears) the widget that arrow-key nudging targets (RF-027). Selection is visual
    /// state owned by the widget view model; widgets that are not part of the edited page are
    /// ignored.
    /// </summary>
    /// <param name="widget">The widget to select, or <see langword="null"/> to clear the selection.</param>
    public void SelectWidget(WidgetViewModelBase? widget)
    {
        if (widget is not null && !_page.Widgets.Contains(widget))
        {
            return;
        }

        if (ReferenceEquals(_selectedWidget, widget))
        {
            return;
        }

        _selectedWidget?.SetSelected(false);
        _selectedWidget = widget;
        widget?.SetSelected(true);

        // M5-RF-502 edge case: a delete confirmation opened for the previous selection is cancelled
        // as soon as the selection changes so it can never delete a widget it was not opened for.
        CancelRemove();

        // UX fix #1: selecting a widget only enables move/resize and the per-widget affordances; it
        // must not auto-open the edit menu. The overlay's "Edit" button calls OpenPropertyEditor.
        ClosePropertyEditor();
        OnPropertyChanged(nameof(SelectedWidget));
        OnPropertyChanged(nameof(CanModifySelection));
    }

    /// <summary>
    /// Opens the property editor for the current selection (UX fix #1). Selection alone never opens
    /// the edit menu; the edit overlay's per-widget <b>Edit</b> affordance calls this. An editor is
    /// created only while edit mode is active with an editable widget selected (unknown widget types
    /// never get one); re-opening for the same selection keeps the existing editor instance.
    /// </summary>
    /// <returns><see langword="true"/> when an editor is open for the current selection.</returns>
    public bool OpenPropertyEditor()
    {
        if (!_isEditMode || _selectedWidget is null)
        {
            return false;
        }

        RefreshPropertyEditor();
        return _propertyEditor is not null;
    }

    /// <summary>
    /// Creates, keeps, or clears the property editor for the current selection (RF-043/M2-RF-433).
    /// An editor exists only while edit mode is active with an editable widget selected; unknown
    /// widget types (preserved raw JSON) never get one. Re-selecting the same configuration keeps
    /// the existing editor so its field state survives a live refresh.
    /// </summary>
    private void RefreshPropertyEditor()
    {
        var widget = _selectedWidget;
        WidgetConfig? config = null;
        if (_isEditMode && widget is not null && widget.CanEditGeometry)
        {
            TryFindConfig(widget, out _, out config);
        }

        if (config is null)
        {
            ClosePropertyEditor();
            return;
        }

        if (_propertyEditor is not null && ReferenceEquals(_propertyEditor.Config, config))
        {
            return;
        }

        _propertyEditor = new PropertyEditorViewModel(this, widget!, config, _executablePicker, _logger, _clockCulture, _sensorCatalog, _config.Hotkeys, _imageFilePicker);
        OnPropertyChanged(nameof(PropertyEditor));
        OnPropertyChanged(nameof(HasPropertyEditor));
    }

    /// <summary>
    /// Closes the property editor (UX fix #1/#3b). The editor is transient overlay state, so closing
    /// it changes nothing about the configuration, the selection, the dirty state, or edit mode;
    /// the state-change notifications are raised only when an editor was actually open. It backs the
    /// edit menu's Close button and stays safe to call when no editor is open.
    /// </summary>
    public void ClosePropertyEditor()
    {
        if (_propertyEditor is null)
        {
            return;
        }

        // User-requested live font-size preview: a selection change or an explicit close while the
        // field is still focused ends the live session cleanly (the pending value is committed)
        // instead of leaving a preview without its normalization/error handling.
        _propertyEditor.EndLiveEdit();

        _propertyEditor = null;
        OnPropertyChanged(nameof(PropertyEditor));
        OnPropertyChanged(nameof(HasPropertyEditor));
    }

    /// <summary>
    /// Applies a validated property edit from the property editor (RF-043/M2-RF-433): the change
    /// is projected onto the live widget view model (no rebuild) and the layout is marked dirty so
    /// the existing single-writer save-on-exit persists it (M2-RF-437). No-op outside edit mode or
    /// for a widget that is not on the edited page.
    /// </summary>
    /// <param name="widget">The edited widget.</param>
    /// <param name="config">The configuration entry the editor mutated.</param>
    /// <param name="recordUndo">
    /// When <see langword="true"/> (the default), the edit becomes a new undo step. When
    /// <see langword="false"/>, the preview is applied and the history's current snapshot is updated
    /// in place without adding a step, so a multi-keystroke live edit stays one undo group
    /// (user-requested live font-size preview).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an undo step was created (only possible with
    /// <paramref name="recordUndo"/>); <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    internal bool ApplyPropertyEdit(WidgetViewModelBase widget, WidgetConfig config, bool recordUndo = true)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(config);

        if (!_isEditMode || !_page.Widgets.Contains(widget))
        {
            return false;
        }

        try
        {
            widget.ApplyConfig(config);
        }
        catch (Exception ex)
        {
            // M2-RF-440/RF-012: a projection failure degrades the live refresh (the config still
            // holds the committed value and is saved); it must not take down edit mode.
            _logger.Warning(ex, "Edit mode: applying a property edit failed for widget {WidgetId}", widget.WidgetId);
        }

        SetDirty(true);
        return recordUndo ? RecordUndoPoint() : UpdateUndoCurrent();
    }

    /// <summary>
    /// Moves a widget to an absolute position, clamped to the page bounds (RF-039). The current
    /// extents are preserved. No-op outside edit mode or for widgets that are not on the edited
    /// page.
    /// </summary>
    /// <param name="widget">The widget to move; must belong to the edited page.</param>
    /// <param name="x">The requested horizontal position in DIPs; hostile values are normalized.</param>
    /// <param name="y">The requested vertical position in DIPs; hostile values are normalized.</param>
    /// <returns><see langword="true"/> when the geometry actually changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    public bool MoveWidget(WidgetViewModelBase widget, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!IsEditable(widget)
            || !ApplyGeometry(widget, ClampMove(x, y, widget.Width, widget.Height, _pageWidth, _pageHeight)))
        {
            return false;
        }

        RecordUndoPoint();
        return true;
    }

    /// <summary>
    /// Moves a widget like <see cref="MoveWidget"/> but lets a live numeric-field preview keep one
    /// undo group: the session's first change records the step and later changes only advance the
    /// current snapshot. Internal because it exists solely for the property editor.
    /// </summary>
    /// <param name="widget">The widget to move.</param>
    /// <param name="x">The requested horizontal position.</param>
    /// <param name="y">The requested vertical position.</param>
    /// <param name="recordUndo">Whether this change may record a new undo step.</param>
    /// <returns><see langword="true"/> when a new undo step was recorded.</returns>
    internal bool MoveWidgetLive(WidgetViewModelBase widget, double x, double y, bool recordUndo)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!IsEditable(widget))
        {
            return false;
        }

        if (!ApplyGeometry(widget, ClampMove(x, y, widget.Width, widget.Height, _pageWidth, _pageHeight)))
        {
            return false;
        }

        return recordUndo ? RecordUndoPoint() : UpdateUndoCurrent();
    }

    /// <summary>
    /// Moves a widget by a relative drag delta with live clamping, so an out-of-bounds drag pins the
    /// widget at the edge instead of allowing it to leave the page (RF-039). When snapping is
    /// enabled the requested position is first snapped to the grid (origin always snaps,
    /// M2-RF-441), then a nearby edge/center alignment overrides it (M2-RF-444), and finally the
    /// composed position is clamped into the page (M2-RF-442); the alignment guides for the resolved
    /// alignment are exposed through <see cref="ActiveGuides"/>. When snapping is disabled or the
    /// caller passes the free-position override, the drag is free positioning (M2-RF-445) and no
    /// guides are shown.
    /// </summary>
    /// <param name="widget">The widget to move; must belong to the edited page.</param>
    /// <param name="deltaX">Horizontal drag delta in DIPs.</param>
    /// <param name="deltaY">Vertical drag delta in DIPs.</param>
    /// <param name="freeOverride">When <see langword="true"/>, bypasses snapping and guides for this drag (modifier-key free positioning, M2-RF-445).</param>
    /// <returns><see langword="true"/> when the geometry actually changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    public bool MoveWidgetBy(WidgetViewModelBase widget, double deltaX, double deltaY, bool freeOverride = false)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!IsEditable(widget))
        {
            return false;
        }

        var x = widget.X + deltaX;
        var y = widget.Y + deltaY;
        var width = widget.Width;
        var height = widget.Height;

        if (!_isSnapEnabled || freeOverride)
        {
            SetGuides([]);
            return ApplyGeometry(widget, ClampMove(x, y, width, height, _pageWidth, _pageHeight));
        }

        // M2-RF-442/M2-RF-446: snap to the grid first (the origin always snaps), then let a nearby
        // edge/center alignment override it, then clamp — so the result is always finite, in-page,
        // and at least the 40 DIP minimum.
        x = GridSnap.Snap(x, _gridSize);
        y = GridSnap.Snap(y, _gridSize);

        var solution = AlignmentGuides.Resolve(
            x,
            y,
            width,
            height,
            BuildAlignmentTargets(widget),
            _pageWidth,
            _pageHeight,
            AlignmentGuides.Threshold);

        x += solution.DeltaX;
        y += solution.DeltaY;
        SetGuides(solution.Guides);

        return ApplyGeometry(widget, ClampMove(x, y, width, height, _pageWidth, _pageHeight));
    }

    /// <summary>
    /// Marks the start of a drag gesture (RF-044/M2-RF-444): the pressed widget is selected and any
    /// stale guides from a previous drag are cleared. Purely visual; no geometry or persistence
    /// changes.
    /// </summary>
    /// <param name="widget">The widget whose drag is starting; must belong to the edited page.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    public void BeginDrag(WidgetViewModelBase widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!IsEditable(widget))
        {
            return;
        }

        SelectWidget(widget);
        SetGuides([]);
    }

    /// <summary>
    /// Marks the end of a drag gesture (RF-044/M2-RF-444): guides are rendered only while dragging
    /// and are never persisted, so they are cleared here. Releasing a drag does not change geometry;
    /// the whole gesture becomes a single undo step (user-requested undo/redo) when it moved or
    /// resized the widget, instead of one step per drag delta.
    /// </summary>
    public void EndDrag()
    {
        SetGuides([]);
        RecordUndoPoint();
    }

    /// <summary>
    /// Resizes a widget, clamped to the page bounds and the 40 DIP minimum (RF-039). The position
    /// is preserved; a resize that would leave the page limits the extent instead of moving the
    /// widget.
    /// </summary>
    /// <param name="widget">The widget to resize; must belong to the edited page.</param>
    /// <param name="width">The requested width in DIPs; hostile values are normalized.</param>
    /// <param name="height">The requested height in DIPs; hostile values are normalized.</param>
    /// <returns><see langword="true"/> when the geometry actually changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    public bool ResizeWidget(WidgetViewModelBase widget, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!IsEditable(widget))
        {
            return false;
        }

        if (!ApplyGeometry(widget, ClampResize(widget.X, widget.Y, width, height, _pageWidth, _pageHeight)))
        {
            return false;
        }

        RecordUndoPoint();
        return true;
    }

    /// <summary>
    /// Resizes a widget like <see cref="ResizeWidget"/> but lets a live numeric-field preview keep
    /// one undo group: the session's first change records the step and later changes only advance the
    /// current snapshot. Internal because it exists solely for the property editor.
    /// </summary>
    /// <param name="widget">The widget to resize.</param>
    /// <param name="width">The requested width.</param>
    /// <param name="height">The requested height.</param>
    /// <param name="recordUndo">Whether this change may record a new undo step.</param>
    /// <returns><see langword="true"/> when a new undo step was recorded.</returns>
    internal bool ResizeWidgetLive(WidgetViewModelBase widget, double width, double height, bool recordUndo)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!IsEditable(widget)
            || !ApplyGeometry(widget, ClampResize(widget.X, widget.Y, width, height, _pageWidth, _pageHeight)))
        {
            return false;
        }

        return recordUndo ? RecordUndoPoint() : UpdateUndoCurrent();
    }

    /// <summary>
    /// Resizes a widget by a relative resize-handle delta with live clamping (RF-039/M2-RF-441).
    /// When snapping is enabled the requested extents are snapped to the grid first (an extent
    /// smaller than one grid cell is left alone, M2-RF-442), then clamped with the 40 DIP minimum.
    /// Resizing does not show alignment guides; they are cleared.
    /// </summary>
    /// <param name="widget">The widget to resize; must belong to the edited page.</param>
    /// <param name="deltaWidth">Horizontal resize delta in DIPs.</param>
    /// <param name="deltaHeight">Vertical resize delta in DIPs.</param>
    /// <param name="freeOverride">When <see langword="true"/>, bypasses snapping for this resize (M2-RF-445).</param>
    /// <returns><see langword="true"/> when the geometry actually changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widget"/> is <see langword="null"/>.</exception>
    public bool ResizeWidgetBy(WidgetViewModelBase widget, double deltaWidth, double deltaHeight, bool freeOverride = false)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (!IsEditable(widget))
        {
            return false;
        }

        SetGuides([]);

        var width = widget.Width + deltaWidth;
        var height = widget.Height + deltaHeight;
        if (_isSnapEnabled && !freeOverride)
        {
            width = GridSnap.SnapExtent(width, _gridSize);
            height = GridSnap.SnapExtent(height, _gridSize);
        }

        return ApplyGeometry(widget, ClampResize(widget.X, widget.Y, width, height, _pageWidth, _pageHeight));
    }

    /// <summary>
    /// Nudges the selected widget (RF-027/M2-RF-443): with snap on the step is one grid size
    /// (<see cref="GridSize"/> DIP), with snap off it is the M1 default of 1 DIP. The move is
    /// clamped through the same page/minimum pipeline as a drag; guides are cleared. No-op when
    /// nothing is selected or edit mode is off.
    /// </summary>
    /// <param name="deltaX">Horizontal nudge direction/magnitude (typically -1, 0, or +1).</param>
    /// <param name="deltaY">Vertical nudge direction/magnitude (typically -1, 0, or +1).</param>
    /// <returns><see langword="true"/> when the geometry actually changed.</returns>
    public bool NudgeSelectedWidget(double deltaX, double deltaY)
    {
        var widget = _selectedWidget;
        if (widget is null || !IsEditable(widget))
        {
            return false;
        }

        SetGuides([]);
        var step = _isSnapEnabled ? GridSnap.NormalizeGridSize(_gridSize) : 1;
        var geometry = ClampMove(
            widget.X + (Math.Sign(deltaX) * step),
            widget.Y + (Math.Sign(deltaY) * step),
            widget.Width,
            widget.Height,
            _pageWidth,
            _pageHeight);
        if (!ApplyGeometry(widget, geometry))
        {
            return false;
        }

        RecordUndoPoint();
        return true;
    }

    /// <summary>
    /// Toggles the edit-session grid snap on or off (RF-044/M2-RF-441, driven by the fixed
    /// <c>Ctrl+G</c> local shortcut, RF-060, or the overlay touch toggle). No-op outside edit mode
    /// so the shortcut stays inert in normal operation (M2-RF-602/M2-RF-604).
    /// </summary>
    /// <returns><see langword="true"/> when the toggle was applied (edit mode active).</returns>
    public bool ToggleSnap() => _isEditMode && SetSnapEnabled(!_isSnapEnabled);

    /// <summary>
    /// Sets the edit-session grid snap explicitly (RF-044/M2-RF-441/M2-RF-445), for example from a
    /// touch toggle. Turning snapping off clears any visible guides. No-op outside edit mode so the
    /// affordance stays inert in normal operation (M2-RF-602/M2-RF-604).
    /// </summary>
    /// <param name="enabled">Whether grid snapping and alignment should be active.</param>
    /// <returns><see langword="true"/> when the value was applied (edit mode active).</returns>
    public bool SetSnapEnabled(bool enabled)
    {
        if (!_isEditMode)
        {
            return false;
        }

        if (_isSnapEnabled != enabled)
        {
            _isSnapEnabled = enabled;
            OnPropertyChanged(nameof(IsSnapEnabled));
        }

        if (!enabled)
        {
            SetGuides([]);
        }

        return true;
    }

    /// <summary>
    /// Sets the edit grid size (RF-044/M2-RF-441), normalized to the supported <c>[2, 64]</c> DIP
    /// range. The value is session-only and never persisted.
    /// </summary>
    /// <param name="gridSize">The requested grid size in DIPs; hostile values fall back to the default.</param>
    public void SetGridSize(double gridSize)
    {
        var normalized = GridSnap.NormalizeGridSize(gridSize);
        if (_gridSize.Equals(normalized))
        {
            return;
        }

        _gridSize = normalized;
        OnPropertyChanged(nameof(GridSize));
    }

    /// <summary>
    /// Shows or hides the edit-mode toolbar from the touch affordances (user-requested hide/show
    /// edit menu). Hiding collapses the whole toolbar (action cluster, palette, and Apps row) so the
    /// page is fully visible; a compact <b>Edit menu</b> control stays in the same bottom area to
    /// restore it with one tap. The state is transient and session-scoped, never persisted. No-op
    /// outside edit mode so it cannot leak into normal mode.
    /// </summary>
    /// <param name="visible">Whether the edit-mode toolbar should be visible.</param>
    /// <returns><see langword="true"/> when the value was applied (edit mode active); a repeated call with the same value is an idempotent no-op.</returns>
    public bool SetEditMenuVisible(bool visible)
    {
        if (!_isEditMode)
        {
            return false;
        }

        SetEditMenuVisibleCore(visible);
        return true;
    }

    /// <summary>Shows the edit-mode toolbar; idempotent and inert outside edit mode.</summary>
    /// <returns><see langword="true"/> when the value was applied (edit mode active).</returns>
    public bool ShowEditMenu() => SetEditMenuVisible(true);

    /// <summary>Hides the edit-mode toolbar; idempotent and inert outside edit mode.</summary>
    /// <returns><see langword="true"/> when the value was applied (edit mode active).</returns>
    public bool HideEditMenu() => SetEditMenuVisible(false);

    /// <summary>
    /// Flips the edit-mode toolbar visibility (user-requested hide/show control). No-op outside edit
    /// mode so the affordance stays inert in normal operation.
    /// </summary>
    /// <returns><see langword="true"/> when the toggle was applied (edit mode active).</returns>
    public bool ToggleEditMenu() => _isEditMode && SetEditMenuVisible(!_isEditMenuVisible);

    private void SetEditMenuVisibleCore(bool visible)
    {
        if (_isEditMenuVisible == visible)
        {
            return;
        }

        _isEditMenuVisible = visible;
        OnPropertyChanged(nameof(IsEditMenuVisible));
        OnPropertyChanged(nameof(IsEditMenuHidden));
    }

    /// <summary>
    /// Removes the selected widget from the edited page and its page configuration (RF-060/
    /// M2-RF-603/M2-RF-432). The removal marks the layout dirty so the next save persists it through
    /// the single writer. No-op when edit mode is off or nothing is selected; the widget's view
    /// model is disposed.
    /// </summary>
    /// <returns><see langword="true"/> when a widget was removed.</returns>
    public bool RemoveSelectedWidget()
    {
        var widget = _selectedWidget;
        if (!_isEditMode || widget is null)
        {
            return false;
        }

        if (!TryFindConfig(widget, out var page, out var config))
        {
            return false;
        }

        SelectWidget(null);
        if (!_page.Remove(widget))
        {
            return false;
        }

        page.Widgets.Remove(config);
        SetDirty(true);
        OnPropertyChanged(nameof(Widgets));
        RecordUndoPoint();
        _logger.Information("Edit mode: removed the selected widget. WidgetId={WidgetId}", widget.WidgetId);
        return true;
    }

    /// <summary>
    /// Opens the inline "Are you sure?" delete confirmation for the current selection (M5-RF-502).
    /// It never deletes on its own: the caller must follow with <see cref="ConfirmRemove"/> or
    /// <see cref="CancelRemove"/>. No-op and returns <see langword="false"/> outside edit mode or
    /// without a selection, which also cancels any stale confirmation.
    /// </summary>
    /// <returns><see langword="true"/> when the confirmation is open for a removable selection.</returns>
    public bool RequestRemoveSelectedWidget()
    {
        if (!CanModifySelection)
        {
            CancelRemove();
            return false;
        }

        SetDeleteConfirmationVisible(true);
        return true;
    }

    /// <summary>
    /// Confirms the open delete confirmation (M5-RF-502): removes the selected widget through the
    /// existing <see cref="RemoveSelectedWidget"/> path (dirty + single-writer save on exit) and
    /// closes the confirmation. Returns <see langword="false"/> when no confirmation is open; a
    /// selection that is no longer removable simply cancels the confirmation without deleting.
    /// </summary>
    /// <returns><see langword="true"/> when a widget was removed.</returns>
    public bool ConfirmRemove()
    {
        if (!_isDeleteConfirmationVisible)
        {
            return false;
        }

        if (!CanModifySelection)
        {
            CancelRemove();
            return false;
        }

        var removed = RemoveSelectedWidget();
        CancelRemove();
        return removed;
    }

    /// <summary>
    /// Cancels the delete confirmation (M5-RF-502): the widget, the selection, and the dirty/
    /// configuration state are all left unchanged. Safe to call when no confirmation is open.
    /// </summary>
    public void CancelRemove() => SetDeleteConfirmationVisible(false);

    private void SetDeleteConfirmationVisible(bool visible)
    {
        if (_isDeleteConfirmationVisible == visible)
        {
            return;
        }

        _isDeleteConfirmationVisible = visible;
        OnPropertyChanged(nameof(IsDeleteConfirmationVisible));
    }

    /// <summary>
    /// Duplicates the selected widget (RF-060/M2-RF-603/M2-RF-432): the configuration is cloned, the
    /// copy receives a new unique id and the fixed <see cref="DuplicateOffset"/> (16 DIP, clamped into
    /// the page), the copy is appended to the page configuration, and its view model is materialized
    /// through the injected factory and selected. Marks the layout dirty so the save persists it
    /// through the single writer. No-op when edit mode is off, nothing is selected, or no factory was
    /// supplied.
    /// </summary>
    /// <returns><see langword="true"/> when a duplicate was added.</returns>
    public bool DuplicateSelectedWidget()
    {
        var widget = _selectedWidget;
        if (!_isEditMode || widget is null)
        {
            return false;
        }

        var factory = _factory;
        if (factory is null)
        {
            _logger.Warning("Edit mode: duplicating a widget requires the widget factory; the request was ignored");
            return false;
        }

        if (!TryFindConfig(widget, out var page, out var source))
        {
            return false;
        }

        WidgetConfig clone;
        try
        {
            // Cloning through the canonical JSON keeps every type-specific property (including the
            // raw JSON of an unknown widget type) without a per-type copy implementation.
            var json = JsonSerializer.Serialize(source, ConfigJson.Options);
            clone = JsonSerializer.Deserialize<WidgetConfig>(json, ConfigJson.Options)!;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Edit mode: duplicating the selected widget failed while cloning its configuration");
            return false;
        }

        clone.Id = GenerateUniqueId(page, source.Id);
        var geometry = ClampMove(
            source.X + DuplicateOffset,
            source.Y + DuplicateOffset,
            source.Width,
            source.Height,
            _pageWidth,
            _pageHeight);
        clone.X = geometry.X;
        clone.Y = geometry.Y;
        clone.Width = geometry.Width;
        clone.Height = geometry.Height;

        WidgetViewModelBase viewModel;
        try
        {
            page.Widgets.Add(clone);
            viewModel = factory.Create(clone, _page.PageId);
            viewModel.SetEditMode(true);
            _page.Add(viewModel);
        }
        catch (Exception ex)
        {
            // Never leave the config and the page out of step: drop the config entry if the view
            // model could not be built (RF-012/RF-013 containment).
            page.Widgets.Remove(clone);
            _logger.Warning(ex, "Edit mode: duplicating the selected widget failed while building its view model");
            return false;
        }

        SelectWidget(viewModel);
        OpenPropertyEditor();
        SetDirty(true);
        OnPropertyChanged(nameof(Widgets));
        RecordUndoPoint();
        _logger.Information(
            "Edit mode: duplicated the selected widget. SourceId={SourceId} NewId={NewId}",
            source.Id,
            clone.Id);
        return true;
    }

    /// <summary>
    /// Adds a new widget of a built-in type to the edited page (RF-043/M2-RF-431): the type must be
    /// one of the palette's built-in registrations (unknown types are never addable, M2-RF-430), a
    /// unique id is generated within the page, the configuration receives the type's default
    /// properties and a default geometry of <c>160×120 DIP</c> (or the type's larger registered
    /// default), and the widget is placed at the first free position scanning the page
    /// top-left→bottom-right on the grid. When no position fits, the widget is cascaded by
    /// <see cref="DuplicateOffset"/> from the last added widget (or placed at the origin when that
    /// is also out of bounds). The new widget is appended to the live page, selected, and the layout
    /// is marked dirty so the existing single-writer save persists it (M2-RF-437). No-op outside
    /// edit mode, without a factory/palette, or for an unknown type.
    /// </summary>
    /// <param name="type">The config <c>type</c> discriminator to add.</param>
    /// <returns><see langword="true"/> when a widget was added.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
    public bool AddWidget(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!_isEditMode)
        {
            return false;
        }

        var palette = _palette;
        var factory = _factory;
        if (palette is null || factory is null)
        {
            _logger.Warning("Edit mode: adding a widget requires the palette and the widget factory; the request was ignored");
            return false;
        }

        var item = palette.BuiltInWidgets.FirstOrDefault(candidate =>
            string.Equals(candidate.Type, type, StringComparison.Ordinal));
        if (item is null)
        {
            // M2-RF-430: unknown/unsupported types are not offered for add.
            _logger.Warning("Edit mode: widget type {WidgetType} is not an addable built-in; the request was ignored", type);
            return false;
        }

        if (!palette.TryCreateDefaultWidget(type, out var config) || config is null)
        {
            _logger.Warning("Edit mode: no default configuration is available for widget type {WidgetType}", type);
            return false;
        }

        var width = Math.Max(DefaultWidgetWidth, item.DefaultWidth > 0 ? item.DefaultWidth : 0);
        var height = Math.Max(DefaultWidgetHeight, item.DefaultHeight > 0 ? item.DefaultHeight : 0);
        return AddWidgetConfig(config, width, height);
    }

    /// <summary>
    /// Adds a preset application button to the edited page (user-requested Apps row). The preset's
    /// executable candidates are probed in order; when none exists, a URI app falls back to the
    /// Windows shell, and an app without a working launch target is still created with an empty
    /// executable so the user can Browse for it (never throws). The resulting image-button
    /// configuration goes through the exact same placement, id generation,
    /// eager view-model creation, selection, dirty tracking, single-writer save, and undo path as
    /// <see cref="AddWidget(string)"/>.
    /// </summary>
    /// <param name="preset">The preset application to add; must not be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a button was added.</returns>
    public bool AddAppPreset(AppPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (!_isEditMode)
        {
            return false;
        }

        if (_factory is null)
        {
            _logger.Warning("Edit mode: adding a preset app requires the widget factory; the request was ignored");
            return false;
        }

        var launch = _appPresetDetector.Resolve(preset);
        if (!launch.HasExecutable)
        {
            _logger.Warning(
                "Edit mode: no executable or fallback URI was found for preset app {AppPreset}; the button is added without a launch path",
                preset.Id);
            _notifier?.Notify(
                "App not found",
                $"No executable was found for {preset.DisplayName}. The button was added; use Browse to pick the program.",
                NotificationSeverity.Info);
        }
        else if (launch.UsedFallbackUri)
        {
            _logger.Information(
                "Edit mode: preset app {AppPreset} was not found; using the shell URI {Uri}",
                preset.Id,
                launch.FallbackUri);
        }

        var config = AppPresetWidgetFactory.Create(preset, launch);
        return AddWidgetConfig(config, AppPresetWidgetFactory.ButtonSize, AppPresetWidgetFactory.ButtonSize);
    }

    /// <summary>
    /// The shared add pipeline used by the widget palette and the preset Apps row: theme seeding,
    /// unique id, clamped placement, eager view-model materialization (with rollback), selection,
    /// dirty tracking, notification, save-through-single-writer on exit, and an undo point.
    /// </summary>
    /// <param name="config">The new configuration with type-specific defaults applied.</param>
    /// <param name="width">The requested width in DIPs.</param>
    /// <param name="height">The requested height in DIPs.</param>
    /// <returns><see langword="true"/> when the widget was added.</returns>
    private bool AddWidgetConfig(WidgetConfig config, double width, double height)
    {
        var factory = _factory;
        if (factory is null)
        {
            _logger.Warning("Edit mode: adding a widget requires the widget factory; the request was ignored");
            return false;
        }

        if (!TryGetPageConfig(out var page))
        {
            return false;
        }

        ApplyThemeSeeding(config);

        var geometry = PlaceNewWidget(width, height);

        config.Id = GenerateUniqueWidgetId(page, config.Type);
        config.X = geometry.X;
        config.Y = geometry.Y;
        config.Width = geometry.Width;
        config.Height = geometry.Height;

        WidgetViewModelBase viewModel;
        try
        {
            page.Widgets.Add(config);
            viewModel = factory.Create(config, _page.PageId);
            viewModel.SetEditMode(true);
            _page.Add(viewModel);
        }
        catch (Exception ex)
        {
            // Never leave the config and the page out of step (RF-012/RF-013).
            page.Widgets.Remove(config);
            _logger.Warning(ex, "Edit mode: adding a widget failed while building its view model");
            return false;
        }

        _lastAddedGeometry = geometry;
        SelectWidget(viewModel);
        OpenPropertyEditor();
        SetDirty(true);
        OnPropertyChanged(nameof(Widgets));
        RecordUndoPoint();
        _logger.Information(
            "Edit mode: added a widget. Type={WidgetType} WidgetId={WidgetId} X={X} Y={Y} Width={Width} Height={Height}",
            config.Type,
            config.Id,
            geometry.X,
            geometry.Y,
            geometry.Width,
            geometry.Height);
        return true;
    }

    /// <summary>
    /// Seeds a freshly created widget's appearance from the active theme (M5-RF-541, Q-M5-1): the
    /// theme-independent <see cref="WidgetConfig.FontSize"/> and <see cref="WidgetConfig.Opacity"/>
    /// plus the theme <see cref="WidgetConfig.Accent"/>, so a new widget is never blank or hard-coded.
    /// The type's registered defaults win when they already set a field; foreground/background stay
    /// inherited so a later theme switch keeps restyling the widget (M5-RF-543). Every seeded value
    /// is normalized with the <see cref="WidgetAppearance"/> rules, so invalid theme-default data can
    /// never produce an invalid config.
    /// </summary>
    /// <param name="config">The new widget configuration to seed in place; must not be <see langword="null"/>.</param>
    private void ApplyThemeSeeding(WidgetConfig config)
    {
        var defaults = ThemeAppearanceDefaults.For(_config.Appearance.Theme);

        if (config.FontSize is null)
        {
            config.FontSize = WidgetAppearance.NormalizeFontSize(defaults.FontSize);
        }

        if (config.Opacity is null)
        {
            config.Opacity = WidgetAppearance.NormalizeOpacity(defaults.Opacity);
        }

        if (config.Accent is null && WidgetAppearance.IsValidColor(defaults.Accent))
        {
            config.Accent = WidgetAppearance.NormalizeColor(defaults.Accent);
        }
    }

    /// <summary>
    /// Chooses the geometry for a newly added widget (M2-RF-431): the first free grid position that
    /// does not overlap an existing widget, or the fixed <see cref="DuplicateOffset"/> cascade from
    /// the last added widget when the page has no free position (falling back to the origin when the
    /// cascade is out of bounds). The chosen geometry is always finite, clamped into the page, and at
    /// least the 40 DIP minimum.
    /// </summary>
    /// <param name="width">The requested default width in DIPs.</param>
    /// <param name="height">The requested default height in DIPs.</param>
    /// <returns>The clamped geometry to apply to the new config and view model.</returns>
    private WidgetGeometry PlaceNewWidget(double width, double height)
    {
        var occupied = _page.Widgets
            .Select(widget => new WidgetGeometry(widget.X, widget.Y, widget.Width, widget.Height))
            .ToArray();

        if (TryFindFirstFreePosition(width, height, _pageWidth, _pageHeight, occupied, PlacementGridStep, out var x, out var y))
        {
            return ClampMove(x, y, width, height, _pageWidth, _pageHeight);
        }

        // No free position: cascade from the last added widget by the fixed 16 DIP offset.
        var candidateX = 0d;
        var candidateY = 0d;
        if (_lastAddedGeometry is { } last)
        {
            candidateX = last.X + DuplicateOffset;
            candidateY = last.Y + DuplicateOffset;
        }

        var cascade = ClampMove(candidateX, candidateY, width, height, _pageWidth, _pageHeight);
        if (!cascade.X.Equals(candidateX) || !cascade.Y.Equals(candidateY))
        {
            // The cascade fell out of bounds; place at the page origin instead (M2-RF-431).
            cascade = ClampMove(0, 0, width, height, _pageWidth, _pageHeight);
        }

        return cascade;
    }

    /// <summary>
    /// Finds the first free position for a widget on the placement grid (M2-RF-431): scans the page
    /// top-left→bottom-right in grid steps and returns the first candidate whose rectangle does not
    /// overlap any of <paramref name="occupied"/>. Edges may touch. Returns <see langword="false"/>
    /// when the page has no usable bounds, is smaller than the widget, or every candidate overlaps.
    /// Pure and deterministic so placement is unit-tested without a UI.
    /// </summary>
    /// <param name="width">The candidate width in DIPs.</param>
    /// <param name="height">The candidate height in DIPs.</param>
    /// <param name="pageWidth">The page width in DIPs.</param>
    /// <param name="pageHeight">The page height in DIPs.</param>
    /// <param name="occupied">The rectangles already occupied by existing widgets.</param>
    /// <param name="gridStep">The placement grid step in DIPs; hostile values fall back to 1 DIP.</param>
    /// <param name="x">The found horizontal position in DIPs.</param>
    /// <param name="y">The found vertical position in DIPs.</param>
    /// <returns><see langword="true"/> when a free position was found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="occupied"/> is <see langword="null"/>.</exception>
    public static bool TryFindFirstFreePosition(
        double width,
        double height,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<WidgetGeometry> occupied,
        double gridStep,
        out double x,
        out double y)
    {
        ArgumentNullException.ThrowIfNull(occupied);

        x = 0;
        y = 0;

        var pageW = NormalizePageExtent(pageWidth);
        var pageH = NormalizePageExtent(pageHeight);
        if (pageW <= 0 || pageH <= 0)
        {
            return false;
        }

        var candidateWidth = ClampExtent(width, pageW);
        var candidateHeight = ClampExtent(height, pageH);
        if (candidateWidth > pageW || candidateHeight > pageH)
        {
            return false;
        }

        var step = double.IsFinite(gridStep) && gridStep > 0 ? gridStep : 1;
        var maxX = pageW - candidateWidth;
        var maxY = pageH - candidateHeight;
        var columns = (int)Math.Floor(maxX / step);
        var rows = (int)Math.Floor(maxY / step);

        for (var row = 0; row <= rows; row++)
        {
            var candidateY = row * step;
            for (var column = 0; column <= columns; column++)
            {
                var candidateX = column * step;
                if (OverlapsAny(candidateX, candidateY, candidateWidth, candidateHeight, occupied))
                {
                    continue;
                }

                x = candidateX;
                y = candidateY;
                return true;
            }
        }

        return false;
    }

    private static bool OverlapsAny(
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<WidgetGeometry> occupied)
    {
        foreach (var other in occupied)
        {
            // Touching edges do not count as an overlap, so widgets may sit flush on the grid.
            if (x < other.X + other.Width
                && x + width > other.X
                && y < other.Y + other.Height
                && y + height > other.Y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Clamps a requested move: the extents are normalized first, then the position is pinned into
    /// the page. Hostile input (non-finite, negative, larger than the page) always produces a valid
    /// finite geometry.
    /// </summary>
    /// <param name="x">Requested horizontal position in DIPs.</param>
    /// <param name="y">Requested vertical position in DIPs.</param>
    /// <param name="width">Current width in DIPs.</param>
    /// <param name="height">Current height in DIPs.</param>
    /// <param name="pageWidth">Page width in DIPs.</param>
    /// <param name="pageHeight">Page height in DIPs.</param>
    /// <returns>The clamped geometry.</returns>
    public static WidgetGeometry ClampMove(
        double x,
        double y,
        double width,
        double height,
        double pageWidth,
        double pageHeight)
    {
        var pageW = NormalizePageExtent(pageWidth);
        var pageH = NormalizePageExtent(pageHeight);

        // A page at least one minimum wide caps the extent to the page; an unknown page (for
        // example before layout) only normalizes to a finite value of at least the touch minimum.
        var clampedWidth = ClampExtent(width, pageW >= MinimumWidgetSize ? pageW : double.PositiveInfinity);
        var clampedHeight = ClampExtent(height, pageH >= MinimumWidgetSize ? pageH : double.PositiveInfinity);

        // An unknown page does not collapse a finite requested position to the origin; it falls
        // back to the validator's absolute maximum so nothing becomes unreasonable.
        var maxX = pageW > 0 ? Math.Max(0, pageW - clampedWidth) : ConfigValidator.MaxWidgetExtent;
        var maxY = pageH > 0 ? Math.Max(0, pageH - clampedHeight) : ConfigValidator.MaxWidgetExtent;

        return new WidgetGeometry(
            ClampPosition(x, maxX),
            ClampPosition(y, maxY),
            clampedWidth,
            clampedHeight);
    }

    /// <summary>
    /// Clamps a requested resize: the position is pinned into the page first, then the extents are
    /// limited to the remaining page space but never below the 40 DIP minimum. A page smaller than
    /// the minimum cannot fully contain the widget; the minimum is preserved so the widget stays
    /// usable.
    /// </summary>
    /// <param name="x">Current horizontal position in DIPs.</param>
    /// <param name="y">Current vertical position in DIPs.</param>
    /// <param name="width">Requested width in DIPs.</param>
    /// <param name="height">Requested height in DIPs.</param>
    /// <param name="pageWidth">Page width in DIPs.</param>
    /// <param name="pageHeight">Page height in DIPs.</param>
    /// <returns>The clamped geometry.</returns>
    public static WidgetGeometry ClampResize(
        double x,
        double y,
        double width,
        double height,
        double pageWidth,
        double pageHeight)
    {
        var pageW = NormalizePageExtent(pageWidth);
        var pageH = NormalizePageExtent(pageHeight);
        var maxX = pageW > 0 ? Math.Max(0, pageW - MinimumWidgetSize) : ConfigValidator.MaxWidgetExtent;
        var maxY = pageH > 0 ? Math.Max(0, pageH - MinimumWidgetSize) : ConfigValidator.MaxWidgetExtent;
        var clampedX = ClampPosition(x, maxX);
        var clampedY = ClampPosition(y, maxY);
        var maxWidth = pageW >= MinimumWidgetSize ? Math.Max(MinimumWidgetSize, pageW - clampedX) : double.PositiveInfinity;
        var maxHeight = pageH >= MinimumWidgetSize ? Math.Max(MinimumWidgetSize, pageH - clampedY) : double.PositiveInfinity;

        return new WidgetGeometry(
            clampedX,
            clampedY,
            ClampExtent(width, maxWidth),
            ClampExtent(height, maxHeight));
    }

    /// <summary>
    /// Clamps every widget's runtime geometry into the given page bounds after a display change
    /// (RF-022), reusing the edit-mode move-clamping rules: oversized extents are capped to the
    /// page and positions are pinned so nothing is left unreachable. The clamp is runtime-only: it
    /// mutates the widget view models but never marks the layout dirty, so geometry changes caused
    /// by a display change are never persisted implicitly (persistence stays tied to explicit
    /// edit-mode saves, RF-026/RF-039).
    /// </summary>
    /// <param name="widgets">The widgets of the rendered page.</param>
    /// <param name="pageWidthDip">The page width in DIPs (monitor physical width ÷ DPI scale).</param>
    /// <param name="pageHeightDip">The page height in DIPs (monitor physical height ÷ DPI scale).</param>
    /// <returns>The number of widgets whose geometry actually changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="widgets"/> is <see langword="null"/>.</exception>
    public static int ClampWidgetsToPage(
        IReadOnlyList<WidgetViewModelBase> widgets,
        double pageWidthDip,
        double pageHeightDip)
    {
        ArgumentNullException.ThrowIfNull(widgets);

        var changed = 0;
        foreach (var widget in widgets)
        {
            var clamped = ClampMove(widget.X, widget.Y, widget.Width, widget.Height, pageWidthDip, pageHeightDip);
            if (widget.X.Equals(clamped.X)
                && widget.Y.Equals(clamped.Y)
                && widget.Width.Equals(clamped.Width)
                && widget.Height.Equals(clamped.Height))
            {
                continue;
            }

            widget.ApplyGeometry(clamped.X, clamped.Y, clamped.Width, clamped.Height);
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// Exits edit mode and persists the layout only when it is dirty (RF-026/RF-039/RF-077). The
    /// edit-mode flag flips synchronously before any I/O, so non-activating behavior is restored
    /// immediately; the returned result reports whether a save was attempted and whether it
    /// succeeded.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save; the layout stays dirty and retryable.</param>
    /// <returns>The save outcome.</returns>
    public async Task<EditModeSaveResult> ExitAsync(CancellationToken cancellationToken = default)
    {
        if (_isEditMode)
        {
            _isEditMode = false;

            // M2-RF-439: restore normal widget activation as soon as editing ends.
            foreach (var widget in _page.Widgets)
            {
                widget.SetEditMode(false);
            }

            SetGuides([]);
            SelectWidget(null);
            CancelRemove();

            // User-requested hide/show edit menu: the transient hidden state never leaks into normal
            // mode; the next session starts expanded (also enforced on Enter).
            SetEditMenuVisibleCore(true);
            OnPropertyChanged(nameof(IsEditMode));

            // User-requested undo/redo: the history is per edit session and is discarded on exit;
            // the layout itself stays as edited (and dirty) for the normal save-on-exit path.
            ResetHistory();
        }

        return await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Persists the layout when dirty: the view-model geometry is written back into the matching
    /// <see cref="WidgetConfig"/> entries of the active page and saved atomically through
    /// <see cref="IConfigStore"/>. A failed or canceled save keeps the dirty flag set, leaves the
    /// in-memory geometry intact, logs, and notifies (RF-077); it never throws.
    /// </summary>
    /// <param name="cancellationToken">Cancels the save; the layout stays dirty and retryable.</param>
    /// <returns>The save outcome; <see cref="EditModeSaveResult.Attempted"/> is <see langword="false"/> when nothing was dirty.</returns>
    public async Task<EditModeSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!_isDirty)
        {
            return EditModeSaveResult.NothingToSave;
        }

        if (!TryApplyGeometryToConfig(out var page))
        {
            const string message = "The layout could not be saved because the edited page is not part of the configuration.";
            _logger.Error("Edit mode: {Message}", message);
            NotifySaveFailure(message);
            return new EditModeSaveResult { Attempted = true, Succeeded = false };
        }

        ConfigSaveResult result;
        try
        {
            result = await _configStore.SaveAsync(_config, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Edit mode: saving the layout was canceled; the layout stays dirty and can be retried");
            return new EditModeSaveResult { Attempted = true, Succeeded = false, Canceled = true };
        }
        catch (Exception ex)
        {
            // The store contract returns failures; this guards a custom implementation that throws.
            _logger.Error(ex, "Edit mode: saving the layout failed unexpectedly");
            NotifySaveFailure("The layout could not be saved; it will be retried on the next exit or shutdown.");
            return new EditModeSaveResult { Attempted = true, Succeeded = false };
        }

        if (!result.Succeeded)
        {
            var message = result.Diagnostics
                .FirstOrDefault(diagnostic => diagnostic.Severity == ConfigDiagnosticSeverity.Error)?.Message
                ?? "The layout could not be saved; it will be retried on the next exit or shutdown.";
            _logger.Warning("Edit mode: the layout save was refused: {Message}", message);
            NotifySaveFailure(message);
            return new EditModeSaveResult { Attempted = true, Succeeded = false };
        }

        SetDirty(false);
        _logger.Information("Edit mode: the layout was saved. PageId={PageId} Widgets={WidgetCount}", page.Id, _page.Widgets.Count);
        return new EditModeSaveResult { Attempted = true, Succeeded = true };
    }

    /// <summary>
    /// Restores the previous page state from the bounded edit-session history (user-requested
    /// undo/redo). The snapshot is written back into the active page configuration, the page view
    /// model is rebuilt through the existing apply/attach path, and the service stays in edit mode
    /// with the selection preserved by widget id when that widget still exists. The layout is marked
    /// dirty so the normal save-on-exit persists the restored state; no action is executed.
    /// </summary>
    /// <returns><see langword="true"/> when a state was restored.</returns>
    public bool TryUndo()
    {
        if (!_isEditMode)
        {
            return false;
        }

        // User-requested live font-size preview: finish any in-progress live session before choosing
        // the snapshot, so Ctrl+Z during typing undoes the whole group instead of racing a pending
        // commit that would record a point after the peek.
        _propertyEditor?.EndLiveEdit();

        if (!_history.TryPeekUndo(out var snapshot))
        {
            return false;
        }

        if (!TryRestoreSnapshot(snapshot))
        {
            return false;
        }

        _history.ApplyUndo();
        NotifyHistoryChanged();
        _logger.Information("Edit mode: undo restored the previous layout. UndoDepth={UndoDepth} RedoDepth={RedoDepth}", _history.UndoCount, _history.RedoCount);
        return true;
    }

    /// <summary>
    /// Re-applies a state that was undone (user-requested undo/redo): the page configuration is
    /// restored and the page view model rebuilt exactly like <see cref="TryUndo"/>, the layout is
    /// marked dirty, and edit mode stays active. No action is executed.
    /// </summary>
    /// <returns><see langword="true"/> when a state was restored.</returns>
    public bool TryRedo()
    {
        if (!_isEditMode)
        {
            return false;
        }

        // Finish an in-progress live session before checking the redo stack (same race as TryUndo).
        _propertyEditor?.EndLiveEdit();

        if (!_history.TryPeekRedo(out var snapshot))
        {
            return false;
        }

        if (!TryRestoreSnapshot(snapshot))
        {
            return false;
        }

        _history.ApplyRedo();
        NotifyHistoryChanged();
        _logger.Information("Edit mode: redo re-applied the layout. UndoDepth={UndoDepth} RedoDepth={RedoDepth}", _history.UndoCount, _history.RedoCount);
        return true;
    }

    /// <summary>
    /// Starts a fresh undo/redo session for the current edit-mode state; called on enter and exit so
    /// history never crosses sessions. The baseline snapshot is captured so the first edit has a
    /// state to restore to.
    /// </summary>
    private void ResetHistory()
    {
        _history.Reset(_isEditMode ? CapturePageSnapshot() : null);
        NotifyHistoryChanged();
    }

    /// <summary>
    /// Records the current page state as a new undo step after a committed edit. Identical states
    /// (for example an idempotent property apply or a drag without movement) are ignored, and a new
    /// edit clears the redo stack.
    /// </summary>
    /// <returns><see langword="true"/> when an undo step was created.</returns>
    private bool RecordUndoPoint()
    {
        if (!_isEditMode)
        {
            return false;
        }

        var snapshot = CapturePageSnapshot();
        if (snapshot is null || !_history.Record(snapshot))
        {
            return false;
        }

        NotifyHistoryChanged();
        return true;
    }

    /// <summary>
    /// Updates the history's current snapshot in place after an in-group live preview, without
    /// pushing a new undo step (user-requested live font-size preview). This keeps the group's single
    /// recorded undo point pointing at the state before the whole group while redo and a following
    /// group still see the latest applied value.
    /// </summary>
    /// <returns><see langword="true"/> when the current snapshot changed.</returns>
    private bool UpdateUndoCurrent()
    {
        if (!_isEditMode)
        {
            return false;
        }

        var snapshot = CapturePageSnapshot();
        return snapshot is not null && _history.ReplaceCurrent(snapshot);
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDepth));
        OnPropertyChanged(nameof(RedoDepth));
    }

    /// <summary>
    /// Captures the edited page as a canonical JSON snapshot (user-requested undo/redo): the page
    /// configuration is cloned through <see cref="ConfigJson"/> and the live widget geometry is
    /// overlaid, because moves and resizes update the view models and are only written back into the
    /// configuration at save time. Returns <see langword="null"/> when the page is not part of the
    /// configuration or the snapshot cannot be produced (isolation, RF-012).
    /// </summary>
    /// <returns>The serialized page snapshot, or <see langword="null"/> when unavailable.</returns>
    private string? CapturePageSnapshot()
    {
        if (!TryGetPageConfig(out var page))
        {
            return null;
        }

        try
        {
            var json = JsonSerializer.Serialize(page, ConfigJson.Options);
            var clone = JsonSerializer.Deserialize<PageConfig>(json, ConfigJson.Options);
            if (clone is null)
            {
                return null;
            }

            foreach (var widget in _page.Widgets)
            {
                var config = clone.Widgets.Find(candidate =>
                    string.Equals(candidate.Id, widget.WidgetId, StringComparison.Ordinal));
                if (config is null)
                {
                    continue;
                }

                config.X = widget.X;
                config.Y = widget.Y;
                config.Width = widget.Width;
                config.Height = widget.Height;
            }

            return JsonSerializer.Serialize(clone, ConfigJson.Options);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Edit mode: capturing an undo snapshot failed; the edit is not undoable");
            return null;
        }
    }

    /// <summary>
    /// Restores a page snapshot into the live configuration and rebuilds the page view model through
    /// the existing factory/attach path (user-requested undo/redo). The active page object is mutated
    /// in place, so the configuration the single-writer store serializes is the one that gets saved;
    /// the rebuilt page replaces the old one and stays in edit mode. Selection is preserved by
    /// widget id when possible and cleared deterministically otherwise; the transient property editor
    /// is closed because it points at the replaced configuration entries.
    /// </summary>
    /// <param name="snapshot">The serialized page snapshot to restore.</param>
    /// <returns><see langword="true"/> when the configuration and the live page were restored.</returns>
    private bool TryRestoreSnapshot(string snapshot)
    {
        if (_pageRebuilder is null && _factory is null)
        {
            _logger.Warning("Edit mode: undo/redo is unavailable because the page cannot be rebuilt");
            return false;
        }

        if (!TryGetPageConfig(out var page))
        {
            return false;
        }

        PageConfig? restored;
        try
        {
            restored = JsonSerializer.Deserialize<PageConfig>(snapshot, ConfigJson.Options);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Edit mode: reading an undo snapshot failed; the layout was left unchanged");
            return false;
        }

        if (restored is null || !string.Equals(restored.Id, page.Id, StringComparison.Ordinal))
        {
            _logger.Warning("Edit mode: the undo snapshot does not describe the edited page; the layout was left unchanged");
            return false;
        }

        var selectedId = _selectedWidget?.WidgetId;

        // Transient overlay state for the old page is closed first: the property editor holds the
        // configuration entries that this restore replaces.
        SelectWidget(null);
        SetGuides([]);

        page.Widgets.Clear();
        page.Widgets.AddRange(restored.Widgets);
        page.Name = restored.Name;

        PageViewModel rebuilt;
        try
        {
            rebuilt = _pageRebuilder is not null
                ? _pageRebuilder(_config) ?? throw new InvalidOperationException("The page rebuilder returned no page.")
                : new PageViewModel(page, _factory!);
        }
        catch (Exception ex)
        {
            // Keep the configuration and the rendered page in step (RF-012/RF-013): re-apply the
            // state the page was showing before the failed restore.
            _logger.Error(ex, "Edit mode: rebuilding the page for undo/redo failed; the layout was left unchanged");
            ReapplyCurrentPageConfig(page);
            return false;
        }

        var previous = _page;
        _page = rebuilt;

        foreach (var widget in rebuilt.Widgets)
        {
            widget.SetEditMode(true);
        }

        // The composition root's rebuilder may already have disposed the previous page; disposal is
        // idempotent, so this also covers the factory-only path used without a host.
        if (!ReferenceEquals(previous, rebuilt))
        {
            previous.Dispose();
        }

        OnPropertyChanged(nameof(Widgets));
        OnPropertyChanged(nameof(CanModifySelection));

        if (selectedId is not null)
        {
            var match = rebuilt.Widgets.FirstOrDefault(widget =>
                string.Equals(widget.WidgetId, selectedId, StringComparison.Ordinal));
            if (match is not null)
            {
                SelectWidget(match);
            }
        }

        // Undo/redo is a normal in-memory edit: the existing save-on-exit persists it (no schema
        // change), and leaving edit mode is the only path that writes the file.
        SetDirty(true);
        return true;
    }

    /// <summary>
    /// Best-effort rollback used when a page rebuild fails mid-restore: the page configuration is
    /// re-filled from the history's current snapshot so the configuration and the still-rendered
    /// page keep describing the same widgets.
    /// </summary>
    /// <param name="page">The live page configuration to re-fill.</param>
    private void ReapplyCurrentPageConfig(PageConfig page)
    {
        try
        {
            if (_history.Current is not { } current)
            {
                return;
            }

            var restored = JsonSerializer.Deserialize<PageConfig>(current, ConfigJson.Options);
            if (restored is null)
            {
                return;
            }

            page.Widgets.Clear();
            page.Widgets.AddRange(restored.Widgets);
            page.Name = restored.Name;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Edit mode: rolling back a failed undo/redo restore failed");
        }
    }

    private bool IsEditable(WidgetViewModelBase widget) =>
        _isEditMode && widget.CanEditGeometry && _page.Widgets.Contains(widget);

    private bool ApplyGeometry(WidgetViewModelBase widget, WidgetGeometry geometry)
    {
        if (widget.X.Equals(geometry.X)
            && widget.Y.Equals(geometry.Y)
            && widget.Width.Equals(geometry.Width)
            && widget.Height.Equals(geometry.Height))
        {
            return false;
        }

        widget.ApplyGeometry(geometry.X, geometry.Y, geometry.Width, geometry.Height);
        SetDirty(true);
        return true;
    }

    private bool TryApplyGeometryToConfig(out PageConfig page)
    {
        var found = _config.Pages.Find(candidate =>
            string.Equals(candidate.Id, _page.PageId, StringComparison.Ordinal));

        page = found!;
        if (found is null)
        {
            return false;
        }

        foreach (var widget in _page.Widgets)
        {
            var config = page.Widgets.Find(candidate =>
                string.Equals(candidate.Id, widget.WidgetId, StringComparison.Ordinal));

            if (config is null)
            {
                // The configuration changed underneath the page (for example a widget was removed
                // by another path); the missing entry is skipped defensively.
                continue;
            }

            config.X = widget.X;
            config.Y = widget.Y;
            config.Width = widget.Width;
            config.Height = widget.Height;
        }

        return true;
    }

    /// <summary>
    /// Finds the rendered page's configuration entry in the effective configuration. The page and
    /// the configuration are mutated in lockstep by add/remove/duplicate, so the lookup is by the
    /// rendered page id.
    /// </summary>
    /// <param name="page">The owning page configuration when found.</param>
    /// <returns><see langword="true"/> when the page is part of the configuration.</returns>
    private bool TryGetPageConfig(out PageConfig page)
    {
        page = _config.Pages.Find(candidate =>
            string.Equals(candidate.Id, _page.PageId, StringComparison.Ordinal))!;
        return page is not null;
    }

    /// <summary>
    /// Finds the page configuration and the config entry for a widget view model, so the
    /// remove/duplicate commands can keep the configuration and the rendered page in lockstep.
    /// </summary>
    /// <param name="widget">The widget view model to look up.</param>
    /// <param name="page">The owning page configuration when found.</param>
    /// <param name="config">The matching widget configuration when found.</param>
    /// <returns><see langword="true"/> when both were found.</returns>
    private bool TryFindConfig(WidgetViewModelBase widget, out PageConfig page, out WidgetConfig config)
    {
        page = null!;
        config = null!;

        if (!TryGetPageConfig(out var foundPage))
        {
            return false;
        }

        var foundConfig = foundPage.Widgets.Find(candidate =>
            string.Equals(candidate.Id, widget.WidgetId, StringComparison.Ordinal));
        if (foundConfig is null)
        {
            return false;
        }

        page = foundPage;
        config = foundConfig;
        return true;
    }

    /// <summary>
    /// Generates a unique widget id for a newly added widget within its page: the type name for the
    /// first widget of that type, then <c>&lt;type&gt;-2</c>, <c>&lt;type&gt;-3</c>, … while the
    /// candidate is already taken (M2-RF-431).
    /// </summary>
    /// <param name="page">The page the widget is added to.</param>
    /// <param name="baseId">The base id (the widget type discriminator).</param>
    /// <returns>The new, page-unique id.</returns>
    private static string GenerateUniqueWidgetId(PageConfig page, string baseId)
    {
        if (!page.Widgets.Exists(existing => string.Equals(existing.Id, baseId, StringComparison.Ordinal)))
        {
            return baseId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseId}-{suffix}";
            if (!page.Widgets.Exists(existing => string.Equals(existing.Id, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Generates a unique widget id for a duplicate within its page: <c>&lt;sourceId&gt;-copy</c>,
    /// with a numeric suffix while the candidate is already taken (M2-RF-432).
    /// </summary>
    /// <param name="page">The page the duplicate is added to.</param>
    /// <param name="sourceId">The id of the widget being duplicated.</param>
    /// <returns>The new, page-unique id.</returns>
    private static string GenerateUniqueId(PageConfig page, string sourceId)
    {
        var candidate = $"{sourceId}-copy";
        if (!page.Widgets.Exists(existing => string.Equals(existing.Id, candidate, StringComparison.Ordinal)))
        {
            return candidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var next = $"{sourceId}-copy-{suffix}";
            if (!page.Widgets.Exists(existing => string.Equals(existing.Id, next, StringComparison.Ordinal)))
            {
                return next;
            }
        }
    }

    /// <summary>
    /// Builds the rectangles the moving widget aligns against (RF-044/M2-RF-444): every other widget
    /// on the edited page, excluding the moving widget itself. Unknown/unusable rectangles are
    /// filtered by the alignment math.
    /// </summary>
    /// <param name="moving">The widget being dragged.</param>
    /// <returns>The other widgets' geometry snapshots.</returns>
    private IReadOnlyList<WidgetGeometry> BuildAlignmentTargets(WidgetViewModelBase moving)
    {
        var targets = new List<WidgetGeometry>(_page.Widgets.Count);
        foreach (var widget in _page.Widgets)
        {
            if (ReferenceEquals(widget, moving))
            {
                continue;
            }

            targets.Add(new WidgetGeometry(widget.X, widget.Y, widget.Width, widget.Height));
        }

        return targets;
    }

    private void SetGuides(IReadOnlyList<GuideLine> guides)
    {
        ArgumentNullException.ThrowIfNull(guides);

        if (GuidesEqual(_activeGuides, guides))
        {
            return;
        }

        _activeGuides = guides;
        OnPropertyChanged(nameof(ActiveGuides));
    }

    private static bool GuidesEqual(IReadOnlyList<GuideLine> left, IReadOnlyList<GuideLine> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private void SetDirty(bool dirty)
    {
        if (_isDirty == dirty)
        {
            return;
        }

        _isDirty = dirty;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void NotifySaveFailure(string message) =>
        _notifier?.Notify("TouchPanel", message, NotificationSeverity.Warning);

    private static double NormalizePageExtent(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private static double ClampPosition(double value, double max) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0, Math.Max(0, max));

    private static double ClampExtent(double value, double max)
    {
        // An unknown page bound degrades to the validator's absolute maximum so a hostile
        // infinity still produces a finite, bindable extent.
        var boundedMax = double.IsFinite(max) && max >= MinimumWidgetSize
            ? Math.Min(max, ConfigValidator.MaxWidgetExtent)
            : ConfigValidator.MaxWidgetExtent;

        if (double.IsNaN(value) || value < MinimumWidgetSize)
        {
            return MinimumWidgetSize;
        }

        return Math.Min(value, boundedMax);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
