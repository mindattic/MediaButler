using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MindAttic.Export.Artifacts;

namespace MediaButler.FileBot;

/// <summary>
/// Superimposes a season-number badge in the lower-right corner of a poster
/// image when TheTVDB has no season-specific artwork to fall back on (see
/// <see cref="FileBotResult.LooksLikeSeasonArtFallback"/>). Every season folder
/// must look visually distinct — reusing the bare series poster across all
/// seasons is the exact bug <c>mediabutler-artwork-tv.groovy</c> works around,
/// and this is the last-resort guarantee for shows TheTVDB doesn't have season
/// art for.
///
/// <para>Badge style echoes MindAttic/JellyFinPoster's "MOVIES" / "TV"
/// library-tile labels (<c>draw_title_label</c> in <c>jellyfin_poster.py</c>):
/// a semi-transparent black rounded panel behind bold white text outlined in
/// black — sized down into a corner badge instead of a full-width banner.</para>
/// </summary>
public static class SeasonPosterBadge
{
    private static readonly string[] PosterFileNames = ["poster.jpg", "folder.jpg"];

    /// <summary>
    /// Badge every poster image found in <paramref name="seasonFolder"/>. Best-effort:
    /// a missing or unreadable image, or any GDI+ failure, is swallowed rather than
    /// failing the pipeline — a plain poster beats no poster.
    /// </summary>
    public static void Apply(string seasonFolder, int season)
    {
        // Zero-padded to match MediaButler's canonical "Show - Season 01" folder
        // naming (NameParser.FormatSeasonFolder) everywhere else in the library.
        var text = $"S{season:D2}";
        foreach (var name in PosterFileNames)
        {
            var path = Path.Combine(seasonFolder, name);
            if (!File.Exists(path)) continue;
            try { ApplyToFile(path, text); }
            catch { /* best-effort -- a missing badge is not a pipeline failure */ }
        }
    }

    private static void ApplyToFile(string path, string text)
    {
        // Read into memory first so the Bitmap doesn't keep a lock on the file
        // we're about to overwrite.
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        using var original = new Bitmap(ms);
        using var canvas = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(original, 0, 0, canvas.Width, canvas.Height);
            DrawBadge(g, canvas.Width, canvas.Height, text);
        }
        // Route the in-place overwrite through MindAttic.Export: same GDI+ JPEG
        // encoder and default parameters (identical bytes), written to a temp file
        // in the season folder and moved over the original, so a failure mid-save
        // can no longer leave a truncated poster behind. The path is exact, so no
        // name sanitizing.
        ArtifactWriter.WriteViaPathAsync(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path),
                (target, _) =>
                {
                    canvas.Save(target, ImageFormat.Jpeg);
                    return Task.CompletedTask;
                },
                new ArtifactOptions { Existing = ExistingArtifact.Overwrite, SanitizeName = false })
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Draws a rounded panel anchored to the bottom-right corner, sized to the
    /// text plus padding (not a fixed box), so "S1" and "S12" both fit snugly.
    /// </summary>
    private static void DrawBadge(Graphics g, int width, int height, string text)
    {
        using var fontFamily = new FontFamily("Arial");

        // Proportional to the actual poster resolution (TheTVDB posters vary
        // in size) rather than a fixed pixel budget.
        var margin = width * 0.05f;
        var maxTextWidth = width * 0.30f;
        var minFontSize = height * 0.025f;
        var fontSize = height * 0.07f;

        using var path = new GraphicsPath();
        RectangleF bounds;
        while (true)
        {
            path.Reset();
            path.AddString(text, fontFamily, (int)FontStyle.Bold, fontSize, new PointF(0, 0), StringFormat.GenericDefault);
            bounds = path.GetBounds();
            if (bounds.Width <= maxTextWidth || fontSize <= minFontSize) break;
            fontSize -= 2f;
        }

        var padX = fontSize * 0.45f;
        var padY = fontSize * 0.3f;
        var panelWidth = bounds.Width + 2 * padX;
        var panelHeight = bounds.Height + 2 * padY;
        var panelLeft = width - margin - panelWidth;
        var panelTop = height - margin - panelHeight;
        var panelRect = new RectangleF(panelLeft, panelTop, panelWidth, panelHeight);
        var cornerRadius = Math.Min(panelHeight, panelWidth) * 0.2f;

        using (var panelPath = RoundedRect(panelRect, cornerRadius))
        using (var panelBrush = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
            g.FillPath(panelBrush, panelPath);

        // Center the glyph outlines within the panel.
        var dx = panelLeft + padX - bounds.X;
        var dy = panelTop + padY - bounds.Y;
        using (var m = new Matrix())
        {
            m.Translate(dx, dy);
            path.Transform(m);
        }

        var strokeWidth = Math.Max(1.5f, fontSize / 20f);
        using var strokePen = new Pen(Color.Black, strokeWidth) { LineJoin = LineJoin.Round };
        g.DrawPath(strokePen, path);
        using var fillBrush = new SolidBrush(Color.White);
        g.FillPath(fillBrush, path);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
