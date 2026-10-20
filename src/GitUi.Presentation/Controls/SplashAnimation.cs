using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace GitUi.Presentation.Controls;

/// <summary>
/// Native port of assets/branding/loading.html: hand-drawn git branches tangle, pruning shears cut
/// the knot, the branches settle into a clean colored graph and the logo lands. One run lasts
/// <see cref="DurationMs"/>; the splash window keeps it on screen at least that long.
/// </summary>
public sealed class SplashAnimation : Control
{
    public const double DurationMs = 5000;

    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer;
    private readonly SplashScene _scene = new();

    public SplashAnimation()
    {
        // ~60 fps redraws while the animation runs; the timer stops itself once it is over.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            InvalidateVisual();
            if (_clock.Elapsed.TotalMilliseconds > DurationMs + 200 && !IsWaiting)
            {
                _timer!.Stop();
            }
        });
    }

    /// <summary>
    /// Set once the minimum duration has passed but the app is still loading: the bar turns
    /// indeterminate and keeps moving.
    /// </summary>
    public bool IsWaiting
    {
        get => _isWaiting;
        set
        {
            _isWaiting = value;
            if (value && !_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
    }

    private bool _isWaiting;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clock.Restart();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var isDark = ActualThemeVariant != ThemeVariant.Light;
        context.Custom(new DrawOperation(new Rect(Bounds.Size), _scene, _clock.Elapsed.TotalMilliseconds, isDark, IsWaiting));
    }

    private sealed class DrawOperation : ICustomDrawOperation
    {
        private readonly SplashScene _scene;
        private readonly double _time;
        private readonly bool _isDark;
        private readonly bool _isWaiting;

        public DrawOperation(Rect bounds, SplashScene scene, double time, bool isDark, bool isWaiting)
        {
            Bounds = bounds;
            _scene = scene;
            _time = time;
            _isDark = isDark;
            _isWaiting = isWaiting;
        }

        public Rect Bounds { get; }

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                return;
            }

            using var lease = feature.Lease();
            _scene.Draw(lease.SkCanvas, (float)Bounds.Width, (float)Bounds.Height, _time, _isDark, _isWaiting);
        }
    }
}

/// <summary>Geometry and timeline of the splash; coordinates live in a 1200 × 760 scene scaled to fit.</summary>
internal sealed class SplashScene
{
    private const float SceneWidth = 1200f;
    private const float SceneHeight = 760f;

    private static readonly string[] Branches = { "main", "feature/login", "bugfix/typo", "feature/ui", "hotfix/1.2.3", "release/v1.2", "dev" };

    private static readonly SKColor[] LaneColors =
    {
        SKColor.Parse("#15A0BF"), SKColor.Parse("#0669F7"), SKColor.Parse("#8E00C2"), SKColor.Parse("#C517B6"),
        SKColor.Parse("#D90171"), SKColor.Parse("#F25D2E"), SKColor.Parse("#2ECE9D"),
    };

    private static readonly SKColor Accent = SKColor.Parse("#15A0BF");

    private static readonly (float X, float Y)[] Anchors =
    {
        (210, 120), (540, 70), (150, 230), (130, 350), (150, 470), (250, 580), (600, 600),
    };

    private static readonly (double At, string Text)[] Messages =
    {
        (0, "Reading refs…"), (1100, "Following every branch…"), (2300, "Pruning the tangles…"),
        (3300, "Laying out the graph…"), (4500, "Ready"),
    };

    // Shears paths, in the 512 × 512 space of assets/branding/logo.svg (pivot at 304, 232).
    private static readonly SKPath HandleA = SKPath.ParseSvgPathData("M 262 236 C 236 236 214 250 206 270 C 186 316 150 362 108 414 C 90 436 104 466 130 456 C 178 420 224 366 252 316 C 264 294 276 276 292 266 Z");
    private static readonly SKPath HandleB = SKPath.ParseSvgPathData("M 316 262 C 330 280 332 300 320 322 C 302 360 290 404 280 446 C 274 474 302 484 312 458 C 330 410 352 362 362 318 C 368 290 360 268 344 254 Z");
    private static readonly SKPath Spring = SKPath.ParseSvgPathData("M 264 292 L 300 312");
    private static readonly SKPath UpperBlade = SKPath.ParseSvgPathData("M 272 216 C 262 150 300 80 390 46 C 404 40 412 52 404 64 C 372 110 350 160 334 214 Z");
    private static readonly SKPath LowerBlade = SKPath.ParseSvgPathData("M 318 254 C 360 236 408 206 454 170 C 466 162 478 172 470 184 C 446 232 400 262 340 272 Z");
    private static readonly SKPath Twig = SKPath.ParseSvgPathData("M 392 318 L 432 360 L 470 344 M 432 360 L 440 410");

