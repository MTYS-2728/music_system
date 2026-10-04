using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconStudio;

/// <summary>
/// Renders the app icon from vector data (no external assets) and assembles a multi-size .ico.
/// Glyph outlines come from Phosphor Icons (MIT).
/// </summary>
internal static class Program
{
    // Phosphor Icons (MIT License, Copyright (c) 2023 Phosphor Icons) - 256x256 viewBox paths.
    private const string HeadphonesFill =
        "M232,128v56a24,24,0,0,1-24,24H192a24,24,0,0,1-24-24V144a24,24,0,0,1,24-24h23.65a87.71,87.71,0,0,0-87-80H128a88,88,0,0,0-87.64,80H64a24,24,0,0,1,24,24v40a24,24,0,0,1-24,24H48a24,24,0,0,1-24-24V128A104.11,104.11,0,0,1,201.89,54.66,103.41,103.41,0,0,1,232,128Z";

    private const string MusicNotesFill =
        "M212.92,17.71a7.89,7.89,0,0,0-6.86-1.46l-128,32A8,8,0,0,0,72,56V166.1A36,36,0,1,0,88,196V102.25l112-28V134.1A36,36,0,1,0,216,164V24A8,8,0,0,0,212.92,17.71Z";

    private const string VinylFill =
        "M128,24A104,104,0,1,0,232,128,104.11,104.11,0,0,0,128,24ZM72,128a8,8,0,0,1-16,0,72.08,72.08,0,0,1,72-72,8,8,0,0,1,0,16A56.06,56.06,0,0,0,72,128Zm32,0a24,24,0,1,1,24,24A24,24,0,0,0,104,128Zm24,72a8,8,0,0,1,0-16,56.06,56.06,0,0,0,56-56,8,8,0,0,1,16,0A72.08,72.08,0,0,1,128,200Z";

    private const string DiscFill =
        "M188.3,43.31a8,8,0,0,0-.65-.5c-.23-.16-.47-.31-.71-.45a103.85,103.85,0,1,0,1.36,1ZM128,152a24,24,0,1,1,24-24A24,24,0,0,1,128,152Zm88-24c0,2.47-.11,4.92-.31,7.34L168,126.92a39.83,39.83,0,0,0-11-26.41l27.78-39.67A87.8,87.8,0,0,1,216,128Z";

    private const string HeadphonesBold =
        "M204.73,51.85A108.07,108.07,0,0,0,20,128v56a28,28,0,0,0,28,28H64a28,28,0,0,0,28-28V144a28,28,0,0,0-28-28H44.84A84.05,84.05,0,0,1,128,44h.64a83.7,83.7,0,0,1,82.52,72H192a28,28,0,0,0-28,28v40a28,28,0,0,0,28,28h16a28,28,0,0,0,28-28V128A107.34,107.34,0,0,0,204.73,51.85ZM64,140a4,4,0,0,1,4,4v40a4,4,0,0,1-4,4H48a4,4,0,0,1-4-4V140Zm148,44a4,4,0,0,1-4,4H192a4,4,0,0,1-4-4V144a4,4,0,0,1,4-4h20Z";

    private const string WaveformFill =
        "M216,40H40A16,16,0,0,0,24,56V200a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V56A16,16,0,0,0,216,40ZM72,152a8,8,0,0,1-16,0V104a8,8,0,0,1,16,0Zm32,32a8,8,0,0,1-16,0V72a8,8,0,0,1,16,0Zm32-16a8,8,0,0,1-16,0V88a8,8,0,0,1,16,0Zm32-16a8,8,0,0,1-16,0V104a8,8,0,0,1,16,0Zm32,8a8,8,0,0,1-16,0V96a8,8,0,0,1,16,0Z";

    private const string MusicNoteFill =
        "M210.3,56.34l-80-24A8,8,0,0,0,120,40V148.26A48,48,0,1,0,136,184V98.75l69.7,20.91A8,8,0,0,0,216,112V64A8,8,0,0,0,210.3,56.34Z";

    private const string EqualizerFill =
        "M80,96a8,8,0,0,1-8,8H24a8,8,0,0,1,0-16H72A8,8,0,0,1,80,96Zm72,24H104a8,8,0,0,0,0,16h48a8,8,0,0,0,0-16Zm32-48h48a8,8,0,0,0,0-16H184a8,8,0,0,0,0,16ZM72,120H24a8,8,0,0,0-8,8v64a8,8,0,0,0,8,8H72a8,8,0,0,0,8-8V128A8,8,0,0,0,72,120ZM232,88H184a8,8,0,0,0-8,8v96a8,8,0,0,0,8,8h48a8,8,0,0,0,8-8V96A8,8,0,0,0,232,88Zm-80,64H104a8,8,0,0,0-8,8v32a8,8,0,0,0,8,8h48a8,8,0,0,0,8-8V160A8,8,0,0,0,152,152Z";

