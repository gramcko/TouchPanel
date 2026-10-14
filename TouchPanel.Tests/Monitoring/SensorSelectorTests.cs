using TouchPanel.Monitoring;

namespace TouchPanel.Tests.Monitoring;

/// <summary>
/// Pure sensor selection coverage for the LibreHardwareMonitor adapter mapping (RF-062): exact
/// case-insensitive name matching in priority order, first-finite fallback, prefix collection for
/// per-core loads, fan collection, GPU-memory convention fallback, and unavailable-value handling.
/// No hardware is involved.
/// </summary>
public sealed class SensorSelectorTests
{
    [Fact]
    public void FindValue_MatchesTheNameCaseInsensitivelyWithinTheRequestedKind()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("cpu total", SensorKind.Load, 42),
        };

        Assert.Equal(42, SensorSelector.FindValue(sensors, SensorKind.Load, "CPU Total"));
    }

    [Fact]
    public void FindValue_UsesNamePriorityInsteadOfSensorOrder()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("Core Average", SensorKind.Temperature, 50),
            Candidate("CPU Package", SensorKind.Temperature, 65),
        };

        // The first name in the list wins even though its sensor appears later in hardware order.
        Assert.Equal(65, SensorSelector.FindValue(sensors, SensorKind.Temperature, "CPU Package", "Core Average"));
    }

    [Fact]
    public void FindValue_SkipsUnavailableAndNonFiniteValues()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("CPU Total", SensorKind.Load, null),
            Candidate("CPU Total", SensorKind.Load, double.NaN),
            Candidate("CPU Total", SensorKind.Load, 37),
        };

        Assert.Equal(37, SensorSelector.FindValue(sensors, SensorKind.Load, "CPU Total"));
    }

    [Fact]
    public void FindValue_DoesNotCrossSensorKinds()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("CPU Package", SensorKind.Load, 10),
        };

        Assert.Null(SensorSelector.FindValue(sensors, SensorKind.Temperature, "CPU Package"));
    }

    [Fact]
    public void FindValue_WhenNoNameMatches_ReturnsUnavailable()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("Something Else", SensorKind.Load, 10),
        };

        Assert.Null(SensorSelector.FindValue(sensors, SensorKind.Load, "CPU Total"));
    }

    [Fact]
    public void FindValueContaining_MatchesASubstringCaseInsensitivelyWithinTheRequestedKind()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("Network Utilization", SensorKind.Load, 4),
            Candidate("Ethernet Download Speed", SensorKind.Throughput, 125000),
        };

        Assert.Equal(125000, SensorSelector.FindValueContaining(sensors, SensorKind.Throughput, "download"));
    }

    [Fact]
    public void FindValueContaining_UsesFragmentPriorityAndSkipsUnavailableValues()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("Download Speed", SensorKind.Throughput, null),
            Candidate("Adapter Upload Speed", SensorKind.Throughput, 8000),
            Candidate("Download Speed", SensorKind.Throughput, 120000),
        };

        Assert.Equal(120000, SensorSelector.FindValueContaining(sensors, SensorKind.Throughput, "download", "upload"));
        Assert.Equal(8000, SensorSelector.FindValueContaining(sensors, SensorKind.Throughput, "upload", "download"));
    }

    [Fact]
    public void FindValueContaining_DoesNotMatchADifferentKind()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("Downloaded Data", SensorKind.Data, 3),
        };

        Assert.Null(SensorSelector.FindValueContaining(sensors, SensorKind.Throughput, "download"));
    }

    [Theory]
    [InlineData(null, 42d, 42d)]     // First node supplies the value.
    [InlineData(null, null, null)]   // No node supplies a value.
    [InlineData(0d, 0d, 0d)]         // Idle readings are kept when nothing better arrives.
    [InlineData(0d, 42d, 42d)]       // A later busy adapter replaces an earlier idle zero.
    [InlineData(42d, 0d, 42d)]       // An idle later adapter never masks an earlier busy one.
    [InlineData(42d, 7d, 42d)]       // The first positive reading wins.
    public void PreferActiveValue_KeepsTheMostMeaningfulReadingAcrossNodes(
        double? current,
        double? candidate,
        double? expected)
    {
        Assert.Equal(expected, SensorSelector.PreferActiveValue(current, candidate));
    }

    [Fact]
    public void FindFirstValue_ReturnsTheFirstFiniteValueOfTheKindInHardwareOrder()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("First", SensorKind.Temperature, null),
            Candidate("Second", SensorKind.Temperature, double.PositiveInfinity),
            Candidate("Third", SensorKind.Temperature, 48),
            Candidate("Fourth", SensorKind.Temperature, 60),
        };

        Assert.Equal(48, SensorSelector.FindFirstValue(sensors, SensorKind.Temperature));
    }

    [Fact]
    public void FindAllValues_CollectsEveryFiniteValueOfTheKindInOrder()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("Fan #1", SensorKind.Fan, 1200),
            Candidate("Load", SensorKind.Load, 20),
            Candidate("Fan #2", SensorKind.Fan, null),
            Candidate("Fan #3", SensorKind.Fan, 900.5),
        };

        Assert.Equal(new[] { 1200d, 900.5 }, SensorSelector.FindAllValues(sensors, SensorKind.Fan));
    }

    [Fact]
    public void FindValuesByNamePrefix_MatchesThePrefixAndSkipsUnavailableValues()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("CPU Core #1", SensorKind.Load, 10),
            Candidate("cpu core #2", SensorKind.Load, double.NaN),
            Candidate("CPU Core #3", SensorKind.Load, 30),
            Candidate("CPU Core Max", SensorKind.Load, 99),
            Candidate("CPU Total", SensorKind.Load, 25),
        };

        Assert.Equal(new[] { 10d, 30d }, SensorSelector.FindValuesByNamePrefix(sensors, SensorKind.Load, "CPU Core #"));
    }

    [Fact]
    public void FindGpuMemoryBytes_PrefersTheSmallDataConventionWithinAName()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("GPU Memory Used", SensorKind.Data, 4),
            Candidate("GPU Memory Used", SensorKind.SmallData, 1536),
        };

        Assert.Equal(1536d * 1024 * 1024, SensorSelector.FindGpuMemoryBytes(sensors, "GPU Memory Used"));
    }

    [Fact]
    public void FindGpuMemoryBytes_UsesNamePriorityAcrossConventions()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("D3D Dedicated Memory Used", SensorKind.SmallData, 512),
            Candidate("GPU Memory Used", SensorKind.Data, 4),
        };

        // The first name wins even when its sensor uses a different convention than a later name.
        Assert.Equal(4d * 1024 * 1024 * 1024, SensorSelector.FindGpuMemoryBytes(
            sensors,
            "GPU Memory Used",
            "D3D Dedicated Memory Used"));
    }

    [Fact]
    public void FindGpuMemoryBytes_FallsBackToTheNextName()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("D3D Dedicated Memory Total", SensorKind.SmallData, 1024),
        };

        Assert.Equal(1024d * 1024 * 1024, SensorSelector.FindGpuMemoryBytes(
            sensors,
            "GPU Memory Total",
            "D3D Dedicated Memory Total"));
    }

    [Fact]
    public void FindGpuMemoryBytes_WhenNothingMatches_ReturnsUnavailable()
    {
        var sensors = new List<SensorCandidate>
        {
            Candidate("Unrelated", SensorKind.SmallData, 100),
        };

        Assert.Null(SensorSelector.FindGpuMemoryBytes(sensors, "GPU Memory Used"));
    }

    [Fact]
    public void PreferredNamesThenFirstSensor_ResolvesTemperatureInFallbackOrder()
    {
        var withPreferred = new List<SensorCandidate>
        {
            Candidate("Random Temp", SensorKind.Temperature, 55),
            Candidate("CPU Package", SensorKind.Temperature, 65),
        };

        var preferred = SensorSelector.FindValue(withPreferred, SensorKind.Temperature, "CPU Package", "Core Average")
            ?? SensorSelector.FindFirstValue(withPreferred, SensorKind.Temperature);

        Assert.Equal(65, preferred);

        var withoutPreferred = new List<SensorCandidate>
        {
            Candidate("Random Temp", SensorKind.Temperature, null),
            Candidate("Other Temp", SensorKind.Temperature, 42),
        };

        var fallback = SensorSelector.FindValue(withoutPreferred, SensorKind.Temperature, "CPU Package", "Core Average")
            ?? SensorSelector.FindFirstValue(withoutPreferred, SensorKind.Temperature);

        Assert.Equal(42, fallback);
    }

    private static SensorCandidate Candidate(string name, SensorKind kind, double? value) => new(name, kind, value);
}