    private static readonly SKTypeface HandTypeface =
        SKTypeface.FromFamilyName("Segoe Print") ?? SKTypeface.FromFamilyName("Comic Sans MS") ?? SKTypeface.Default;
    private static readonly SKTypeface UiTypeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    private static readonly SKTypeface UiBoldTypeface =
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.ExtraBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;

    private readonly List<(SKPath Path, float Width)> _strands = new();
    private readonly List<(float X, float Y, int Strand, float Order)> _tangleNodes = new();
    private readonly List<(SKPath Path, SKColor Color, List<(float X, float Y)> Commits, float LabelY)> _lanes = new();
    private readonly List<(float X, float Y, float Dx, float Dy, float Spin, float Bend)> _twigs = new();

    public SplashScene()
    {
        BuildTangle();
        BuildLanes();
        BuildTwigs();
    }

    // ---------- geometry ----------

    /// <summary>The seeded generator of loading.html, so the tangle has the same shape.</summary>
    private static Func<float> Random(ulong seed) => () =>
    {
        seed = (seed * 1664525UL + 1013904223UL) % 4294967296UL;
        return (float)(seed / 4294967296.0);
    };

    private void BuildTangle()
    {
        var random = Random(42);
        for (var i = 0; i < Anchors.Length; i++)
        {
            var (x, y) = Anchors[i];
            var points = new List<SKPoint> { new(x + 70 * Math.Sign(600 - x), y + (y < 320 ? 30 : -30)) };
            for (var k = 0; k < 4; k++)
            {
                points.Add(new SKPoint(360 + random() * 420, 170 + random() * 300));
            }

            points.Add(new SKPoint(760 + random() * 70, 240 + random() * 140));

            var builder = new SKPathBuilder();
            builder.MoveTo(points[0]);
            for (var k = 1; k < points.Count; k++)
            {
                var p = points[k - 1];
                var c = points[k];
                var bend = (random() - .5f) * 220;
                builder.CubicTo(p.X + bend, p.Y - bend, c.X - bend, c.Y + bend, c.X, c.Y);
            }

            var path = builder.Detach();

            _strands.Add((path, 4.5f));

            // Loose ends that sprout and stop on a commit, as in the illustration.
            if (i % 2 == 0)
            {
                using var measure = new SKPathMeasure(path);
                measure.GetPosition(measure.Length * (.3f + random() * .3f), out var start);
                var ex = start.X + (random() - .5f) * 140;
                var ey = start.Y + (random() - .5f) * 140;
                var stub = new SKPathBuilder();
                stub.MoveTo(start);
                stub.QuadTo((start.X + ex) / 2 + 30, (start.Y + ey) / 2 - 30, ex, ey);
                _strands.Add((stub.Detach(), 4f));
            }
        }

        for (var s = 0; s < _strands.Count; s++)
        {
            var (path, width) = _strands[s];
            using var measure = new SKPathMeasure(path);
            var count = width < 4.5f ? 1 : 3;
            for (var k = 1; k <= count; k++)
            {
                var order = count == 1 ? 1f : (float)k / (count + 1);
                measure.GetPosition(measure.Length * order, out var point);
                _tangleNodes.Add((point.X, point.Y, s, order));
            }
        }
    }

    private void BuildLanes()
    {
        static float LaneY(int i) => 140 + i * 62;
        const float left = 330, right = 1010;
        for (var i = 0; i < Branches.Length; i++)
        {
            var y = LaneY(i);
            var fork = left + 30 + i * 58;
            var merges = i <= 3;
            var merge = merges ? right - 40 - i * 62 : Math.Max(fork + 220, right - 150 - (i - 4) * 70);
            var lane = new SKPathBuilder();
            if (i == 0)
            {
                lane.MoveTo(left, y);
                lane.LineTo(right, y);
            }
            else
            {
                lane.MoveTo(fork - 40, LaneY(0));
                lane.CubicTo(fork - 10, LaneY(0), fork - 10, y, fork + 20, y);
                lane.LineTo(merge - 20, y);
                if (merges)
                {
                    lane.CubicTo(merge + 10, y, merge + 10, LaneY(0), merge + 40, LaneY(0));
                }
            }

            var path = lane.Detach();

            var commits = new List<(float, float)>();
            var start = i == 0 ? left + 30 : fork + 50;
            var end = i == 0 ? right - 20 : merge - 30;
            for (var x = start; x <= end; x += 92)
            {
                commits.Add((x + (i % 2) * 24, y));
            }

            _lanes.Add((path, LaneColors[i], commits, y));
        }
    }

