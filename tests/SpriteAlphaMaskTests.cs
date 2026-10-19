using System.Windows;
using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class SpriteAlphaMaskTests
{
    [Fact]
    public void TransparentPixelDoesNotCountAsSpriteHit()
    {
        var mask = SpriteAlphaMask.FromBgra32(2, 1, [0, 0, 0, 0, 0, 0, 0, 255]);

        Assert.False(mask.ContainsOpaquePixel(new Point(0.5, 0.5), 2, 1));
        Assert.True(mask.ContainsOpaquePixel(new Point(1.5, 0.5), 2, 1));
        Assert.False(mask.ContainsOpaquePixel(new Point(2, 0.5), 2, 1));
    }

    [Fact]
    public void AlphaHitTestScalesWithRenderedSprite()
    {
        var mask = SpriteAlphaMask.FromBgra32(2, 1, [0, 0, 0, 0, 0, 0, 0, 128]);

        Assert.False(mask.ContainsOpaquePixel(new Point(1, 1), 4, 2));
        Assert.True(mask.ContainsOpaquePixel(new Point(3, 1), 4, 2));
    }

    [Fact]
    public void UniformToFillAccountsForCroppedTransparentArea()
    {
        var mask = SpriteAlphaMask.FromBgra32(2, 1, [0, 0, 0, 0, 0, 0, 0, 255]);

        Assert.False(mask.ContainsOpaquePixel(new Point(0.5, 2), 4, 4, uniformToFill: true));
        Assert.True(mask.ContainsOpaquePixel(new Point(2.5, 2), 4, 4, uniformToFill: true));
    }
}
