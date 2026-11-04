
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WifiHeatMap1
{
    // Produces real floor-plan + heat-map snapshots independently of
    // whichever workspace is currently visible in the dashboard.
    public static class MapReportRenderer
    {
        public static byte[] Create(
            BitmapSource floorPlan,
            IReadOnlyList<SamplePoint> samples,
            IReadOnlyList<WallSegment> walls,
            Point? router,
            double mapUnitsPerMeter,
            bool survey,
            Point? suggestedRouter = null)
        {
            double sourceWidth = floorPlan.Width;
            double sourceHeight = floorPlan.Height;

            // Cap the longest side so PDF creation stays practical.
            double factor = Math.Min(
                1.0, 1400.0 / Math.Max(sourceWidth, sourceHeight));

            int width = Math.Max(1, (int)Math.Round(sourceWidth * factor));
            int height = Math.Max(1, (int)Math.Round(sourceHeight * factor));

            double sx = width / sourceWidth;
            double sy = height / sourceHeight;

            Point Scale(Point p) => new(p.X * sx, p.Y * sy);

            List<SamplePoint> scaledSamples = samples.Select(s => new SamplePoint
            {
                Position = Scale(s.Position),
                Rssi = s.Rssi
            }).ToList();

            List<WallSegment> scaledWalls = walls.Select(w => new WallSegment
            {
                Start = Scale(w.Start),
                End = Scale(w.End),
                Material = w.Material,
                CustomAttenuationDb = w.CustomAttenuationDb
            }).ToList();

            Point? scaledRouter = router.HasValue
                ? Scale(router.Value) : null;
            Point? scaledSuggestion = suggestedRouter.HasValue
                ? Scale(suggestedRouter.Value) : null;

            DrawingVisual visual = new();
            using (DrawingContext dc = visual.RenderOpen())
            {
                Rect area = new(0, 0, width, height);
                dc.DrawRectangle(Brushes.White, null, area);
                dc.DrawImage(floorPlan, area);

                if (survey && scaledSamples.Count >= 3)
                {
                    var overlay = HeatMapRenderer.RenderIdwHeatMap(
                        width, height, scaledSamples,
                        mapUnitsPerMeter * factor);
                    dc.DrawImage(overlay, area);
                }
                else if (!survey && scaledRouter.HasValue)
                {
                    var overlay = HeatMapRenderer.RenderPredictiveHeatMap(
                        width, height, scaledRouter.Value,
                        scaledWalls, mapUnitsPerMeter * factor);
                    dc.DrawImage(overlay, area);
                }

                if (survey)
                {
                    int index = 1;
                    foreach (SamplePoint sample in scaledSamples)
                    {
                        Point p = sample.Position;
                        dc.DrawEllipse(Brushes.DeepSkyBlue,
                            new Pen(Brushes.White, 2), p, 12, 12);
                        DrawCenteredText(dc, index.ToString(CultureInfo.InvariantCulture),
                            p, 10, Brushes.Black);
                        index++;
                    }
                }
                else
                {
                    foreach (WallSegment wall in scaledWalls)
                    {
                        dc.DrawLine(new Pen(GetWallBrush(wall.Material), 4),
                            wall.Start, wall.End);
                    }

                    if (scaledSuggestion.HasValue)
                    {
                        dc.DrawEllipse(Brushes.ForestGreen,
                            new Pen(Brushes.White, 2.5),
                            scaledSuggestion.Value, 19, 19);
                        DrawCenteredText(dc, "BEST", scaledSuggestion.Value,
                            10, Brushes.White);
                    }

                    if (scaledRouter.HasValue)
                    {
                        dc.DrawEllipse(Brushes.DarkRed,
                            new Pen(Brushes.White, 2.5),
                            scaledRouter.Value, 17, 17);
                        DrawCenteredText(dc, "AP", scaledRouter.Value,
                            12, Brushes.White);
                    }
                }
            }

            RenderTargetBitmap bitmap = new(
                width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using MemoryStream stream = new();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static void DrawCenteredText(
            DrawingContext dc,
            string text,
            Point center,
            double fontSize,
            Brush color)
        {
            FormattedText formatted = new(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                color,
                1.0);

            dc.DrawText(formatted, new Point(
                center.X - formatted.Width / 2,
                center.Y - formatted.Height / 2));
        }

        private static Brush GetWallBrush(WallMaterial material) => material switch
        {
            WallMaterial.Glass => Brushes.LightBlue,
            WallMaterial.Brick => Brushes.Orange,
            WallMaterial.Concrete => Brushes.Red,
            WallMaterial.Custom => Brushes.MediumPurple,
            _ => Brushes.Gold
        };
    }
}
