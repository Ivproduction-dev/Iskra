namespace IskraStudio;

public enum PaintTool
{
    Brush,
    Spray,
    Eraser,
    Fill,
    Pipette,
    Shape,
    Line,
    Text,
    Hand
}

public enum PaintShape
{
    Rectangle,
    Ellipse,
    Triangle,
    Heart,
    Star,
    Line
}

public readonly record struct PaintPoint(double X, double Y);

public sealed class PaintDocument
{
    public const int MaxUndo = 25;

    private readonly List<int[]> undoStack = [];
    private readonly List<int[]> redoStack = [];

    public PaintDocument(int width, int height)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Размер холста должен быть положительным.");
        }

        Width = width;
        Height = height;
        Pixels = new int[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public int[] Pixels { get; }

    public bool CanUndo => undoStack.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    public int UndoCount => undoStack.Count;

    public int RedoCount => redoStack.Count;

    public void ResetHistory()
    {
        undoStack.Clear();
        redoStack.Clear();
    }

    public void PushUndo()
    {
        undoStack.Add((int[])Pixels.Clone());
        if (undoStack.Count > MaxUndo)
        {
            undoStack.RemoveAt(0);
        }
        redoStack.Clear();
    }

    public void PushUndo(int[] snapshot)
    {
        undoStack.Add((int[])snapshot.Clone());
        if (undoStack.Count > MaxUndo)
        {
            undoStack.RemoveAt(0);
        }
        redoStack.Clear();
    }

    public bool CancelUndo()
    {
        if (undoStack.Count == 0)
        {
            return false;
        }
        Array.Copy(undoStack[^1], Pixels, Pixels.Length);
        undoStack.RemoveAt(undoStack.Count - 1);
        redoStack.Clear();
        return true;
    }

    public bool Undo()
    {
        if (undoStack.Count == 0)
        {
            return false;
        }
        redoStack.Add((int[])Pixels.Clone());
        Array.Copy(undoStack[^1], Pixels, Pixels.Length);
        undoStack.RemoveAt(undoStack.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (redoStack.Count == 0)
        {
            return false;
        }
        undoStack.Add((int[])Pixels.Clone());
        if (undoStack.Count > MaxUndo)
        {
            undoStack.RemoveAt(0);
        }
        Array.Copy(redoStack[^1], Pixels, Pixels.Length);
        redoStack.RemoveAt(redoStack.Count - 1);
        return true;
    }
}

public static class PaintOperations
{
    public static int ToPixel(byte alpha, byte red, byte green, byte blue) =>
        (alpha << 24) | (red << 16) | (green << 8) | blue;

    public static int FromPixel(int pixel) => pixel;

    public static int BlendPixel(int destination, int source)
    {
        var sourceAlpha = (source >> 24) & 0xFF;
        if (sourceAlpha == 0)
        {
            return 0;
        }
        if (sourceAlpha == 255)
        {
            return source;
        }
        var destinationAlpha = (destination >> 24) & 0xFF;
        var alpha = sourceAlpha / 255.0;
        var backAlpha = destinationAlpha / 255.0 * (1 - alpha);
        var outAlpha = alpha + backAlpha;
        if (outAlpha <= 0)
        {
            return 0;
        }
        int Blend(int front, int back) =>
            (int)((front * alpha + back * backAlpha) / outAlpha);
        return ((int)(outAlpha * 255) << 24)
            | (Blend((source >> 16) & 0xFF, (destination >> 16) & 0xFF) << 16)
            | (Blend((source >> 8) & 0xFF, (destination >> 8) & 0xFF) << 8)
            | Blend(source & 0xFF, destination & 0xFF);
    }

    public static void Clear(int[] pixels) => Array.Clear(pixels, 0, pixels.Length);

    public static void Stamp(int[] pixels, int width, int height, int x, int y, double size, int color, bool erase)
    {
        if (size <= 1)
        {
            if (x >= 0 && y >= 0 && x < width && y < height)
            {
                var index = y * width + x;
                pixels[index] = erase ? 0 : BlendPixel(pixels[index], color);
            }
            return;
        }

        var radius = Math.Max(1, (int)Math.Round(size / 2.0));
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || py < 0 || px >= width || py >= height)
                {
                    continue;
                }
                var index = py * width + px;
                pixels[index] = erase ? 0 : BlendPixel(pixels[index], color);
            }
        }
    }

    public static PaintPoint StrokeTo(
        int[] pixels, int width, int height, PaintPoint last, int x, int y, double size, int color, bool erase)
    {
        var steps = Math.Max(Math.Abs(x - (int)last.X), Math.Abs(y - (int)last.Y));
        steps = Math.Max(steps, 1);
        for (var step = 1; step <= steps; step++)
        {
            var t = (double)step / steps;
            Stamp(pixels, width, height,
                (int)Math.Round(last.X + (x - last.X) * t),
                (int)Math.Round(last.Y + (y - last.Y) * t),
                size, color, erase);
        }
        return new PaintPoint(x, y);
    }

    public static void SprayAt(
        int[] pixels, int width, int height, int x, int y, double size, double density, int color, bool erase, Random random)
    {
        var radius = Math.Max(1.0, size / 2.0);
        var count = Math.Max(1, (int)density);
        for (var i = 0; i < count; i++)
        {
            var angle = random.NextDouble() * 2 * Math.PI;
            var u1 = random.NextDouble();
            var u2 = random.NextDouble();
            var distFactor = Math.Min(u1, u2) + Math.Abs(u1 - u2) * 0.5;
            var distance = distFactor * radius;

            var px = x + (int)Math.Round(Math.Cos(angle) * distance);
            var py = y + (int)Math.Round(Math.Sin(angle) * distance);
            if (px < 0 || py < 0 || px >= width || py >= height)
            {
                continue;
            }
            var index = py * width + px;
            pixels[index] = erase ? 0 : BlendPixel(pixels[index], color);
        }
    }

    public static void FloodFill(int[] pixels, int width, int height, int x, int y, int color, double tolerance)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }
        var target = pixels[y * width + x];
        if (target == color)
        {
            return;
        }

        var toleranceSquared = tolerance * tolerance * 3;
        var targetAlpha = (target >> 24) & 0xFF;
        var targetRed = (target >> 16) & 0xFF;
        var targetGreen = (target >> 8) & 0xFF;
        var targetBlue = target & 0xFF;
        bool Matches(int pixel)
        {
            var alpha = (pixel >> 24) & 0xFF;
            var red = (pixel >> 16) & 0xFF;
            var green = (pixel >> 8) & 0xFF;
            var blue = pixel & 0xFF;
            var dr = red - targetRed;
            var dg = green - targetGreen;
            var db = blue - targetBlue;
            var da = (alpha - targetAlpha) * 2;
            return dr * dr + dg * dg + db * db + da * da <= toleranceSquared;
        }

        var total = width * height;
        var visited = new bool[total];
        var queue = new Queue<int>();
        var startIndex = y * width + x;
        if (!Matches(pixels[startIndex]))
        {
            return;
        }
        visited[startIndex] = true;
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            pixels[index] = color;
            var cx = index % width;
            var cy = index / width;

            if (cx > 0)
            {
                var next = index - 1;
                if (!visited[next] && Matches(pixels[next]))
                {
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (cx < width - 1)
            {
                var next = index + 1;
                if (!visited[next] && Matches(pixels[next]))
                {
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (cy > 0)
            {
                var next = index - width;
                if (!visited[next] && Matches(pixels[next]))
                {
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (cy < height - 1)
            {
                var next = index + width;
                if (!visited[next] && Matches(pixels[next]))
                {
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }
        }
    }

    public static void DrawShape(
        int[] pixels, int width, int height, PaintShape shape,
        int x0, int y0, int x1, int y1, double size, int color, bool filled)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);
        var thickness = Math.Max(1, (int)Math.Round(size));

        void Line(int ax, int ay, int bx, int by)
        {
            var steps = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
            steps = Math.Max(steps, 1);
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                StampOnto(pixels, width, height,
                    (int)Math.Round(ax + (bx - ax) * t),
                    (int)Math.Round(ay + (by - ay) * t),
                    size, color);
            }
        }

        if (shape == PaintShape.Line)
        {
            Line(x0, y0, x1, y1);
            return;
        }

        switch (shape)
        {
            case PaintShape.Rectangle:
                if (filled)
                {
                    for (var py = top; py <= bottom; py++)
                    {
                        for (var px = left; px <= right; px++)
                        {
                            StampOnto(pixels, width, height, px, py, size, color);
                        }
                    }
                }
                else
                {
                    for (var offset = 0; offset < thickness; offset++)
                    {
                        Line(left + offset, top + offset, right - offset, top + offset);
                        Line(left + offset, bottom - offset, right - offset, bottom - offset);
                        Line(left + offset, top + offset, left + offset, bottom - offset);
                        Line(right - offset, top + offset, right - offset, bottom - offset);
                    }
                }
                break;
            case PaintShape.Ellipse:
                {
                    var cx = (left + right) / 2.0;
                    var cy = (top + bottom) / 2.0;
                    var rx = Math.Max((right - left) / 2.0, 0.5);
                    var ry = Math.Max((bottom - top) / 2.0, 0.5);
                    if (filled)
                    {
                        for (var py = top; py <= bottom; py++)
                        {
                            for (var px = left; px <= right; px++)
                            {
                                var nx = (px - cx) / rx;
                                var ny = (py - cy) / ry;
                                if (nx * nx + ny * ny <= 1)
                                {
                                    StampOnto(pixels, width, height, px, py, size, color);
                                }
                            }
                        }
                    }
                    else
                    {
                        var perimeter = (int)(Math.PI * (rx + ry) * 2);
                        for (var step = 0; step <= perimeter; step++)
                        {
                            var angle = step * 2 * Math.PI / Math.Max(perimeter, 1);
                            StampOnto(pixels, width, height,
                                (int)Math.Round(cx + rx * Math.Cos(angle)),
                                (int)Math.Round(cy + ry * Math.Sin(angle)),
                                size, color);
                        }
                    }
                }
                break;
            default:
                Line(x0, y0, x1, y1);
                break;
        }
    }

    private static void StampOnto(int[] pixels, int width, int height, int x, int y, double size, int color)
    {
        if (size <= 1)
        {
            if (x >= 0 && y >= 0 && x < width && y < height)
            {
                var index = y * width + x;
                pixels[index] = BlendPixel(pixels[index], color);
            }
            return;
        }
        var radius = Math.Max(1, (int)Math.Round(size / 2.0));
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || py < 0 || px >= width || py >= height)
                {
                    continue;
                }
                var index = py * width + px;
                pixels[index] = BlendPixel(pixels[index], color);
            }
        }
    }

    public static PaintPoint[] FrameCorners(double x, double y, double width, double height, double rotation)
    {
        var radians = rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var halfW = width / 2;
        var halfH = height / 2;
        return
        [
            RotatePoint(-halfW, -halfH),
            RotatePoint(halfW, -halfH),
            RotatePoint(halfW, halfH),
            RotatePoint(-halfW, halfH)
        ];

        PaintPoint RotatePoint(double lx, double ly) =>
            new(x + lx * cos - ly * sin, y + lx * sin + ly * cos);
    }

    public static PaintPoint[] ShapeWorldPoints(
        PaintShape shape, double x, double y, double width, double height, double rotation)
    {
        var halfW = width / 2.0;
        var halfH = height / 2.0;
        var localPoints = shape switch
        {
            PaintShape.Triangle => new[] { new PaintPoint(0, -halfH), new PaintPoint(halfW, halfH), new PaintPoint(-halfW, halfH) },
            PaintShape.Heart => Enumerable.Range(0, 80).Select(index =>
            {
                var angle = index * Math.PI * 2 / 80;
                var sin = Math.Sin(angle);
                var px = 16 * sin * sin * sin;
                var py = 13 * Math.Cos(angle) - 5 * Math.Cos(2 * angle) - 2 * Math.Cos(3 * angle) - Math.Cos(4 * angle);
                return new PaintPoint(px / 16 * halfW, -py / 17 * halfH);
            }).ToArray(),
            PaintShape.Star => Enumerable.Range(0, 10).Select(index =>
            {
                var angle = -Math.PI / 2 + index * Math.PI / 5;
                var radius = index % 2 == 0 ? 1.0 : 0.43;
                return new PaintPoint(Math.Cos(angle) * halfW * radius, Math.Sin(angle) * halfH * radius);
            }).ToArray(),
            _ => new[]
            {
                new PaintPoint(-halfW, -halfH), new PaintPoint(halfW, -halfH),
                new PaintPoint(halfW, halfH), new PaintPoint(-halfW, halfH)
            }
        };

        var radians = rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return localPoints.Select(point => new PaintPoint(
            x + point.X * cos - point.Y * sin,
            y + point.X * sin + point.Y * cos)).ToArray();
    }

    public static void RasterizeRotatedShape(
        int[] buffer, int width, int height, PaintShape shape,
        double centerX, double centerY, double frameWidth, double frameHeight,
        double rotation, double size, int color, bool filled)
    {
        var radians = rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var halfW = frameWidth / 2;
        var halfH = frameHeight / 2;

        PaintPoint Rotate(double lx, double ly) =>
            new(centerX + lx * cos - ly * sin, centerY + lx * sin + ly * cos);

        if (shape is PaintShape.Triangle or PaintShape.Heart or PaintShape.Star)
        {
            var points = ShapeWorldPoints(shape, centerX, centerY, frameWidth, frameHeight, rotation);
            if (filled)
            {
                FillPolygon(buffer, width, height, points, color);
            }
            else
            {
                DrawPolygonOutline(buffer, width, height, points, size, color);
            }
            return;
        }

        if (shape == PaintShape.Ellipse && filled)
        {
            var left = (int)Math.Floor(centerX - halfW - halfH);
            var right = (int)Math.Ceiling(centerX + halfW + halfH);
            var top = (int)Math.Floor(centerY - halfW - halfH);
            var bottom = (int)Math.Ceiling(centerY + halfW + halfH);
            for (var py = Math.Max(top, 0); py <= Math.Min(bottom, height - 1); py++)
            {
                for (var px = Math.Max(left, 0); px <= Math.Min(right, width - 1); px++)
                {
                    var lx = px - centerX;
                    var ly = py - centerY;
                    var nx = (lx * cos + ly * sin) / Math.Max(halfW, 0.5);
                    var ny = (-lx * sin + ly * cos) / Math.Max(halfH, 0.5);
                    if (nx * nx + ny * ny <= 1)
                    {
                        var index = py * width + px;
                        buffer[index] = BlendPixel(buffer[index], color);
                    }
                }
            }
            return;
        }

        if (shape == PaintShape.Rectangle && filled)
        {
            var corners = FrameCorners(centerX, centerY, frameWidth, frameHeight, rotation);
            var minX = (int)Math.Floor(corners.Min(point => point.X));
            var maxX = (int)Math.Ceiling(corners.Max(point => point.X));
            var minY = (int)Math.Floor(corners.Min(point => point.Y));
            var maxY = (int)Math.Ceiling(corners.Max(point => point.Y));
            for (var py = Math.Max(minY, 0); py <= Math.Min(maxY, height - 1); py++)
            {
                for (var px = Math.Max(minX, 0); px <= Math.Min(maxX, width - 1); px++)
                {
                    var lx = px - centerX;
                    var ly = py - centerY;
                    var rx = lx * cos + ly * sin;
                    var ry = -lx * sin + ly * cos;
                    if (Math.Abs(rx) <= halfW && Math.Abs(ry) <= halfH)
                    {
                        var index = py * width + px;
                        buffer[index] = BlendPixel(buffer[index], color);
                    }
                }
            }
            return;
        }

        if (shape == PaintShape.Ellipse)
        {
            var perimeter = (int)(Math.PI * (halfW + halfH) * 2);
            for (var step = 0; step <= perimeter; step++)
            {
                var angle = step * 2 * Math.PI / Math.Max(perimeter, 1);
                var local = new PaintPoint(halfW * Math.Cos(angle), halfH * Math.Sin(angle));
                var rotated = Rotate(local.X, local.Y);
                StampOnto(buffer, width, height, (int)Math.Round(rotated.X), (int)Math.Round(rotated.Y), size, color);
            }
            return;
        }

        var rectCorners = FrameCorners(centerX, centerY, frameWidth, frameHeight, rotation);
        for (var edge = 0; edge < 4; edge++)
        {
            var a = rectCorners[edge];
            var b = rectCorners[(edge + 1) % 4];
            var steps = (int)Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            steps = Math.Max(steps, 1);
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                StampOnto(buffer, width, height,
                    (int)Math.Round(a.X + (b.X - a.X) * t),
                    (int)Math.Round(a.Y + (b.Y - a.Y) * t),
                    size, color);
            }
        }
    }

    private static void FillPolygon(int[] buffer, int width, int height, PaintPoint[] points, int color)
    {
        var minY = Math.Max(0, (int)Math.Floor(points.Min(point => point.Y)));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(points.Max(point => point.Y)));

        for (var y = minY; y <= maxY; y++)
        {
            var scanY = y + 0.5;
            var intersections = new List<double>(points.Length);
            for (var edge = 0; edge < points.Length; edge++)
            {
                var start = points[edge];
                var end = points[(edge + 1) % points.Length];
                if ((start.Y > scanY) != (end.Y > scanY))
                {
                    intersections.Add(start.X + (scanY - start.Y) * (end.X - start.X) / (end.Y - start.Y));
                }
            }

            intersections.Sort();
            for (var pair = 0; pair + 1 < intersections.Count; pair += 2)
            {
                var firstX = Math.Max(0, (int)Math.Ceiling(intersections[pair] - 0.5));
                var lastX = Math.Min(width - 1, (int)Math.Floor(intersections[pair + 1] - 0.5));
                for (var x = firstX; x <= lastX; x++)
                {
                    var index = y * width + x;
                    buffer[index] = BlendPixel(buffer[index], color);
                }
            }
        }
    }

    private static void DrawPolygonOutline(int[] buffer, int width, int height, PaintPoint[] points, double size, int color)
    {
        for (var edge = 0; edge < points.Length; edge++)
        {
            var start = points[edge];
            var end = points[(edge + 1) % points.Length];
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y))));
            for (var step = 0; step <= steps; step++)
            {
                var amount = (double)step / steps;
                StampOnto(buffer, width, height,
                    (int)Math.Round(start.X + (end.X - start.X) * amount),
                    (int)Math.Round(start.Y + (end.Y - start.Y) * amount),
                    size, color);
            }
        }
    }

    public static void BlitText(
        int[] destination, int destinationWidth, int destinationHeight,
        int[] source, int sourceWidth, int sourceHeight,
        int left, int top, double scaleX, double scaleY, double rotation)
    {
        if (Math.Abs(rotation) < 0.001)
        {
            BlitTextScaled(destination, destinationWidth, destinationHeight,
                source, sourceWidth, sourceHeight, left, top, scaleX, scaleY);
            return;
        }

        var centerX = left + sourceWidth * scaleX / 2.0;
        var centerY = top + sourceHeight * scaleY / 2.0;
        BlitTextRotated(destination, destinationWidth, destinationHeight,
            source, sourceWidth, sourceHeight, centerX, centerY, rotation, scaleX, scaleY);
    }

    public static void BlitTextScaled(
        int[] destination, int destinationWidth, int destinationHeight,
        int[] source, int sourceWidth, int sourceHeight,
        int left, int top, double scaleX, double scaleY)
    {
        var destinationW = Math.Max(1, (int)Math.Round(sourceWidth * scaleX));
        var destinationH = Math.Max(1, (int)Math.Round(sourceHeight * scaleY));
        for (var y = 0; y < destinationH; y++)
        {
            var destinationY = top + y;
            if (destinationY < 0 || destinationY >= destinationHeight)
            {
                continue;
            }
            var sourceY = Math.Min(sourceHeight - 1, (int)(y / scaleY));
            for (var x = 0; x < destinationW; x++)
            {
                var destinationX = left + x;
                if (destinationX < 0 || destinationX >= destinationWidth)
                {
                    continue;
                }
                var sourceX = Math.Min(sourceWidth - 1, (int)(x / scaleX));
                var pixel = source[sourceY * sourceWidth + sourceX];
                var alpha = (pixel >> 24) & 0xFF;
                if (alpha == 0)
                {
                    continue;
                }
                var index = destinationY * destinationWidth + destinationX;
                destination[index] = BlendPixel(destination[index], pixel);
            }
        }
    }

    public static void BlitTextRotated(
        int[] destination, int destinationWidth, int destinationHeight,
        int[] source, int sourceWidth, int sourceHeight,
        double centerX, double centerY, double angleDegrees, double scaleX, double scaleY)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var halfW = sourceWidth * scaleX / 2.0;
        var halfH = sourceHeight * scaleY / 2.0;
        double[] corners =
        [
            centerX + (-halfW) * cos - (-halfH) * sin, centerY + (-halfW) * sin + (-halfH) * cos,
            centerX + halfW * cos - (-halfH) * sin, centerY + halfW * sin + (-halfH) * cos,
            centerX + halfW * cos - halfH * sin, centerY + halfW * sin + halfH * cos,
            centerX + (-halfW) * cos - halfH * sin, centerY + (-halfW) * sin + halfH * cos
        ];
        var minX = (int)Math.Floor(Math.Min(Math.Min(corners[0], corners[2]), Math.Min(corners[4], corners[6])));
        var maxX = (int)Math.Ceiling(Math.Max(Math.Max(corners[0], corners[2]), Math.Max(corners[4], corners[6])));
        var minY = (int)Math.Floor(Math.Min(Math.Min(corners[1], corners[3]), Math.Min(corners[5], corners[7])));
        var maxY = (int)Math.Ceiling(Math.Max(Math.Max(corners[1], corners[3]), Math.Max(corners[5], corners[7])));
        for (var y = Math.Max(0, minY); y <= Math.Min(destinationHeight - 1, maxY); y++)
        {
            for (var x = Math.Max(0, minX); x <= Math.Min(destinationWidth - 1, maxX); x++)
            {
                var lx = x - centerX;
                var ly = y - centerY;
                var sourceX = (int)Math.Floor((lx * cos + ly * sin) / scaleX + sourceWidth / 2.0);
                var sourceY = (int)Math.Floor((-lx * sin + ly * cos) / scaleY + sourceHeight / 2.0);
                if (sourceX < 0 || sourceX >= sourceWidth || sourceY < 0 || sourceY >= sourceHeight)
                {
                    continue;
                }
                var pixel = source[sourceY * sourceWidth + sourceX];
                if (((pixel >> 24) & 0xFF) == 0)
                {
                    continue;
                }
                var index = y * destinationWidth + x;
                destination[index] = BlendPixel(destination[index], pixel);
            }
        }
    }
}
