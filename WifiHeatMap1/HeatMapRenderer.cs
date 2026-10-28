using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WifiHeatMap1
{
    public static class HeatMapRenderer
    {
        // 1. Empirical Mode: IDW Interpolation with Distance Falloff & Calibrated Scale
        public static WriteableBitmap RenderIdwHeatMap(int width, int height, List<SamplePoint> samples, double pixelsPerMeter = 35.0, int step = 4)
        {
            WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            if (samples.Count == 0) return bitmap;

            byte[] pixelData = new byte[width * height * 4];
            double maxSamplingRadius = pixelsPerMeter * 12.0; // Max radius of ~12 meters

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    double totalWeight = 0;
                    double weightedRssiSum = 0;
                    double minDistance = double.MaxValue;

                    foreach (var sample in samples)
                    {
                        double dist = Math.Sqrt(Math.Pow(x - sample.Position.X, 2) + Math.Pow(y - sample.Position.Y, 2));
                        if (dist < minDistance) minDistance = dist;

                        double d = Math.Max(1.0, dist);
                        double weight = 1.0 / (d * d);

                        totalWeight += weight;
                        weightedRssiSum += weight * sample.Rssi;
                    }

                    if (minDistance > maxSamplingRadius) continue;

                    double baseRssi = weightedRssiSum / totalWeight;
                    double distanceMeters = minDistance / pixelsPerMeter;
                    double finalRssi = baseRssi - (distanceMeters * 3.0);

                    Color c = RssiToColor(finalRssi);

                    for (int dy = 0; dy < step && (y + dy) < height; dy++)
                    {
                        for (int dx = 0; dx < step && (x + dx) < width; dx++)
                        {
                            int index = ((y + dy) * width + (x + dx)) * 4;
                            pixelData[index] = c.B;
                            pixelData[index + 1] = c.G;
                            pixelData[index + 2] = c.R;
                            pixelData[index + 3] = c.A;
                        }
                    }
                }
            }

            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixelData, width * 4, 0);
            return bitmap;
        }

        // 2. Predictive Mode: Virtual AP + Calibrated Wall Raycasting
        public static WriteableBitmap RenderPredictiveHeatMap(int width, int height, System.Windows.Point apPos, List<WallSegment> walls, double pixelsPerMeter = 35.0, double txPowerDbms = 20, int step = 4)
        {
            WriteableBitmap bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixelData = new byte[width * height * 4];

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    System.Windows.Point currentPx = new System.Windows.Point(x, y);
                    double distPixels = Math.Sqrt(Math.Pow(x - apPos.X, 2) + Math.Pow(y - apPos.Y, 2));
                    double distMeters = Math.Max(0.5, distPixels / pixelsPerMeter);

                    double pathLoss = 20 * Math.Log10(distMeters) + 40;

                    double wallLoss = 0;
                    foreach (var wall in walls)
                    {
                        if (wall.IntersectsLine(apPos, currentPx))
                        {
                            wallLoss += wall.AttenuationDb;
                        }
                    }

                    double calculatedRssi = txPowerDbms - pathLoss - wallLoss;
                    Color c = RssiToColor(calculatedRssi);

                    for (int dy = 0; dy < step && (y + dy) < height; dy++)
                    {
                        for (int dx = 0; dx < step && (x + dx) < width; dx++)
                        {
                            int index = ((y + dy) * width + (x + dx)) * 4;
                            pixelData[index] = c.B;
                            pixelData[index + 1] = c.G;
                            pixelData[index + 2] = c.R;
                            pixelData[index + 3] = c.A;
                        }
                    }
                }
            }

            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixelData, width * 4, 0);
            return bitmap;
        }

        // 3. Thermal Color Gradient
        public static Color RssiToColor(double rssi)
        {
            if (rssi < -88.0) return Colors.Transparent;

            double norm = (rssi - (-88.0)) / (-30.0 - (-88.0));
            norm = Math.Max(0.0, Math.Min(1.0, norm));

            byte alpha = 160;

            if (norm > 0.75)
            {
                double t = (norm - 0.75) / 0.25;
                return Color.FromArgb(alpha, 255, (byte)(255 * (1 - t)), 0);
            }
            else if (norm > 0.50)
            {
                double t = (norm - 0.50) / 0.25;
                return Color.FromArgb(alpha, (byte)(255 * t), 255, 0);
            }
            else if (norm > 0.25)
            {
                double t = (norm - 0.25) / 0.25;
                return Color.FromArgb(alpha, 0, 255, (byte)(255 * (1 - t)));
            }
            else
            {
                double t = norm / 0.25;
                return Color.FromArgb(alpha, 0, (byte)(255 * t), 255);
            }
        }
    }
}