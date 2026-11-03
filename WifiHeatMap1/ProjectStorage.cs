
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace WifiHeatMap1
{
    public sealed class MapPointData
    {
        public double X { get; set; }
        public double Y { get; set; }

        public static MapPointData FromPoint(Point p) =>
            new() { X = p.X, Y = p.Y };

        public Point ToPoint() => new(X, Y);
    }

    public sealed class SurveySampleData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Rssi { get; set; }
    }

    public sealed class WallData
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public string Material { get; set; } = "Drywall";
        public double CustomAttenuationDb { get; set; } = 3.0;
    }

    public sealed class WifiMapProject
    {
        public string Format { get; set; } = "WiFiHeatMapProject";
        public int Version { get; set; } = 1;
        public string FloorPlanFileName { get; set; } = "floorplan.png";
        public string FloorPlanBase64 { get; set; } = "";
        public double MapUnitsPerMeter { get; set; } = 35.0;
        public bool ScaleCalibrated { get; set; }
        public string? SurveyBssid { get; set; }
        public string? SurveySsid { get; set; }
        public List<SurveySampleData> Samples { get; set; } = new();
        public List<WallData> Walls { get; set; } = new();
        public MapPointData? RouterPosition { get; set; }
        public bool ShowPredictiveWorkspace { get; set; }
    }

    public sealed class LoadedWifiMapProject
    {
        public WifiMapProject Project { get; init; } = new();
        public byte[] FloorPlanBytes { get; init; } = Array.Empty<byte>();
    }

    public static class ProjectStorage
    {
        private const int MaxImageBytes = 25 * 1024 * 1024;
        private const long MaxProjectBytes = 40L * 1024 * 1024;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static void Save(
            string path,
            WifiMapProject project,
            byte[] imageBytes)
        {
            if (imageBytes.Length == 0 ||
                imageBytes.Length > MaxImageBytes)
            {
                throw new InvalidDataException(
                    "Floor plan image must be at most 25 MB.");
            }

            project.Format = "WiFiHeatMapProject";
            project.Version = 1;
            project.FloorPlanBase64 =
                Convert.ToBase64String(imageBytes);

            string json = JsonSerializer.Serialize(project, Options);
            string temp = path + ".tmp";

            try
            {
                File.WriteAllText(temp, json);
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);

                project.FloorPlanBase64 = "";
            }
        }

        public static LoadedWifiMapProject Load(string path)
        {
            if (new FileInfo(path).Length > MaxProjectBytes)
            {
                throw new InvalidDataException(
                    "Project file exceeds the 40 MB safety limit.");
            }

            string json = File.ReadAllText(path);

            WifiMapProject data =
                JsonSerializer.Deserialize<WifiMapProject>(
                    json, Options)
                ?? throw new InvalidDataException(
                    "Project is empty or invalid.");

            if (data.Format != "WiFiHeatMapProject" ||
                data.Version != 1)
            {
                throw new InvalidDataException(
                    "Unsupported project format or version.");
            }

            if (data.FloorPlanBase64.Length > 36_000_000)
            {
                throw new InvalidDataException(
                    "Embedded floor plan is too large.");
            }

            byte[] imageBytes;

            try
            {
                imageBytes = Convert.FromBase64String(
                    data.FloorPlanBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(
                    "The embedded image is damaged.", ex);
            }

            if (imageBytes.Length == 0 ||
                imageBytes.Length > MaxImageBytes)
            {
                throw new InvalidDataException(
                    "Invalid embedded image size.");
            }

            if (!double.IsFinite(data.MapUnitsPerMeter) ||
                data.MapUnitsPerMeter <= 0 ||
                data.MapUnitsPerMeter > 100000)
            {
                throw new InvalidDataException(
                    "Invalid project scale.");
            }

            if (data.Samples == null ||
                data.Walls == null ||
                data.Samples.Count > 2000 ||
                data.Walls.Count > 5000)
            {
                throw new InvalidDataException(
                    "Project contains too many map objects.");
            }

            if (data.Samples.Count > 0 &&
                string.IsNullOrWhiteSpace(data.SurveyBssid))
            {
                throw new InvalidDataException(
                    "Survey samples have no BSSID.");
            }

            foreach (SurveySampleData s in data.Samples)
            {
                if (!ValidCoordinate(s.X, s.Y) ||
                    !double.IsFinite(s.Rssi) ||
                    s.Rssi >= 0 || s.Rssi < -120)
                {
                    throw new InvalidDataException(
                        "Invalid survey point.");
                }
            }

            foreach (WallData w in data.Walls)
            {
                if (!ValidCoordinate(w.X1, w.Y1) ||
                    !ValidCoordinate(w.X2, w.Y2) ||
                    !Enum.TryParse<WallMaterial>(
                        w.Material, false, out _) ||
                    !double.IsFinite(w.CustomAttenuationDb) ||
                    w.CustomAttenuationDb < 0 ||
                    w.CustomAttenuationDb > 100)
                {
                    throw new InvalidDataException(
                        "Invalid wall in project.");
                }
            }

            if (data.RouterPosition != null &&
                !ValidCoordinate(
                    data.RouterPosition.X,
                    data.RouterPosition.Y))
            {
                throw new InvalidDataException(
                    "Invalid router position.");
            }

            data.FloorPlanBase64 = "";

            return new LoadedWifiMapProject
            {
                Project = data,
                FloorPlanBytes = imageBytes
            };
        }

        private static bool ValidCoordinate(double x, double y) =>
            double.IsFinite(x) &&
            double.IsFinite(y) &&
            x >= 0 && y >= 0 &&
            x <= 100000 && y <= 100000;
    }
}
