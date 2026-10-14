using TouchPanel.Config;

namespace TouchPanel.ViewModels.Edit;

/// <summary>
/// One selectable entry in the byte/data-rate display-unit picker (user-requested addition): the
/// <see cref="DisplayUnit"/> written into the widget configuration and a short, touch-friendly
/// label (<c>Auto</c> / <c>B</c> / <c>KB</c> / <c>MB</c> / <c>GB</c>). A plain data record so it
/// stays WPF-free and bindable through the property editor.
/// </summary>
/// <param name="Unit">The configured display unit.</param>
/// <param name="DisplayName">The label shown in the picker.</param>
public sealed record DisplayUnitOption(DisplayUnit Unit, string DisplayName);
