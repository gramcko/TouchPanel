using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Views.Controls;

/// <summary>
/// Attached behavior that turns a <see cref="TextBox"/> into a numeric property field: typing and
/// paste are filtered to the field's numeric grammar, Up/Down step it through the editor's stepper
/// path, GotFocus/LostFocus open and commit the editor's single live-edit group, and Enter commits
/// and starts a fresh group without leaving the field.
/// </summary>
/// <remarks>
/// <para>The field key is the property-editor VM property name (for example <c>XText</c> or
/// <c>LevelText</c>). The behavior asks the bound <see cref="INumericFieldHost"/> for the current
/// <see cref="NumericFieldSpec"/> on every event, so a bound that depends on live state (geometry,
/// the opposite range bound) is always current.</para>
/// <para>Only the reusable behavior is attached here; the existing validation remains the safety
/// net, so an invalid value that slips through (for example an inherited theme default with more
/// decimals than the field formats) is still clamped on commit.</para>
/// </remarks>
public static class NumericInput
{
    /// <summary>Identifies the attached numeric-field key property.</summary>
    public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
        "Field",
        typeof(string),
        typeof(NumericInput),
        new PropertyMetadata(null, OnFieldChanged));

    private static readonly ConditionalWeakTable<TextBox, State> States = new();

    /// <summary>Gets the numeric-field key attached to a text box.</summary>
    /// <param name="element">The text box.</param>
    /// <returns>The field key, or <see langword="null"/>.</returns>
    public static string? GetField(DependencyObject element) =>
        (string?)element.GetValue(FieldProperty);

    /// <summary>Attaches a numeric-field key to a text box.</summary>
    /// <param name="element">The text box.</param>
    /// <param name="value">The field key.</param>
    public static void SetField(DependencyObject element, string? value) =>
        element.SetValue(FieldProperty, value);

    private static void OnFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        States.GetValue(textBox, static box =>
        {
            var state = new State(box);
            state.Attach();
            return state;
        });
    }

    private static (INumericFieldHost Host, NumericFieldSpec Spec)? Resolve(TextBox textBox)
    {
        var field = GetField(textBox);
        if (string.IsNullOrEmpty(field) || textBox.DataContext is not INumericFieldHost host)
        {
            return null;
        }

        var spec = host.GetNumericFieldSpec(field);
        return spec is null ? null : (host, spec);
    }

    private sealed class State
    {
        private readonly TextBox _textBox;

        public State(TextBox textBox) => _textBox = textBox;

        public void Attach()
        {
            _textBox.PreviewTextInput += OnPreviewTextInput;
            _textBox.PreviewKeyDown += OnPreviewKeyDown;
            _textBox.KeyDown += OnKeyDown;
            _textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            _textBox.LostKeyboardFocus += OnLostKeyboardFocus;
            DataObject.AddPastingHandler(_textBox, OnPasting);
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (Resolve(_textBox) is not { } target
                || NumericTextFilter.TryApplyEdit(
                    _textBox.Text,
                    _textBox.SelectionStart,
                    _textBox.SelectionLength,
                    e.Text,
                    target.Spec.Decimals,
                    target.Spec.AllowNegative,
                    out _))
            {
                return;
            }

            // Reject the keystroke: the bound value and the current text are untouched.
            e.Handled = true;
        }

        private void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (Resolve(_textBox) is not { } target)
            {
                return;
            }

            var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            if (NumericTextFilter.TryApplyEdit(
                _textBox.Text,
                _textBox.SelectionStart,
                _textBox.SelectionLength,
                pasted,
                target.Spec.Decimals,
                target.Spec.AllowNegative,
                out _))
            {
                return;
            }

            // Reject the whole paste rather than inserting a partially valid string.
            e.CancelCommand();
        }

        private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (Resolve(_textBox) is { } target && GetField(_textBox) is string { Length: > 0 } field)
            {
                target.Host.BeginNumericLiveEdit(field, fromFocus: true);
            }
        }

        private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (Resolve(_textBox) is { } target && GetField(_textBox) is string { Length: > 0 } field)
            {
                target.Host.CommitNumericLiveEdit(field);
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var field = GetField(_textBox);
            if (e.Key is not (Key.Up or Key.Down)
                || Resolve(_textBox) is not { } target
                || string.IsNullOrEmpty(field))
            {
                return;
            }

            e.Handled = target.Host.StepNumericField(field, e.Key == Key.Up ? 1 : -1);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var field = GetField(_textBox);
            if (e.Key != Key.Enter
                || Resolve(_textBox) is not { } target
                || string.IsNullOrEmpty(field))
            {
                return;
            }

            // Commit and immediately reopen the session so further typing stays a live preview and
            // forms a separate undo group, exactly like the original Font size field.
            target.Host.CommitNumericLiveEdit(field);
            target.Host.BeginNumericLiveEdit(field, fromFocus: true);
            e.Handled = true;
        }
    }
}
