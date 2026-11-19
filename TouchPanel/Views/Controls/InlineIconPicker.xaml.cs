using System.Collections;
using System.Windows;
using System.Windows.Controls;
using TouchPanel.Config;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Inline, expandable picker for the long built-in icon catalogs (user-requested replacement for
/// the icon ComboBoxes). The collapsed header shows the current entry; tapping it expands a
/// two-column tile list inside the property panel's scroll surface, and tapping a tile applies that
/// entry and collapses again. Because the picker is part of the panel flow (no <c>Popup</c>), the
/// panel's <see cref="TouchPan"/> behavior is the single scroll mechanism and a drag that starts on
/// a tile scrolls the panel instead of selecting.
/// </summary>
public partial class InlineIconPicker : UserControl
{
    /// <summary>Identifies the <see cref="ItemsSource"/> dependency property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(InlineIconPicker),
        new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="SelectedItem"/> dependency property (two-way by default).</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem),
        typeof(IconOption),
        typeof(InlineIconPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Identifies the <see cref="IsExpanded"/> dependency property.</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(InlineIconPicker),
        new FrameworkPropertyMetadata(false));

    /// <summary>Initializes a new instance of the <see cref="InlineIconPicker"/> class.</summary>
    public InlineIconPicker()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the entries shown when expanded. The property editor supplies its existing
    /// "none + catalog" option list, so the picker never owns catalog semantics.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the current entry. Two-way bound to the property editor's selection, so a tile
    /// tap runs the same apply/live-projection path the previous ComboBox used.
    /// </summary>
    public IconOption? SelectedItem
    {
        get => (IconOption?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Gets or sets whether the inline tile list is expanded. Starts collapsed.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Toggles the inline list; tapping the header again collapses it.</summary>
    /// <param name="sender">The header button.</param>
    /// <param name="e">The click event data.</param>
    private void OnHeaderClick(object sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    /// <summary>
    /// Applies the tapped tile through the two-way selection binding and collapses the list. A drag
    /// never reaches this handler: the panel's touch pan consumes the gesture before promotion.
    /// </summary>
    /// <param name="sender">The tile button whose data context is an icon option.</param>
    /// <param name="e">The click event data.</param>
    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: IconOption option })
        {
            SelectedItem = option;
            IsExpanded = false;
        }

        e.Handled = true;
    }
}
