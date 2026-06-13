using CodexBar.Models;
using CodexBar.ViewModels;
using System.Windows.Media;
using Xunit;

namespace CodexBar.Tests;

public sealed class ActivityStatusPaletteTests
{
    [Theory]
    [InlineData(CodexActivityStatus.Idle)]
    [InlineData(CodexActivityStatus.Completed)]
    public void IdleAndCompletedUseGreenLight(CodexActivityStatus status)
    {
        Assert.Same(Brushes.LimeGreen, ActivityStatusPalette.GreenLightBrush(status));
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.BlueLightBrush(status));
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.RedLightBrush(status));
    }

    [Theory]
    [InlineData(CodexActivityStatus.Working)]
    [InlineData(CodexActivityStatus.AutoReviewing)]
    [InlineData(CodexActivityStatus.WaitingForUser)]
    public void ActiveStatesUseBlueLight(CodexActivityStatus status)
    {
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.GreenLightBrush(status));
        Assert.Same(Brushes.DeepSkyBlue, ActivityStatusPalette.BlueLightBrush(status));
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.RedLightBrush(status));
    }

    [Fact]
    public void ErrorUsesRedLight()
    {
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.GreenLightBrush(CodexActivityStatus.Error));
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.BlueLightBrush(CodexActivityStatus.Error));
        Assert.Same(Brushes.OrangeRed, ActivityStatusPalette.RedLightBrush(CodexActivityStatus.Error));
    }

    [Fact]
    public void UnknownLeavesLightsOff()
    {
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.GreenLightBrush(CodexActivityStatus.Unknown));
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.BlueLightBrush(CodexActivityStatus.Unknown));
        Assert.Same(Brushes.DimGray, ActivityStatusPalette.RedLightBrush(CodexActivityStatus.Unknown));
    }
}