    private const string CassetteFill =
        "M156.3,96a31.92,31.92,0,0,0,0,32H99.7a31.92,31.92,0,0,0,0-32ZM72,96a16,16,0,1,0,16,16A16,16,0,0,0,72,96ZM240,64V192a16,16,0,0,1-16,16H32a16,16,0,0,1-16-16V64A16,16,0,0,1,32,48H224A16,16,0,0,1,240,64ZM186,192l-15.6-20.8A8,8,0,0,0,164,168H92a8,8,0,0,0-6.4,3.2L70,192Zm30-80a32,32,0,0,0-32-32H72a32,32,0,0,0,0,64H184A32,32,0,0,0,216,112ZM184,96a16,16,0,1,0,16,16A16,16,0,0,0,184,96Z";

    private const string RadioFill =
        "M216,64H86.51L194.3,31.67a8,8,0,0,0-4.6-15.33l-160,48h0A8,8,0,0,0,24,72V192a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V80A16,16,0,0,0,216,64ZM104,176H64a8,8,0,0,1,0-16h40a8,8,0,0,1,0,16Zm0-32H64a8,8,0,0,1,0-16h40a8,8,0,0,1,0,16Zm0-32H64a8,8,0,0,1,0-16h40a8,8,0,0,1,0,16Zm64,56a32,32,0,1,1,32-32A32,32,0,0,1,168,168Z";

    private const string SpeakerFill =
        "M160,32.25V223.69a8.29,8.29,0,0,1-3.91,7.18,8,8,0,0,1-9-.56l-65.57-51A4,4,0,0,1,80,176.16V79.84a4,4,0,0,1,1.55-3.15l65.57-51a8,8,0,0,1,10,.16A8.27,8.27,0,0,1,160,32.25ZM60,80H32A16,16,0,0,0,16,96v64a16,16,0,0,0,16,16H60a4,4,0,0,0,4-4V84A4,4,0,0,0,60,80Zm126.77,20.84a8,8,0,0,0-.72,11.3,24,24,0,0,1,0,31.72,8,8,0,1,0,12,10.58,40,40,0,0,0,0-52.88A8,8,0,0,0,186.74,100.84Zm40.89-26.17a8,8,0,1,0-11.92,10.66,64,64,0,0,1,0,85.34,8,8,0,1,0,11.92,10.66,80,80,0,0,0,0-106.66Z";

    private const string PlaylistFill =
        "M208,32H48A16,16,0,0,0,32,48V208a16,16,0,0,0,16,16H208a16,16,0,0,0,16-16V48A16,16,0,0,0,208,32ZM64,72H192a8,8,0,0,1,0,16H64a8,8,0,0,1,0-16Zm0,48h72a8,8,0,0,1,0,16H64a8,8,0,0,1,0-16Zm40,64H64a8,8,0,0,1,0-16h40a8,8,0,0,1,0,16Zm103.59-53.47a8,8,0,0,1-10.12,5.06L184,131.1V176a24,24,0,1,1-16-22.62V120a8,8,0,0,1,10.53-7.59l24,8A8,8,0,0,1,207.59,130.53Z";

    private sealed record Variant(
        string Id,
        string Group,
        string Name,
        string Glyph,
        string ColorTop,
        string ColorBottom,
        byte[] GlyphColor,
        double GlyphScale,
        bool Gloss = true);

    private const string TealTop = "#45C6A8";
    private const string TealBottom = "#146B5E";

