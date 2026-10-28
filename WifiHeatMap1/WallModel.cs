using System;
using System.Windows;

namespace WifiHeatMap1
{
    public enum WallMaterial
    {
        Drywall,    // -3 dB
        Glass,      // -2 dB
        Brick,      // -6 dB
        Concrete,   // -12 dB
        Custom      // User-defined attenuation
    }

    public class WallSegment
    {
        public Point Start { get; set; }
        public Point End { get; set; }
        public WallMaterial Material { get; set; }
        public double CustomAttenuationDb { get; set; } = 3.0;

        public double AttenuationDb => Material switch
        {
            WallMaterial.Glass => 2.0,
            WallMaterial.Drywall => 3.0,
            WallMaterial.Brick => 6.0,
            WallMaterial.Concrete => 12.0,
            WallMaterial.Custom => CustomAttenuationDb,
            _ => 3.0
        };

        public bool IntersectsLine(Point p1, Point p2)
        {
            return FastLineIntersection(p1, p2, Start, End);
        }

        private static bool FastLineIntersection(Point p1, Point p2, Point p3, Point p4)
        {
            double d = (p2.X - p1.X) * (p4.Y - p3.Y) - (p2.Y - p1.Y) * (p4.X - p3.X);
            if (Math.Abs(d) < 1e-9) return false;

            double u = ((p3.X - p1.X) * (p4.Y - p3.Y) - (p3.Y - p1.Y) * (p4.X - p3.X)) / d;
            double v = ((p3.X - p1.X) * (p2.Y - p1.Y) - (p3.Y - p1.Y) * (p2.X - p1.X)) / d;

            return (u >= 0 && u <= 1 && v >= 0 && v <= 1);
        }
    }

    public class SamplePoint
    {
        public Point Position { get; set; }
        public double Rssi { get; set; }
    }
}