using TouchPanel.Interop.Audio;

namespace TouchPanel.Tests.Interop;

/// <summary>
/// Pure level-to-scalar mapping of the Core Audio volume controller. The COM call itself is never
/// exercised by tests (RNF-006: no hardware); this covers the one piece of mapping arithmetic the
/// T-09 volume decision adds outside the seams.
/// </summary>
public class CoreAudioControllerTests
{
    [Theory]
    [InlineData(0, 0f)]
    [InlineData(25, 0.25f)]
    [InlineData(50, 0.5f)]
    [InlineData(100, 1f)]
    [InlineData(-10, 0f)]
    [InlineData(150, 1f)]
    public void LevelToScalar_ClampsAndMapsToUnitRange(int level, float expected)
    {
        Assert.Equal(expected, CoreAudioController.LevelToScalar(level));
    }
}
