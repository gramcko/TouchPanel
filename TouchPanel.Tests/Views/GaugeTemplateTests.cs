using System.Windows;
using System.Windows.Controls;
using TouchPanel.Config;
using TouchPanel.Monitoring;
using TouchPanel.Tests.Monitoring;
using TouchPanel.Tests.TestSupport;
using TouchPanel.ViewModels;

namespace TouchPanel.Tests.Views;

/// <summary>
/// Gauge template smoke test (T-11, RF-033/RF-038): loads the real resource dictionary and applies
/// the <c>Widget.Gauge</c> <see cref="DataTemplate"/> to a live view model on an STA thread, forcing
/// template instantiation, binding attachment, measure, and arrange. This catches template/binding
/// defects that pure view-model tests cannot see (for example a TwoWay binding onto a read-only
/// view-model property, which only fails when WPF materializes the template).
/// </summary>
public sealed class GaugeTemplateTests
{
    [Fact]
    public async Task GaugeTemplate_LoadsAndBindsToTheReadOnlyViewModel()
    {
        using var sta = new StaThread();

        await sta.InvokeAsync(() =>
        {
            // Loads the real compiled BAML resource dictionary from the TouchPanel assembly.
            var dictionary = Assert.IsType<ResourceDictionary>(Application.LoadComponent(
                new Uri(
                    "/TouchPanel;component/Widgets/GaugeWidget/GaugeWidgetTemplates.xaml",
                    UriKind.Relative)));
            var template = Assert.IsType<DataTemplate>(dictionary["Widget.Gauge"]);

            var source = new FakeSnapshotSource();
            source.Publish(new SensorSnapshot
            {
                CapturedAt = DateTimeOffset.UnixEpoch,
                CpuTotalPercent = 42,
            });

            using var gauge = new GaugeWidgetViewModel(
                new GaugeWidgetConfig
                {
                    Id = "gauge-1",
                    X = 0,
                    Y = 0,
                    Width = 200,
                    Height = 100,
                    Label = "CPU",
                    Sensor = SensorKeys.CpuTotal,
                    Minimum = 0,
                    Maximum = 100,
                },
                source);

            var presenter = new ContentPresenter
            {
                Content = gauge,
                ContentTemplate = template,
            };

            // Materializing the template attaches the bindings; the read-only Progress property is
            // bound OneWay, so this must complete without a XamlParseException.
            presenter.Measure(new Size(200, 100));
            presenter.Arrange(new Rect(0, 0, 200, 100));

            Assert.Equal("42 %", gauge.ValueText);
        });
    }
}