    private void BuildTwigs()
    {
        var random = Random(7);
        for (var i = 0; i < 9; i++)
        {
            _twigs.Add((560 + random() * 180, 250 + random() * 150, (random() - .5f) * 120, 180 + random() * 140, (random() - .5f) * 140, random()));
        }
    }

    // ---------- timeline helpers ----------

    private static float Progress(double time, double delay, double duration) =>
        (float)Math.Clamp((time - delay) / duration, 0, 1);

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }

    /// <summary>Scale of a "pop" (0.2 → 1.12 → 1) and its opacity.</summary>
    private static (float Scale, float Alpha) Pop(double time, double delay, double duration = 260)
    {
        var t = Progress(time, delay, duration);
        if (t <= 0)
        {
            return (0.2f, 0f);
        }

        var scale = t < .7f ? .2f + (1.12f - .2f) * Smooth(t / .7f) : 1.12f - .12f * Smooth((t - .7f) / .3f);
        return (scale, Math.Min(1f, t / .7f));
    }

    private static float Keyframes(float t, params (float At, float Value)[] points)
    {
        for (var i = 1; i < points.Length; i++)
        {
            if (t <= points[i].At)
            {
                var k = (t - points[i - 1].At) / (points[i].At - points[i - 1].At);
                return points[i - 1].Value + (points[i].Value - points[i - 1].Value) * Smooth(Math.Clamp(k, 0, 1));
            }
        }

        return points[^1].Value;
    }

    // ---------- drawing ----------

    public void Draw(SKCanvas canvas, float width, float height, double time, bool isDark, bool isWaiting)
    {
        var paper = isDark ? SKColor.Parse("#1C1E23") : SKColors.White;
        var ink = isDark ? SKColor.Parse("#F2F2F2") : SKColor.Parse("#111111");
        var muted = ink.WithAlpha(150);
        canvas.Clear(paper);

        var scale = Math.Min(width / SceneWidth, height / SceneHeight);
        canvas.Save();
        canvas.Translate((width - SceneWidth * scale) / 2, (height - SceneHeight * scale) / 2);
        canvas.Scale(scale);

        using var sketch = SKPathEffect.CreateDiscrete(9, 1.4f, 7);
        using var inkPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = ink, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, PathEffect = sketch };
        using var paperFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = paper };

        DrawTangle(canvas, time, inkPaint, paperFill, ink, paper);
        DrawLanes(canvas, time, paper, ink);
        DrawTwigs(canvas, time, inkPaint, paperFill);
        DrawShears(canvas, time, ink, paper);
        DrawSpark(canvas, time, ink);
        DrawLockup(canvas, time, ink, paper, muted);
        DrawStatus(canvas, time, ink, muted, isWaiting);

        canvas.Restore();
    }

    private void DrawTangle(SKCanvas canvas, double time, SKPaint inkPaint, SKPaint paperFill, SKColor ink, SKColor paper)
    {
        var fade = 1 - Smooth(Progress(time, 2700, 650));
        if (fade <= 0)
        {
            return;
        }

        canvas.Save();
        // 1.2 – 2.3 s: the knot strains, like hands pulling on it.
        var strain = Progress(time, 1200, 1100);
        if (strain is > 0 and < 1)
        {
            var wave = MathF.Sin(strain * MathF.PI * 4) * (1 - strain);
            canvas.Translate(6 * wave, -3 * wave);
            canvas.RotateDegrees(.6f * wave, 600, 330);
        }

        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * fade)) };
        canvas.SaveLayer(layer);

        for (var i = 0; i < _strands.Count; i++)
        {
            var (path, strokeWidth) = _strands[i];
            var t = Smooth(Progress(time, 150 + i * 70, 1150));
            if (t <= 0)
            {
                continue;
            }

            using var measure = new SKPathMeasure(path);
            using var segment = new SKPathBuilder();
            measure.GetSegment(0, measure.Length * t, segment, true);
            using var partial = segment.Detach();
            inkPaint.StrokeWidth = strokeWidth;
            canvas.DrawPath(partial, inkPaint);
        }

        using var nodeStroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, Color = ink };
        foreach (var (x, y, strand, order) in _tangleNodes)
        {
            var (s, alpha) = Pop(time, 150 + strand * 70 + 1150 * order, 220);
            if (alpha <= 0)
            {
                continue;
            }

            nodeStroke.Color = ink.WithAlpha((byte)(255 * alpha));
            canvas.DrawCircle(x, y, 9 * s, paperFill);
            canvas.DrawCircle(x, y, 9 * s, nodeStroke);
        }

        for (var i = 0; i < Anchors.Length; i++)
        {
            var (s, alpha) = Pop(time, 80 + i * 90);
            DrawLabel(canvas, Anchors[i].X, Anchors[i].Y, Branches[i], null, s, alpha, ink, paper);
        }

        canvas.Restore();
        canvas.Restore();
    }

    private void DrawLabel(SKCanvas canvas, float x, float y, string text, SKColor? dot, float scale, float alpha, SKColor ink, SKColor paper)
    {
        if (alpha <= 0)
        {
            return;
        }

        using var font = new SKFont(HandTypeface, 22);
        var textWidth = font.MeasureText(text);
        var width = textWidth + 26 + (dot is null ? 0 : 20);
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(scale);
        var rect = new SKRect(-width / 2, -19, width / 2, 19);
        using var fill = new SKPaint { IsAntialias = true, Color = paper.WithAlpha((byte)(255 * alpha)) };
        using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.5f, Color = ink.WithAlpha((byte)(255 * alpha)) };
        canvas.DrawRoundRect(rect, 10, 10, fill);
        canvas.DrawRoundRect(rect, 10, 10, border);
        var textX = -textWidth / 2;
        if (dot is { } color)
        {
            using var dotPaint = new SKPaint { IsAntialias = true, Color = color.WithAlpha((byte)(255 * alpha)) };
            canvas.DrawCircle(-width / 2 + 14, 0, 5, dotPaint);
            textX += 10;
        }

        using var textPaint = new SKPaint { IsAntialias = true, Color = ink.WithAlpha((byte)(255 * alpha)) };
        canvas.DrawText(text, textX, 8, SKTextAlign.Left, font, textPaint);
        canvas.Restore();
    }

    private void DrawLanes(SKCanvas canvas, double time, SKColor paper, SKColor ink)
    {
        if (time < 3200)
        {
            return;
        }

        var dim = 1 - .78f * Smooth(Progress(time, 4300, 500));
        using var lanePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5, StrokeCap = SKStrokeCap.Round };
        using var commitFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = paper };

        for (var i = 0; i < _lanes.Count; i++)
        {
            var (path, color, commits, labelY) = _lanes[i];
            var faded = color.WithAlpha((byte)(255 * dim));
            var t = Smooth(Progress(time, 3200 + i * 80, 700));
            if (t > 0)
            {
                using var measure = new SKPathMeasure(path);
                using var segment = new SKPathBuilder();
                measure.GetSegment(0, measure.Length * t, segment, true);
                using var partial = segment.Detach();
                lanePaint.Color = faded;
                canvas.DrawPath(partial, lanePaint);
            }

            for (var k = 0; k < commits.Count; k++)
            {
                var (s, alpha) = Pop(time, 3500 + i * 80 + k * 45, 220);
                if (alpha <= 0)
                {
                    continue;
                }

                lanePaint.Color = color.WithAlpha((byte)(255 * alpha * dim));
                canvas.DrawCircle(commits[k].X, commits[k].Y, 9 * s, commitFill);
                canvas.DrawCircle(commits[k].X, commits[k].Y, 9 * s, lanePaint);
            }

            var (labelScale, labelAlpha) = Pop(time, 3200 + i * 80, 320);
            DrawLabel(canvas, 205, labelY, Branches[i], color, labelScale, labelAlpha * dim, ink, paper);
        }
    }

    private void DrawTwigs(SKCanvas canvas, double time, SKPaint inkPaint, SKPaint paperFill)
    {
        for (var i = 0; i < _twigs.Count; i++)
        {
            var t = Progress(time, 2550 + i * 70, 900);
            if (t <= 0 || t >= 1)
            {
                continue;
            }

            var (x, y, dx, dy, spin, bend) = _twigs[i];
            var fall = t * t;
            canvas.Save();
            canvas.Translate(dx * fall, dy * fall);
            canvas.RotateDegrees(spin * fall, x, y);
            using var pieceBuilder = new SKPathBuilder();
            pieceBuilder.MoveTo(x, y);
            pieceBuilder.QuadTo(x + 20 + bend * 30, y - 20 + bend * 40, x + 50 + bend * 30, y + bend * 30);
            using var piece = pieceBuilder.Detach();
            inkPaint.StrokeWidth = 4.5f;
            inkPaint.Color = inkPaint.Color.WithAlpha((byte)(255 * (1 - t)));
            canvas.DrawPath(piece, inkPaint);
            canvas.DrawCircle(x, y, 8, paperFill);
            canvas.DrawCircle(x, y, 8, inkPaint);
            inkPaint.Color = inkPaint.Color.WithAlpha(255);
            canvas.Restore();
        }
    }

    private static void DrawShears(SKCanvas canvas, double time, SKColor ink, SKColor paper)
    {
        var enter = Progress(time, 1850, 520);
        var exit = Smooth(Progress(time, 3250, 450));
        if (enter <= 0 || exit >= 1)
        {
            return;
        }

        var e = EaseOutBack(enter);
        var alpha = Math.Min(1f, enter * 2) * (1 - exit);
        var snip = Keyframes(Progress(time, 2250, 900), (0, 0), (.3f, 14), (.55f, 0), (.75f, 12), (1, 0));

        canvas.Save();
        canvas.Translate(380 * (1 - e) + 60 * exit, -40 * (1 - e) - 30 * exit);
        canvas.RotateDegrees(18 * (1 - e), 800, 330);
        canvas.Translate(800, 330);
        canvas.RotateDegrees(-135);
        canvas.Scale(.6f * (1 - .15f * exit));
        canvas.Translate(-304, -232);

        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * alpha)) };
        canvas.SaveLayer(layer);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = paper };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 14, Color = ink, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

        void Half(float angle, params SKPath[] parts)
        {
            canvas.Save();
            canvas.RotateDegrees(angle, 304, 232);
            foreach (var part in parts)
            {
                canvas.DrawPath(part, fill);
                canvas.DrawPath(part, stroke);
            }

            canvas.Restore();
        }

        Half(-snip, HandleA, LowerBlade);
        Half(snip, HandleB, UpperBlade);
        canvas.DrawCircle(304, 232, 34, fill);
        canvas.DrawCircle(304, 232, 34, stroke);
        using var dot = new SKPaint { IsAntialias = true, Color = ink };
        canvas.DrawCircle(304, 232, 13, dot);
        canvas.Restore();
        canvas.Restore();
    }

    private static void DrawSpark(SKCanvas canvas, double time, SKColor ink)
    {
        foreach (var (at, index) in new[] { (2520.0, 0), (2950.0, 1) })
        {
            var t = Progress(time, at, 320);
            if (t is <= 0 or >= 1)
            {
                continue;
            }

            var scale = t < .4f ? .4f + .7f * (t / .4f) : 1.1f + .3f * ((t - .4f) / .6f);
            var alpha = t < .4f ? t / .4f : 1 - (t - .4f) / .6f;
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5, StrokeCap = SKStrokeCap.Round, Color = ink.WithAlpha((byte)(255 * alpha)) };
            canvas.Save();
            canvas.Translate(688 - index * 14, 318 + index * 18);
            canvas.Scale(scale);
            for (var a = 0; a < 8; a++)
            {
                var angle = a * MathF.PI / 4;
                var (inner, outer) = a % 2 == 0 ? (16f, 34f) : (12f, 26f);
                canvas.DrawLine(MathF.Cos(angle) * inner, MathF.Sin(angle) * inner, MathF.Cos(angle) * outer, MathF.Sin(angle) * outer, paint);
            }

            canvas.Restore();
        }
    }

    private static void DrawLockup(SKCanvas canvas, double time, SKColor ink, SKColor paper, SKColor muted)
    {
        var t = Progress(time, 4300, 600);
        if (t <= 0)
        {
            return;
        }

        var e = EaseOutBack(t);
        canvas.Save();
        canvas.Translate(600, 330 + 18 * (1 - e));
        canvas.Scale(.86f + .14f * e);
        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * Math.Min(1, t * 1.6f))) };
        canvas.SaveLayer(layer);

        var card = new SKRect(-200, -70, 200, 70);
        using var shadow = new SKPaint { IsAntialias = true, Color = SKColors.Black.WithAlpha(70), ImageFilter = SKImageFilter.CreateBlur(24, 24) };
        canvas.DrawRoundRect(new SKRect(card.Left, card.Top + 14, card.Right, card.Bottom + 14), 22, 22, shadow);
        using var cardFill = new SKPaint { IsAntialias = true, Color = paper };
        using var cardBorder = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = ink.WithAlpha(40) };
        canvas.DrawRoundRect(card, 22, 22, cardFill);
        canvas.DrawRoundRect(card, 22, 22, cardBorder);

        DrawMark(canvas, new SKRect(-180, -48, -84, 48), ink, paper);

        using var wordFont = new SKFont(UiBoldTypeface, 56);
        using var inkPaint = new SKPaint { IsAntialias = true, Color = ink };
        using var accentPaint = new SKPaint { IsAntialias = true, Color = Accent };
        canvas.DrawText("Git", -66, 12, SKTextAlign.Left, wordFont, inkPaint);
        canvas.DrawText("UI", -66 + wordFont.MeasureText("Git"), 12, SKTextAlign.Left, wordFont, accentPaint);
        using var taglineFont = new SKFont(HandTypeface, 19);
        using var mutedPaint = new SKPaint { IsAntialias = true, Color = muted };
        canvas.DrawText("untangle your branches", -64, 44, SKTextAlign.Left, taglineFont, mutedPaint);

        canvas.Restore();
        canvas.Restore();
    }

    /// <summary>The logo mark (shears + pruned twig) fitted into <paramref name="bounds"/>.</summary>
    private static void DrawMark(SKCanvas canvas, SKRect bounds, SKColor ink, SKColor paper)
    {
        canvas.Save();
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.Scale(bounds.Width / 512f);
        canvas.Translate(-34, 4);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = paper };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 14, Color = ink, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        foreach (var part in new[] { HandleA, HandleB, UpperBlade, LowerBlade })
        {
            canvas.DrawPath(part, fill);
            canvas.DrawPath(part, stroke);
        }

        stroke.StrokeWidth = 22;
        canvas.DrawPath(Spring, stroke);
        stroke.StrokeWidth = 14;
        canvas.DrawCircle(304, 232, 34, fill);
        canvas.DrawCircle(304, 232, 34, stroke);
        using var dot = new SKPaint { IsAntialias = true, Color = ink };
        canvas.DrawCircle(304, 232, 13, dot);

        canvas.Save();
        canvas.Translate(18, -34);
        canvas.RotateDegrees(-18, 432, 360);
        stroke.StrokeWidth = 12;
        canvas.DrawPath(Twig, stroke);
        canvas.DrawCircle(470, 344, 13, fill);
        canvas.DrawCircle(470, 344, 13, stroke);
        canvas.DrawCircle(440, 410, 13, fill);
        canvas.DrawCircle(440, 410, 13, stroke);
        canvas.Restore();
        canvas.Restore();
    }

    private static void DrawStatus(SKCanvas canvas, double time, SKColor ink, SKColor muted, bool isWaiting)
    {
        var message = isWaiting ? "Still loading…" : Messages.Last(m => time >= m.At).Text;
        using var font = new SKFont(UiTypeface, 15);
        using var paint = new SKPaint { IsAntialias = true, Color = muted };
        canvas.DrawText(message, 600, 676, SKTextAlign.Center, font, paint);

        var track = new SKRect(390, 692, 810, 696);
        using var trackPaint = new SKPaint { IsAntialias = true, Color = ink.WithAlpha(30) };
        canvas.DrawRoundRect(track, 2, 2, trackPaint);
        using var barPaint = new SKPaint { IsAntialias = true, Color = Accent };
        if (isWaiting)
        {
            // Indeterminate: a segment sweeping across while the app finishes loading.
            var phase = (float)(time % 1100 / 1100);
            var start = track.Left + (track.Width + 150) * phase - 150;
            canvas.Save();
            canvas.ClipRect(track);
            canvas.DrawRoundRect(new SKRect(start, track.Top, start + 150, track.Bottom), 2, 2, barPaint);
            canvas.Restore();
        }
        else
        {
            var p = Keyframes(Progress(time, 0, SplashAnimation.DurationMs), (0, 0), (.35f, .38f), (.6f, .62f), (.85f, .86f), (1, 1));
            canvas.DrawRoundRect(new SKRect(track.Left, track.Top, track.Left + track.Width * p, track.Bottom), 2, 2, barPaint);
        }
    }
}