    // Gallery: A6 is the icon currently shipped in Assets/app.ico.
    private static readonly Variant[] Variants =
    {
        new("A6", "★ 当前使用", "双音符 · 靛蓝紫", MusicNotesFill, "#8095FA", "#4B37D6", new byte[] { 255, 255, 255 }, 0.54),
        new("A5", "★ 当前使用", "耳机（实心）· 青绿（上一版）", HeadphonesFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.56),

        new("G1", "一、字形对比（统一青绿配色）", "耳机 · 实心", HeadphonesFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.56),
        new("G2", "一、字形对比（统一青绿配色）", "耳机 · 粗体", HeadphonesBold, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.58),
        new("G3", "一、字形对比（统一青绿配色）", "双音符", MusicNotesFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.54),
        new("G4", "一、字形对比（统一青绿配色）", "单音符", MusicNoteFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.54),
        new("G5", "一、字形对比（统一青绿配色）", "黑胶唱片", VinylFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.60),
        new("G6", "一、字形对比（统一青绿配色）", "CD 光盘", DiscFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.60),
        new("G7", "一、字形对比（统一青绿配色）", "均衡器", EqualizerFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.58),
        new("G8", "一、字形对比（统一青绿配色）", "磁带", CassetteFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.70),
        new("G9", "一、字形对比（统一青绿配色）", "收音机", RadioFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.68),
        new("G10", "一、字形对比（统一青绿配色）", "音箱", SpeakerFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.60),
        new("G11", "一、字形对比（统一青绿配色）", "声波方块", WaveformFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.60),
        new("G12", "一、字形对比（统一青绿配色）", "歌单", PlaylistFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.58),

        new("C1", "二、配色对比（耳机字形）", "青绿（当前）", HeadphonesFill, TealTop, TealBottom, new byte[] { 255, 255, 255 }, 0.56),
        new("C2", "二、配色对比（耳机字形）", "靛蓝紫", HeadphonesFill, "#8095FA", "#4B37D6", new byte[] { 255, 255, 255 }, 0.56),
        new("C3", "二、配色对比（耳机字形）", "日落橙红", HeadphonesFill, "#FFA45C", "#E04B57", new byte[] { 255, 255, 255 }, 0.56),
        new("C4", "二、配色对比（耳机字形）", "蓝青", HeadphonesFill, "#5AA9F5", "#2C9C8B", new byte[] { 255, 255, 255 }, 0.56),
        new("C5", "二、配色对比（耳机字形）", "深空夜 + 青绿字形", HeadphonesFill, "#22304A", "#0E1524", new byte[] { 79, 209, 180 }, 0.56),
        new("C6", "二、配色对比（耳机字形）", "紫罗兰", HeadphonesFill, "#B08CFB", "#6D28D9", new byte[] { 255, 255, 255 }, 0.56),
        new("C7", "二、配色对比（耳机字形）", "琥珀金", HeadphonesFill, "#FFCE6B", "#DE8A12", new byte[] { 255, 255, 255 }, 0.56),
        new("C8", "二、配色对比（耳机字形）", "石墨黑", HeadphonesFill, "#3A4152", "#181C26", new byte[] { 255, 255, 255 }, 0.56),
    };

    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);

        if (args.Contains("--ico"))
        {
            var variantName = args.SkipWhile(a => a != "--variant").Skip(1).FirstOrDefault() ?? "A5";
            var variant = Variants.First(v => v.Id.Equals(variantName, StringComparison.OrdinalIgnoreCase));
            var icoPath = Path.Combine(outDir, args.SkipWhile(a => a != "--out").Skip(1).FirstOrDefault() ?? "app.ico");
            WriteIco(variant, icoPath);
            Console.WriteLine($"ICO written: {icoPath} ({variant.Id} {variant.Name}, {new FileInfo(icoPath).Length} bytes)");
            return 0;
        }

        // Contact sheet: each variant as a big tile plus 48/32/16 renders scaled up 4x (nearest) for legibility checks.
        var section = args.SkipWhile(a => a != "--section").Skip(1).FirstOrDefault();
        var sheetName = section switch
        {
            "1" => "gallery-glyphs.png",
            "2" => "gallery-colors.png",
            _ => "contact-sheet.png"
        };
        var sheet = BuildContactSheet(section);
        var sheetPath = Path.Combine(outDir, sheetName);
        SavePng(sheet, sheetPath);
        Console.WriteLine($"Contact sheet: {sheetPath}");

        foreach (var variant in Variants)
            SavePng(Render(variant, 256), Path.Combine(outDir, $"preview-{variant.Id}.png"));
        Console.WriteLine("done");
        return 0;
    }

    // ------------------------------------------------------------------ rendering

    private static DrawingVisual RenderVisual(Variant variant, double size)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        var radius = size * 0.235;
        var tile = new Rect(0, 0, size, size);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0.15, 0),
            EndPoint = new Point(0.85, 1)
        };
        gradient.GradientStops.Add(new GradientStop(ParseColor(variant.ColorTop), 0));
        gradient.GradientStops.Add(new GradientStop(ParseColor(variant.ColorBottom), 1));

        dc.DrawRoundedRectangle(gradient, null, tile, radius, radius);

        if (variant.Gloss)
        {
            // Soft light from the upper-left, clipped to the tile.
            var gloss = new RadialGradientBrush
            {
                Center = new Point(0.28, 0.16),
                GradientOrigin = new Point(0.28, 0.16),
                RadiusX = 0.85,
                RadiusY = 0.85
            };
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 255, 255), 0));
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));

            dc.PushClip(new RectangleGeometry(tile, radius, radius));
            dc.DrawRectangle(gloss, null, tile);
            dc.Pop();
        }

        var geometry = Geometry.Parse(variant.Glyph);
        var bounds = geometry.Bounds;
        var target = size * variant.GlyphScale;
        var scale = target / Math.Max(bounds.Width, bounds.Height);

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(
            (size - bounds.Width * scale) / 2 - bounds.X * scale,
            (size - bounds.Height * scale) / 2 - bounds.Y * scale));

        var placed = geometry.Clone();
        placed.Transform = transform;

        var brush = new SolidColorBrush(Color.FromRgb(variant.GlyphColor[0], variant.GlyphColor[1], variant.GlyphColor[2]));
        brush.Freeze();

        if (variant.GlyphColor[0] == 255 && variant.GlyphColor[1] == 255 && variant.GlyphColor[2] == 255)
        {
            // Gentle drop shadow so the white glyph keeps contrast on light parts of the gradient.
            dc.PushOpacity(0.22);
            dc.PushTransform(new TranslateTransform(0, size * 0.012));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0, 40, 35)), null, placed);
            dc.Pop();
            dc.Pop();
        }

        dc.DrawGeometry(brush, null, placed);
        return visual;
    }

    private static BitmapSource Render(Variant variant, double size)
    {
        var visual = RenderVisual(variant, size);
        var bitmap = new RenderTargetBitmap((int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource BuildContactSheet(string? section)
    {
        var variants = section switch
        {
            "1" => Variants.Where(v => v.Id == "A5" || v.Id.StartsWith('G')).ToArray(),
            "2" => Variants.Where(v => v.Id.StartsWith('C')).ToArray(),
            _ => Variants
        };

        const int tile = 168;
        const int rowPad = 22;
        const int headerHeight = 46;
        const int labelWidth = 64;
        const int labelSpan = 210;
        int[] smallSizes = { 48, 32, 16 };
        var stripWidth = smallSizes.Sum(s => s * 4 + 14);
        var darkStrip = 250;

        // Pre-compute row layout including section headers.
        var rows = new List<(Variant? Variant, string? Header, int Y)>();
        var y = 68;
        string? currentGroup = null;
        foreach (var variant in variants)
        {
            if (variant.Group != currentGroup)
            {
                currentGroup = variant.Group;
                rows.Add((null, currentGroup, y));
                y += headerHeight;
            }
            rows.Add((variant, null, y));
            y += tile + rowPad;
        }
        var height = y + 24;

        var width = labelWidth + labelSpan + tile + stripWidth + darkStrip + 40;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF9)), null, new Rect(0, 0, width, height));

            var title = new FormattedText("聆迹 · 应用图标候选", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                22, new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x34)), 96);
            dc.DrawText(title, new Point(20, 22));

            foreach (var (variant, header, rowY) in rows)
            {
                if (header is not null)
                {
                    var headerText = new FormattedText(header, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                        16, new SolidColorBrush(Color.FromRgb(0x2C, 0x9C, 0x8B)), 96);
                    dc.DrawText(headerText, new Point(20, rowY + 12));
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0xE2, 0xE8)), 1),
                        new Point(20, rowY + 38), new Point(width - 20, rowY + 38));
                    continue;
                }

                var v = variant!;

                var id = new FormattedText(v.Id, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 15,
                    new SolidColorBrush(Color.FromRgb(0x63, 0x72, 0x8A)), 96);
                dc.DrawText(id, new Point(20, rowY + tile / 2.0 - 10));

                var label = new FormattedText(v.Name, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    14, new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x34)), 96);
                dc.DrawText(label, new Point(labelWidth + 4, rowY + tile / 2.0 - 9));

                var tileX = labelWidth + labelSpan;
                dc.DrawImage(Render(v, tile), new Rect(tileX, rowY, tile, tile));

                // 48 / 32 / 16 rendered small then blown up 4x (nearest) to judge small-size legibility.
                var x = tileX + tile + 14;
                foreach (var small in smallSizes)
                {
                    var bmp = Render(v, small);
                    var scaled = new TransformedBitmap(bmp, new ScaleTransform(4, 4));
                    var shown = small * 4;
                    dc.DrawImage(scaled, new Rect(x, rowY + (tile - shown) / 2.0, shown, shown));
                    x += shown + 14;
                }

                // Dark taskbar strip: how it actually looks on a dark Windows shell.
                var strip = new Rect(x, rowY + tile / 2.0 - 30, darkStrip - 20, 60);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27)), null, strip, 8, 8);
                var px = strip.X + 12;
                foreach (var small in new[] { 48, 32, 24, 16 })
                {
                    dc.DrawImage(Render(v, small), new Rect(px, strip.Y + (60 - small) / 2.0, small, small));
                    px += small + 10;
                }
            }
        }

        var result = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ------------------------------------------------------------------ ico container

    private static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    /// <summary>
    /// Classic ICO layout: BMP/DIB payload for every size up to 128px, PNG payload only for 256px.
    /// GDI+ (System.Drawing.Icon) cannot decode PNG entries at all - keeping DIB for the sizes the
    /// app actually loads (tray 16/32, window 32, taskbar 48) avoids a silent fallback to the
    /// generic system icon, while the shell still gets a crisp 256px PNG for Explorer's large views.
    /// </summary>
    private static void WriteIco(Variant variant, string path)
    {
        var payloads = new List<(int Size, byte[] Data)>();
        foreach (var size in IcoSizes)
        {
            var bitmap = Render(variant, size);
            payloads.Add(size <= 128
                ? (size, BuildDib(bitmap, size))
                : (size, EncodePng(bitmap)));
        }

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: icon
        writer.Write((ushort)payloads.Count);

        var offset = 6 + 16 * payloads.Count;
        foreach (var (size, data) in payloads)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);            // palette
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // planes
            writer.Write((ushort)32);         // bits per pixel
            writer.Write((uint)data.Length);
            writer.Write((uint)offset);
            offset += data.Length;
        }
        foreach (var (_, data) in payloads) writer.Write(data);
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    /// <summary>Builds a 32bpp BITMAPINFOHEADER payload (XOR bitmap + AND mask), bottom-up as the format requires.</summary>
    private static byte[] BuildDib(BitmapSource bitmap, int size)
    {
        var stride = size * 4;
        var premultiplied = new byte[stride * size];
        bitmap.CopyPixels(premultiplied, stride, 0);

        // RenderTargetBitmap hands back premultiplied BGRA; DIB wants straight alpha.
        var xor = new byte[stride * size];
        for (var i = 0; i < xor.Length; i += 4)
        {
            var a = premultiplied[i + 3];
            if (a == 0) continue;
            if (a == 255)
            {
                xor[i] = premultiplied[i];
                xor[i + 1] = premultiplied[i + 1];
                xor[i + 2] = premultiplied[i + 2];
            }
            else
            {
                xor[i] = (byte)Math.Min(255, premultiplied[i] * 255 / a);
                xor[i + 1] = (byte)Math.Min(255, premultiplied[i + 1] * 255 / a);
                xor[i + 2] = (byte)Math.Min(255, premultiplied[i + 2] * 255 / a);
            }
            xor[i + 3] = a;
        }

        var maskStride = (size + 31) / 32 * 4;
        var mask = new byte[maskStride * size];   // alpha channel drives transparency; mask stays zero

        var header = new byte[40];
        using (var memory = new MemoryStream(header))
        using (var writer = new BinaryWriter(memory))
        {
            writer.Write(40);                 // biSize
            writer.Write(size);               // biWidth
            writer.Write(size * 2);           // biHeight (XOR + AND)
            writer.Write((short)1);           // biPlanes
            writer.Write((short)32);          // biBitCount
            writer.Write(0);                  // biCompression = BI_RGB
            writer.Write(stride * size);      // biSizeImage
            writer.Write(0);                  // biXPelsPerMeter
            writer.Write(0);                  // biYPelsPerMeter
            writer.Write(0);                  // biClrUsed
            writer.Write(0);                  // biClrImportant
        }

        var result = new byte[40 + xor.Length + mask.Length];
        Buffer.BlockCopy(header, 0, result, 0, 40);
        for (var y = 0; y < size; y++)
            Buffer.BlockCopy(xor, y * stride, result, 40 + (size - 1 - y) * stride, stride);
        Buffer.BlockCopy(mask, 0, result, 40 + xor.Length, mask.Length);
        return result;
    }
}
