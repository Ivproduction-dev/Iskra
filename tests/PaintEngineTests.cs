using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class PaintEngineTests
{
    private static int Red => PaintOperations.ToPixel(255, 255, 0, 0);
    private static int Green => PaintOperations.ToPixel(255, 0, 255, 0);
    private static int Blue => PaintOperations.ToPixel(255, 0, 0, 255);
    private static int Yellow => PaintOperations.ToPixel(255, 255, 255, 0);

    private static int Alpha(int pixel) => (pixel >> 24) & 0xFF;
    private static int RedOf(int pixel) => (pixel >> 16) & 0xFF;
    private static int GreenOf(int pixel) => (pixel >> 8) & 0xFF;
    private static int BlueOf(int pixel) => pixel & 0xFF;

    [Fact]
    public void NewDocumentStartsEmpty()
    {
        var document = new PaintDocument(7, 5);

        Assert.Equal(7, document.Width);
        Assert.Equal(5, document.Height);
        Assert.Equal(35, document.Pixels.Length);
        Assert.All(document.Pixels, pixel => Assert.Equal(0, pixel));
        Assert.False(document.CanUndo);
        Assert.False(document.CanRedo);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void DocumentRejectsEmptyCanvas(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaintDocument(width, height));
    }

    [Fact]
    public void UndoAndRedoRestorePixels()
    {
        var document = new PaintDocument(4, 4);
        document.PushUndo();
        PaintOperations.Stamp(document.Pixels, 4, 4, 2, 2, 1, Red, erase: false);

        Assert.True(document.CanUndo);
        Assert.True(document.Undo());
        Assert.Equal(0, document.Pixels[2 * 4 + 2]);
        Assert.True(document.CanRedo);

        Assert.True(document.Redo());
        Assert.Equal(Red, document.Pixels[2 * 4 + 2]);
    }

    [Fact]
    public void UndoHistoryKeepsOnlyTwentyFiveSnapshots()
    {
        var document = new PaintDocument(2, 2);
        for (var index = 0; index < 30; index++)
        {
            document.PushUndo();
            document.Pixels[0] = index + 1;
        }

        Assert.Equal(25, document.UndoCount);
        for (var index = 0; index < 25; index++)
        {
            Assert.True(document.Undo());
        }
        Assert.False(document.Undo());
        Assert.Equal(25, document.RedoCount);
    }

    [Fact]
    public void CancelUndoRestoresPixelsWithoutAddingRedo()
    {
        var document = new PaintDocument(2, 2);
        document.PushUndo();
        document.Pixels[0] = 1;
        document.Pixels[1] = 2;

        Assert.True(document.CancelUndo());
        Assert.Equal(0, document.Pixels[0]);
        Assert.Equal(0, document.Pixels[1]);
        Assert.False(document.CanUndo);
        Assert.False(document.CanRedo);

        Assert.False(document.CancelUndo());
    }

    [Fact]
    public void PushUndoClearsRedoHistory()
    {
        var document = new PaintDocument(2, 2);
        document.PushUndo();
        document.Pixels[0] = 1;
        document.Undo();
        Assert.True(document.CanRedo);

        document.PushUndo();

        Assert.False(document.CanRedo);
        Assert.Equal(0, document.RedoCount);
    }

    [Fact]
    public void OpaqueStampReplacesPixel()
    {
        var pixels = new int[5 * 5];
        PaintOperations.Stamp(pixels, 5, 5, 2, 2, 1, Red, erase: false);

        Assert.Equal(Red, pixels[2 * 5 + 2]);
    }

    [Fact]
    public void TransparentStampClearsPixelLikeWindowsPainting()
    {
        var pixels = new int[5 * 5];
        pixels[2 * 5 + 2] = Green;

        PaintOperations.Stamp(pixels, 5, 5, 2, 2, 1, PaintOperations.ToPixel(0, 0, 0, 0), erase: false);

        Assert.Equal(0, pixels[2 * 5 + 2]);
    }

    [Fact]
    public void SemiTransparentStampBlendsWithBackground()
    {
        var pixels = new int[5 * 5];
        pixels[2 * 5 + 2] = Green;

        PaintOperations.Stamp(pixels, 5, 5, 2, 2, 1, PaintOperations.ToPixel(128, 255, 0, 0), erase: false);

        var pixel = pixels[2 * 5 + 2];
        Assert.Equal(255, Alpha(pixel));
        Assert.InRange(RedOf(pixel), 127, 128);
        Assert.InRange(GreenOf(pixel), 127, 128);
        Assert.Equal(0, BlueOf(pixel));
    }

    [Fact]
    public void EraserStampMakesPixelTransparent()
    {
        var pixels = new int[5 * 5];
        pixels[2 * 5 + 2] = Red;

        PaintOperations.Stamp(pixels, 5, 5, 2, 2, 1, Blue, erase: true);

        Assert.Equal(0, pixels[2 * 5 + 2]);
    }

    [Fact]
    public void RoundStampCoversCircleAndStaysInsideCanvas()
    {
        var pixels = new int[11 * 11];

        PaintOperations.Stamp(pixels, 11, 11, 5, 5, 7, Red, erase: false);

        Assert.Equal(Red, pixels[5 * 11 + 5]);
        Assert.Equal(Red, pixels[5 * 11 + 8]);
        Assert.Equal(0, pixels[0]);
        Assert.Equal(0, pixels[10]);
    }

    [Fact]
    public void StrokeConnectsBothEndpoints()
    {
        var pixels = new int[40 * 20];
        var last = PaintOperations.StrokeTo(pixels, 40, 20, new PaintPoint(4, 10), 34, 10, 2, Red, erase: false);

        Assert.Equal(new PaintPoint(34, 10), last);
        for (var x = 4; x <= 34; x++)
        {
            Assert.NotEqual(0, pixels[10 * 40 + x]);
        }
    }

    [Fact]
    public void SprayPaintStaysWithinBrushRadius()
    {
        var pixels = new int[80 * 80];
        var random = new Random(12345);

        for (var index = 0; index < 20; index++)
        {
            PaintOperations.SprayAt(pixels, 80, 80, 40, 40, 20, 100, Red, erase: false, random);
        }

        var painted = 0;
        for (var y = 0; y < 80; y++)
        {
            for (var x = 0; x < 80; x++)
            {
                if (pixels[y * 80 + x] == 0)
                {
                    continue;
                }
                painted++;
                Assert.InRange(Math.Abs(x - 40), 0, 11);
                Assert.InRange(Math.Abs(y - 40), 0, 11);
            }
        }
        Assert.True(painted > 0);
    }

    [Fact]
    public void FloodFillFillsOnlyConnectedArea()
    {
        var pixels = new int[7 * 7];
        for (var y = 0; y < 7; y++)
        {
            pixels[y * 7 + 3] = Blue;
        }

        PaintOperations.FloodFill(pixels, 7, 7, 0, 0, Red, 0);

        Assert.Equal(Red, pixels[0]);
        Assert.Equal(Red, pixels[3 * 7 + 2]);
        Assert.Equal(Blue, pixels[3 * 7 + 3]);
        Assert.Equal(0, pixels[3 * 7 + 4]);
        Assert.Equal(0, pixels[6 * 7 + 6]);
    }

    [Fact]
    public void FloodFillHonorsTolerance()
    {
        var pixels = Enumerable.Repeat(PaintOperations.ToPixel(255, 128, 128, 128), 9).ToArray();
        pixels[4] = PaintOperations.ToPixel(255, 130, 128, 128);

        PaintOperations.FloodFill(pixels, 3, 3, 0, 0, Red, 1);

        Assert.Equal(Red, pixels[0]);
        Assert.Equal(PaintOperations.ToPixel(255, 130, 128, 128), pixels[4]);

        var wideTolerance = Enumerable.Repeat(PaintOperations.ToPixel(255, 128, 128, 128), 9).ToArray();
        wideTolerance[4] = PaintOperations.ToPixel(255, 130, 128, 128);
        PaintOperations.FloodFill(wideTolerance, 3, 3, 0, 0, Green, 2);

        Assert.Equal(Green, wideTolerance[4]);
    }

    [Fact]
    public void FloodFillIgnoresOutsidePoint()
    {
        var pixels = new int[9];

        PaintOperations.FloodFill(pixels, 3, 3, -1, 0, Red, 0);
        PaintOperations.FloodFill(pixels, 3, 3, 3, 0, Red, 0);

        Assert.All(pixels, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void DrawShapeLineAndRectangle()
    {
        var line = new int[24 * 24];
        PaintOperations.DrawShape(line, 24, 24, PaintShape.Line, 3, 3, 20, 20, 1, Red, filled: false);
        Assert.NotEqual(0, line[10 * 24 + 10]);
        Assert.Equal(0, line[0]);

        var rectangle = new int[24 * 24];
        PaintOperations.DrawShape(rectangle, 24, 24, PaintShape.Rectangle, 3, 3, 20, 20, 1, Red, filled: false);
        Assert.NotEqual(0, rectangle[3 * 24 + 10]);
        Assert.NotEqual(0, rectangle[10 * 24 + 3]);
        Assert.Equal(0, rectangle[10 * 24 + 10]);
    }

    [Fact]
    public void FilledEllipseAndRectangleCoverCenters()
    {
        var pixels = new int[41 * 41];

        PaintOperations.DrawShape(pixels, 41, 41, PaintShape.Ellipse, 5, 5, 35, 35, 1, Red, filled: true);
        Assert.Equal(Red, pixels[20 * 41 + 20]);

        PaintOperations.Clear(pixels);
        PaintOperations.DrawShape(pixels, 41, 41, PaintShape.Rectangle, 5, 5, 35, 35, 1, Blue, filled: true);
        Assert.Equal(Blue, pixels[20 * 41 + 20]);
    }

    [Fact]
    public void FrameCornersFollowCenterAndRotation()
    {
        var corners = PaintOperations.FrameCorners(10, 20, 100, 50, 0);
        Assert.Equal(new PaintPoint(-40, -5), corners[0]);
        Assert.Equal(new PaintPoint(60, -5), corners[1]);
        Assert.Equal(new PaintPoint(60, 45), corners[2]);
        Assert.Equal(new PaintPoint(-40, 45), corners[3]);

        var rotated = PaintOperations.FrameCorners(10, 20, 100, 50, 90);
        Assert.Equal(35, rotated[0].X, 6);
        Assert.Equal(-30, rotated[0].Y, 6);
        Assert.Equal(-15, rotated[2].X, 6);
        Assert.Equal(70, rotated[2].Y, 6);
    }

    [Fact]
    public void ShapeWorldPointsHaveExpectedPointCounts()
    {
        Assert.Equal(3, PaintOperations.ShapeWorldPoints(PaintShape.Triangle, 50, 50, 40, 40, 0).Length);
        Assert.Equal(10, PaintOperations.ShapeWorldPoints(PaintShape.Star, 50, 50, 40, 40, 0).Length);
        Assert.Equal(80, PaintOperations.ShapeWorldPoints(PaintShape.Heart, 50, 50, 40, 40, 0).Length);
        Assert.Equal(4, PaintOperations.ShapeWorldPoints(PaintShape.Rectangle, 50, 50, 40, 40, 0).Length);
    }

    [Theory]
    [InlineData(PaintShape.Triangle)]
    [InlineData(PaintShape.Heart)]
    [InlineData(PaintShape.Star)]
    public void FilledRotatedPolygonShapesPaintPixels(PaintShape shape)
    {
        var pixels = new int[80 * 80];

        PaintOperations.RasterizeRotatedShape(pixels, 80, 80, shape, 40, 40, 50, 50, 25, 2, Red, filled: true);

        Assert.Contains(pixels, pixel => pixel != 0);
        Assert.Equal(Red, pixels[40 * 80 + 40]);
    }

    [Fact]
    public void RotatedRectangleFilledCoversCenterButNotFarCorner()
    {
        var pixels = new int[80 * 80];

        PaintOperations.RasterizeRotatedShape(
            pixels, 80, 80, PaintShape.Rectangle, 40, 40, 50, 20, 45, 2, Red, filled: true);

        Assert.Equal(Red, pixels[40 * 80 + 40]);
        Assert.Equal(0, pixels[0]);
        Assert.InRange(pixels.Count(pixel => pixel != 0), 600, 1200);
    }

    [Fact]
    public void RotatedShapeOutlinePaintsWithoutFillingCenter()
    {
        var pixels = new int[80 * 80];

        PaintOperations.RasterizeRotatedShape(
            pixels, 80, 80, PaintShape.Ellipse, 40, 40, 50, 30, 0, 3, Blue, filled: false);

        Assert.True(pixels.Count(pixel => pixel != 0) > 60);
        Assert.Equal(0, pixels[40 * 80 + 40]);
    }

    [Fact]
    public void BlitTextScaledCopiesNearestPixels()
    {
        var source = new[] { Red, Green, Blue, Yellow };
        var destination = new int[20 * 20];

        PaintOperations.BlitText(destination, 20, 20, source, 2, 2, 5, 6, 3, 3, 0);

        Assert.Equal(Red, destination[6 * 20 + 5]);
        Assert.Equal(Green, destination[6 * 20 + 8]);
        Assert.Equal(Blue, destination[9 * 20 + 5]);
        Assert.Equal(Yellow, destination[9 * 20 + 8]);
        Assert.Equal(0, destination[0]);
    }

    [Fact]
    public void BlitTextClipsAtCanvasEdges()
    {
        var source = new[] { Red };
        var destination = new int[4 * 4];

        PaintOperations.BlitText(destination, 4, 4, source, 1, 1, -5, -5, 1, 1, 0);
        PaintOperations.BlitText(destination, 4, 4, source, 1, 1, 10, 10, 1, 1, 0);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void BlitTextRotatedPaintsAroundCenter()
    {
        var source = Enumerable.Repeat(Red, 4 * 2).ToArray();
        var destination = new int[20 * 20];

        PaintOperations.BlitText(destination, 20, 20, source, 4, 2, 8, 9, 1, 1, 90);

        Assert.NotEqual(0, destination[10 * 20 + 10]);
        Assert.NotEqual(0, destination[8 * 20 + 10]);
        Assert.NotEqual(0, destination[11 * 20 + 10]);
        Assert.Equal(0, destination[12 * 20 + 10]);
        Assert.Equal(0, destination[6 * 20 + 6]);
    }

    [Fact]
    public void BlitTextSemiTransparentBlends()
    {
        var source = new[] { PaintOperations.ToPixel(128, 255, 0, 0) };
        var destination = new int[3 * 3];
        destination[4] = Green;

        PaintOperations.BlitText(destination, 3, 3, source, 1, 1, 1, 1, 1, 1, 0);

        var pixel = destination[4];
        Assert.Equal(255, Alpha(pixel));
        Assert.InRange(RedOf(pixel), 127, 128);
        Assert.InRange(GreenOf(pixel), 127, 128);
    }
}
