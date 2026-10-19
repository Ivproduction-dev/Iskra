using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace IconForge;

internal static class Program
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            if (args.Length == 0)
            {
                return Usage();
            }

            var names = args.FirstOrDefault(arg => arg.StartsWith("--names=", StringComparison.Ordinal));
            if (names is not null)
            {
                nameFilter = names[8..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                args = args.Where(arg => arg != names).ToArray();
            }

            switch (args[0])
            {
                case "convert" when args.Length == 3:
                    return ConvertAll(args[1], args[2]);
                case "grid" when args.Length >= 4:
                    return Grid(args[1], args[2], args[3..]);
                case "gallery" when args.Length >= 3:
                    return Gallery(args[1], args[2..]);
                default:
                    return Usage();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            IconForge
              convert <svgDir> <out.xaml>                 SVG masters -> XAML ResourceDictionary
              grid    <out.png> <bgHex> <svgDir> [dirs]   contact sheet (one row per dir)
              gallery <out.html> <svgDir> [dirs]          html preview with size/theme contexts
              --names=a,b,c   render only the listed icons (grid/gallery)
            """);
        return 1;
    }

    private static int ConvertAll(string svgDir, string outPath)
    {
        var dictionary = new XDocument(
            new XElement(Wpf + "ResourceDictionary",
                new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName)));

        foreach (var file in SvgFiles(svgDir))
        {
            var icon = LoadIcon(file);
            var key = "Icon." + ToPascal(Path.GetFileNameWithoutExtension(file));
            dictionary.Root!.Add(
                new XElement(Wpf + "DrawingImage",
                    new XAttribute(X + "Key", key),
                    new XElement(Wpf + "DrawingImage.Drawing", ToXaml(icon.Group))));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, dictionary.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outPath} ({dictionary.Root!.Elements(Wpf + "DrawingImage").Count()} icons)");
        return 0;
    }

    private static int Grid(string outPng, string bgHex, string[] dirs)
    {
        var rows = dirs.Select(dir => SvgFiles(dir)
            .Select(file => (Name: Path.GetFileNameWithoutExtension(file), Icon: LoadIcon(file)))
            .ToArray()).ToArray();

        const double pad = 10;
        const double cellW = 132;
        const double cellH = 210;
        const double labelH = 26;
        var columns = rows.Max(row => row.Length);
        var width = (int)Math.Ceiling(pad * 2 + columns * cellW);
        var height = (int)Math.Ceiling(pad * 2 + rows.Length * (cellH + labelH));
        var bg = ParseColor(bgHex);
        var textBrush = new SolidColorBrush(IsDark(bg) ? Color.FromRgb(0xF0, 0xED, 0xE6) : Color.FromRgb(0x44, 0x40, 0x3A));
        var subBrush = new SolidColorBrush(IsDark(bg) ? Color.FromRgb(0xAA, 0xA5, 0x9C) : Color.FromRgb(0x8A, 0x85, 0x7C));
        var sizes = new[] { 64.0, 34.0, 22.0, 16.0 };

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, width, height));
            for (var r = 0; r < rows.Length; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    var x0 = pad + c * cellW;
                    var y0 = pad + r * (cellH + labelH);
                    dc.DrawText(Text(rows[r][c].Name, 11, textBrush), new Point(x0, y0));
                    var y = y0 + 16;
                    foreach (var size in sizes)
                    {
                        var scale = size / rows[r][c].Icon.Width;
                        dc.DrawText(Text(((int)size).ToString(), 9, subBrush), new Point(x0 + 2, y + size / 2 - 6));
                        dc.PushTransform(new TranslateTransform(x0 + 22 + (cellW - 32 - size) / 2, y));
                        dc.PushTransform(new ScaleTransform(scale, scale));
                        dc.DrawDrawing(rows[r][c].Icon.Group);
                        dc.Pop();
                        dc.Pop();
                        y += size + 6;
                    }
                }
            }
        }

        SavePng(visual, width, height, outPng);
        Console.WriteLine($"Wrote {outPng} ({width}x{height})");
        return 0;
    }

    private static readonly Dictionary<string, (string Title, string Note)> StyleNotes = new()
    {
        ["a-flat"] = ("A · Плоский дуотон", "два тона, смещённая тень, глянцевый блик"),
        ["b-juicy"] = ("B · Сочный градиент", "объёмные градиенты, блики, больше оттенков"),
        ["c-outline"] = ("C · Тёплый контур", "толстый тёмный контур + плотные заливки"),
        ["w-thin"] = ("Белый · тонкий контур", "обводка 1,15 · внутри меняется только яркость белого"),
        ["w-medium"] = ("Белый · средний контур", "обводка 1,8"),
        ["w-bold"] = ("Белый · жирный контур", "обводка 2,6"),
        ["set"] = ("Набор · финал", "тонкий контур 1,15; Запуск — средний 1,8"),
    };

    private static int Gallery(string outHtml, string[] dirs)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
            <meta charset="utf-8">
            <title>Искра — образцы иконок</title>
            <style>
              :root { --ink:#141413; --body:#3d3d3a; --muted:#6c6a64; --canvas:#faf9f5; --panel:#ffffff; --dark:#211f1c; --dark2:#171614; --accent:#a9583e; }
              body { margin:0; padding:28px 34px 60px; background:var(--canvas); color:var(--ink);
                     font-family:"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif; }
              h1 { font-size:24px; margin:0 0 4px; }
              .sub { color:var(--muted); font-size:13px; margin-bottom:26px; }
              section { margin:0 0 34px; }
              h2 { font-size:17px; margin:0 0 2px; }
              .note { color:var(--muted); font-size:12px; margin:0 0 12px; }
              .card { background:var(--panel); border:1px solid #e6dfd8; border-radius:14px; padding:16px 18px; }
              .row { display:flex; align-items:flex-end; gap:26px; flex-wrap:wrap; }
              .cell { display:flex; flex-direction:column; align-items:center; gap:6px; }
              .cell .tag { font-size:10px; color:var(--muted); }
              .darkcard { background:var(--dark); border-color:#403c36; }
              .darkrow { display:flex; align-items:flex-end; gap:26px; flex-wrap:wrap; background:var(--dark); border-radius:12px; padding:14px 18px; margin-top:12px; }
              .darkrow .tag { color:#aaa59c; }
              .fab { width:68px; height:68px; border-radius:34px; background:var(--accent); display:flex; align-items:center; justify-content:center; }
              .fab:hover { background:#8f4430; }
              .fab svg { width:40px; height:40px; }
              .sizes { display:grid; grid-template-columns:repeat(auto-fill,minmax(120px,1fr)); gap:10px; }
              .chip { border-radius:10px; padding:10px 12px; display:flex; align-items:center; justify-content:center; gap:14px; }
              .chip.light { background:#ffffff; border:1px solid #e6dfd8; }
              .chip.cream { background:#f5f0e8; border:1px solid #e6dfd8; }
              .chip.dark { background:#211f1c; }
              .chip.dark2 { background:#171614; }
            </style>
            </head>
            <body>
            <h1>Искра · редизайн иконок — образцы стилей</h1>
            <div class="sub">Белые иконки: внутри меняется только яркость (белый → светло-серый), чёрный — обводка и грифель. Три толщины обводки. Сетка 24×24; размеры 16 / 22 / 34 и FAB 68.</div>
            """);

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            var (title, note) = StyleNotes.TryGetValue(name, out var styleNote)
                ? styleNote
                : (name, "");
            sb.Append($"<section><h2>{title}</h2><div class=\"note\">{note}</div><div class=\"card\">");
            sb.Append("<div class=\"row\">");
            sb.Append("<div class=\"cell\"><div class=\"fab\">");
            AppendSvgInline(sb, SvgFiles(dir).FirstOrDefault(file => Path.GetFileNameWithoutExtension(file) == "play") ?? SvgFiles(dir).First(), "fab-inline");
            sb.Append("</div><span class=\"tag\">Запуск (FAB 68)</span></div>");
            sb.Append("<div class=\"cell\"><div class=\"fab\">");
            var createFile = SvgFiles(dir).FirstOrDefault(file => Path.GetFileNameWithoutExtension(file) == "create");
            if (createFile is not null)
            {
                AppendSvgInline(sb, createFile, "fab-inline");
            }
            else
            {
                sb.Append("""
                <svg viewBox="0 0 24 24" style="width:38px;height:38px"><path d="M12,4.8 V19.2 M4.8,12 H19.2" stroke="#fff" stroke-width="3.4" stroke-linecap="round" fill="none"/><path d="M18.4,3.4 c0.28,1.0 0.62,1.36 1.62,1.64 c-1.0,0.28 -1.34,0.62 -1.62,1.62 c-0.28,-1.0 -0.62,-1.34 -1.62,-1.62 c1.0,-0.28 1.34,-0.64 1.62,-1.64 z" fill="#fff" opacity="0.9"/></svg>
                """);
            }
            sb.Append("</div><span class=\"tag\">Создать (FAB 68)</span></div>");

            foreach (var file in SvgFiles(dir))
            {
                var iconName = Path.GetFileNameWithoutExtension(file);
                if (iconName == "play")
                {
                    continue;
                }
                sb.Append("<div class=\"cell\">");
                AppendSvgInline(sb, file, "w34");
                sb.Append($"<span class=\"tag\">{iconName} · 34</span></div>");
            }
            sb.Append("</div>");

            sb.Append("<div style=\"margin-top:14px\">");
            foreach (var (chipClass, label) in new[] { ("chip light", "белая панель · 22 и 16 px"), ("chip cream", "кремовый фон #F5F0E8"), ("chip dark", "тёмная тема #211F1C") })
            {
                sb.Append($"<div class=\"tag\" style=\"margin:10px 0 6px\">{label}</div><div class=\"row\" style=\"gap:14px\">");
                foreach (var file in SvgFiles(dir))
                {
                    sb.Append($"<div class=\"{chipClass}\">");
                    AppendSvgInline(sb, file, "w22");
                    AppendSvgInline(sb, file, "w16");
                    sb.Append("</div>");
                }
                sb.Append("</div>");
            }
            sb.Append("</div>");

            sb.Append("<div class=\"darkrow\">");
            foreach (var file in SvgFiles(dir))
            {
                AppendSvgInline(sb, file, "w34");
            }
            sb.Append("<span class=\"tag\">на тёмном фоне · 34 px</span>");
            sb.Append("</div>");

            sb.Append("</div></section>");
        }

        sb.Append("</body></html>");
        File.WriteAllText(outHtml, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outHtml}");
        return 0;
    }

    private static void AppendSvgInline(StringBuilder sb, string file, string cssClass)
    {
        var text = File.ReadAllText(file).Trim();
        var open = text.IndexOf("<svg", StringComparison.Ordinal);
        var body = text[open..];
        body = body.Replace("<svg ", $"<svg class=\"{cssClass}\" ");
        sb.Append(body);
    }

    private static void SavePng(Visual visual, int width, int height, string outPath)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        using var stream = File.Create(outPath);
        encoder.Save(stream);
    }

    private static FormattedText Text(string value, double size, Brush brush) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, 1.0);

    private static bool IsDark(Color color) => (color.R + color.G + color.B) / 3 < 120;

    private static string[] SvgFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.svg")
            .Where(file => nameFilter is null || nameFilter.Contains(Path.GetFileNameWithoutExtension(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();

    private static HashSet<string>? nameFilter;

    private static string ToPascal(string value) =>
        string.Concat(value.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private sealed record Icon(DrawingGroup Group, double Width, double Height);

    private static Icon LoadIcon(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidOperationException($"no root in {path}");
        var ns = root.GetDefaultNamespace();
        var width = TryNumber(root, "width", 24);
        var height = TryNumber(root, "height", 24);
        if ((string?)root.Attribute("viewBox") is { } viewBox)
        {
            var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4)
            {
                width = double.Parse(parts[2], CultureInfo.InvariantCulture);
                height = double.Parse(parts[3], CultureInfo.InvariantCulture);
            }
        }

        var gradients = new Dictionary<string, LinearGradientBrush>();
        foreach (var element in root.Descendants(ns + "linearGradient"))
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(TryNumber(element, "x1", 0), TryNumber(element, "y1", 0)),
                EndPoint = new Point(TryNumber(element, "x2", 1), TryNumber(element, "y2", 0))
            };
            foreach (var stop in element.Elements(ns + "stop"))
            {
                var color = ParseColor((string?)stop.Attribute("stop-color") ?? "#000000");
                var alpha = TryNumber(stop, "stop-opacity", 1);
                color = Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B);
                brush.GradientStops.Add(new GradientStop(color, TryNumber(stop, "offset", 0)));
            }
            brush.Freeze();
            gradients[(string?)element.Attribute("id") ?? ""] = brush;
        }

        var group = new DrawingGroup();
        foreach (var element in root.Elements().Where(el => el.Name == ns + "path"))
        {
            var data = (string?)element.Attribute("d") ?? throw new InvalidOperationException($"path without d in {path}");
            var opacity = TryNumber(element, "opacity", 1);
            var fill = ResolveBrush((string?)element.Attribute("fill") ?? "#000000", gradients, opacity);
            Pen? pen = null;
            if ((string?)element.Attribute("stroke") is { Length: > 0 } stroke && stroke != "none")
            {
                pen = new Pen(ResolveBrush(stroke, gradients, opacity)!, TryNumber(element, "stroke-width", 1))
                {
                    StartLineCap = LineCap(element),
                    EndLineCap = LineCap(element),
                    LineJoin = LineJoin(element)
                };
                pen.Freeze();
            }
            group.Children.Add(new GeometryDrawing(fill, pen, Geometry.Parse(data)));
        }
        group.Freeze();
        return new Icon(group, width, height);
    }

    private static Brush? ResolveBrush(string value, Dictionary<string, LinearGradientBrush> gradients, double opacity)
    {
        if (value == "none")
        {
            return null;
        }
        if (value.StartsWith("url(#", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            var id = value[5..^1];
            if (!gradients.TryGetValue(id, out var gradient))
            {
                throw new InvalidOperationException($"unknown gradient {id}");
            }
            return gradient;
        }
        var color = ParseColor(value);
        if (opacity < 1)
        {
            color = Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
        }
        return new SolidColorBrush(color);
    }

    private static PenLineCap LineCap(XElement element) => (string?)element.Attribute("stroke-linecap") switch
    {
        "round" => PenLineCap.Round,
        "square" => PenLineCap.Square,
        _ => PenLineCap.Flat
    };

    private static PenLineJoin LineJoin(XElement element) => (string?)element.Attribute("stroke-linejoin") switch
    {
        "round" => PenLineJoin.Round,
        "bevel" => PenLineJoin.Bevel,
        _ => PenLineJoin.Miter
    };

    private static double TryNumber(XElement element, string attribute, double fallback) =>
        double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static Color ParseColor(string value)
    {
        var text = value.Trim().TrimStart('#');
        return text.Length switch
        {
            3 => Color.FromRgb(Convert.ToByte(new string(text[0], 2), 16), Convert.ToByte(new string(text[1], 2), 16), Convert.ToByte(new string(text[2], 2), 16)),
            6 => Color.FromRgb(Convert.ToByte(text[0..2], 16), Convert.ToByte(text[2..4], 16), Convert.ToByte(text[4..6], 16)),
            8 => Color.FromArgb(Convert.ToByte(text[0..2], 16), Convert.ToByte(text[2..4], 16), Convert.ToByte(text[4..6], 16), Convert.ToByte(text[6..8], 16)),
            _ => throw new InvalidOperationException($"bad color {value}")
        };
    }

    private static XElement ToXaml(DrawingGroup group)
    {
        var element = new XElement(Wpf + "DrawingGroup");
        foreach (var child in group.Children)
        {
            if (child is not GeometryDrawing drawing)
            {
                throw new InvalidOperationException("unsupported drawing child");
            }
            var xml = new XElement(Wpf + "GeometryDrawing",
                new XAttribute("Geometry", drawing.Geometry.ToString() ?? ""));
            if (drawing.Brush is SolidColorBrush solid)
            {
                xml.Add(new XAttribute("Brush", solid.Color.ToString()));
            }
            else if (drawing.Brush is LinearGradientBrush gradient)
            {
                var brush = new XElement(Wpf + "LinearGradientBrush",
                    new XAttribute("StartPoint", $"{Fmt(gradient.StartPoint.X)},{Fmt(gradient.StartPoint.Y)}"),
                    new XAttribute("EndPoint", $"{Fmt(gradient.EndPoint.X)},{Fmt(gradient.EndPoint.Y)}"));
                foreach (var stop in gradient.GradientStops)
                {
                    brush.Add(new XElement(Wpf + "GradientStop",
                        new XAttribute("Color", stop.Color.ToString()),
                        new XAttribute("Offset", Fmt(stop.Offset))));
                }
                xml.Add(new XElement(Wpf + "GeometryDrawing.Brush", brush));
            }
            if (drawing.Pen is { } pen)
            {
                var penXml = new XElement(Wpf + "Pen",
                    new XAttribute("Thickness", Fmt(pen.Thickness)),
                    new XAttribute("LineJoin", pen.LineJoin),
                    new XAttribute("StartLineCap", pen.StartLineCap),
                    new XAttribute("EndLineCap", pen.EndLineCap));
                if (pen.Brush is SolidColorBrush penSolid)
                {
                    penXml.Add(new XAttribute("Brush", penSolid.Color.ToString()));
                }
                else if (pen.Brush is LinearGradientBrush penGradient)
                {
                    var brush = new XElement(Wpf + "LinearGradientBrush",
                        new XAttribute("StartPoint", $"{Fmt(penGradient.StartPoint.X)},{Fmt(penGradient.StartPoint.Y)}"),
                        new XAttribute("EndPoint", $"{Fmt(penGradient.EndPoint.X)},{Fmt(penGradient.EndPoint.Y)}"));
                    foreach (var stop in penGradient.GradientStops)
                    {
                        brush.Add(new XElement(Wpf + "GradientStop",
                            new XAttribute("Color", stop.Color.ToString()),
                            new XAttribute("Offset", Fmt(stop.Offset))));
                    }
                    penXml.Add(new XElement(Wpf + "Pen.Brush", brush));
                }
                xml.Add(new XElement(Wpf + "GeometryDrawing.Pen", penXml));
            }
            element.Add(xml);
        }
        return element;
    }

    private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
