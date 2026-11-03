
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WifiHeatMap1
{
    public static class HeatMapRenderer
    {
        // ==========================================
        // MEASURED COVERAGE: IDW
        // ==========================================

        public static WriteableBitmap RenderIdwHeatMap(
            int width,
            int height,
            List<SamplePoint> samples,
            double pixelsPerMeter = 35.0,
            int step = 4)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            WriteableBitmap bitmap = new(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null);

            if (samples.Count == 0)
                return bitmap;

            step = Math.Max(1, step);

            byte[] pixels = new byte[
                checked(width * height * 4)];

            double radius = pixelsPerMeter * 12.0;

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    double weightTotal = 0;
                    double rssiTotal = 0;
                    double nearestDistance = double.MaxValue;

                    foreach (SamplePoint sample in samples)
                    {
                        double distance = Math.Sqrt(
                            Math.Pow(
                                x - sample.Position.X, 2)
                            +
                            Math.Pow(
                                y - sample.Position.Y, 2));

                        nearestDistance = Math.Min(
                            nearestDistance, distance);

                        double weight =
                            1.0 /
                            Math.Pow(
                                Math.Max(1.0, distance), 2);

                        weightTotal += weight;
                        rssiTotal += weight * sample.Rssi;
                    }

                    if (nearestDistance > radius)
                        continue;

                    double estimate =
                        rssiTotal / weightTotal -
                        3.0 * (
                            nearestDistance / pixelsPerMeter);

                    PaintBlock(
                        pixels,
                        width,
                        height,
                        x,
                        y,
                        step,
                        RssiToColor(estimate));
                }
            }

            bitmap.WritePixels(
                new Int32Rect(0, 0, width, height),
                pixels,
                width * 4,
                0);

            return bitmap;
        }

        // ==========================================
        // SHARED PREDICTIVE RSSI MODEL
        // ==========================================

        public static double PredictRssi(
            Point router,
            Point receiver,
            IReadOnlyList<WallSegment> walls,
            double pixelsPerMeter,
            double txPowerDbm = 20)
        {
            if (pixelsPerMeter <= 0 ||
                !double.IsFinite(pixelsPerMeter))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pixelsPerMeter));
            }

            double distanceMapUnits =
                (receiver - router).Length;

            double distanceMeters = Math.Max(
                0.5,
                distanceMapUnits / pixelsPerMeter);

            double pathLoss =
                20.0 * Math.Log10(distanceMeters) + 40.0;

            double wallLoss = 0.0;

            foreach (WallSegment wall in walls)
            {
                if (wall.IntersectsLine(router, receiver))
                {
                    wallLoss += wall.AttenuationDb;
                }
            }

            return txPowerDbm - pathLoss - wallLoss;
        }

        // ==========================================
        // PREDICTIVE HEAT MAP
        // ==========================================

        public static WriteableBitmap RenderPredictiveHeatMap(
            int width,
            int height,
            Point router,
            List<WallSegment> walls,
            double pixelsPerMeter = 35.0,
            double txPowerDbms = 20,
            int step = 4)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            WriteableBitmap bitmap = new(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null);

            byte[] pixels = new byte[
                checked(width * height * 4)];

            step = Math.Max(1, step);

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    double rssi = PredictRssi(
                        router,
                        new Point(x, y),
                        walls,
                        pixelsPerMeter,
                        txPowerDbms);

                    PaintBlock(
                        pixels,
                        width,
                        height,
                        x,
                        y,
                        step,
                        RssiToColor(rssi));
                }
            }

            bitmap.WritePixels(
                new Int32Rect(0, 0, width, height),
                pixels,
                width * 4,
                0);

            return bitmap;
        }

        // ==========================================
        // WRITE COLORED PIXELS
        // ==========================================

        private static void PaintBlock(
            byte[] pixels,
            int width,
            int height,
            int x,
            int y,
            int step,
            Color color)
        {
            for (int dy = 0;
                 dy < step && y + dy < height;
                 dy++)
            {
                for (int dx = 0;
                     dx < step && x + dx < width;
                     dx++)
                {
                    int index =
                        ((y + dy) * width + x + dx) * 4;

                    pixels[index] = color.B;
                    pixels[index + 1] = color.G;
                    pixels[index + 2] = color.R;
                    pixels[index + 3] = color.A;
                }
            }
        }

        // ==========================================
        // RSSI COLOR GRADIENT
        // ==========================================

        public static Color RssiToColor(double rssi)
        {
            if (rssi < -88)
                return Colors.Transparent;

            double norm = Math.Clamp(
                (rssi + 88.0) / 58.0,
                0.0,
                1.0);

            const byte alpha = 160;

            if (norm > 0.75)
            {
                double t = (norm - 0.75) / 0.25;

                return Color.FromArgb(
                    alpha,
                    255,
                    (byte)(255 * (1 - t)),
                    0);
            }

            if (norm > 0.50)
            {
                double t = (norm - 0.50) / 0.25;

                return Color.FromArgb(
                    alpha,
                    (byte)(255 * t),
                    255,
                    0);
            }

            if (norm > 0.25)
            {
                double t = (norm - 0.25) / 0.25;

                return Color.FromArgb(
                    alpha,
                    0,
                    255,
                    (byte)(255 * (1 - t)));
            }

            double a = norm / 0.25;

            return Color.FromArgb(
                alpha,
                0,
                (byte)(255 * a),
                255);
        }
    }
}
