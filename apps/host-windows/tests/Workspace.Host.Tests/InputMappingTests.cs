using Workspace.Host.Windows;

namespace Workspace.Host.Tests;

public sealed class InputMappingTests
{
    [Theory]
    [InlineData(0.0, 0.0, 100, 200)]
    [InlineData(1.0, 1.0, 900, 800)]
    [InlineData(0.5, 0.5, 500, 500)]
    public void MapsNormalizedCoordinates(
        double x,
        double y,
        int expectedX,
        int expectedY)
    {
        var result = InputCoordinateMapper.Map(
            x,
            y,
            new WindowBounds(100, 200, 800, 600));

        Assert.Equal((expectedX, expectedY), (result.X, result.Y));
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(0.5, -0.01)]
    [InlineData(0.5, 1.01)]
    public void RejectsCoordinatesOutsideTheNormalizedSurface(double x, double y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InputCoordinateMapper.Map(x, y, new WindowBounds(0, 0, 800, 600)));
    }
}
