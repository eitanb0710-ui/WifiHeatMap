
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Fix CS0104 ambiguous references.
using Document = QuestPDF.Fluent.Document;
using Colors = QuestPDF.Helpers.Colors;
using IContainer = QuestPDF.Infrastructure.IContainer;

namespace WifiHeatMap1
{
    public sealed class PdfSurveySample
    {
        public int Number { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Rssi { get; init; }
    }

    public sealed class PdfWall
    {
        public int Number { get; init; }
        public string Material { get; init; } = "Drywall";
        public double LossDb { get; init; }
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public double X2 { get; init; }
        public double Y2 { get; init; }
    }

    public sealed class PdfOptimization
    {
        public double? CurrentCoverage { get; init; }
        public double BestGridCoverage { get; init; }
        public double BestX { get; init; }
        public double BestY { get; init; }
        public int CandidateCount { get; init; }
        public int EvaluationPointCount { get; init; }
    }

    public sealed class PdfReportData
    {
        public string ProjectName { get; init; } = "Untitled project";
        public string FloorPlanName { get; init; } = "floorplan.png";
        public DateTime ExportedAt { get; init; }
        public double MapWidth { get; init; }
        public double MapHeight { get; init; }
        public double MapUnitsPerMeter { get; init; }
        public bool IsCalibrated { get; init; }
        public bool HasUnsavedChanges { get; init; }
        public string SurveySsid { get; init; } = "Not selected";
        public string SurveyBssid { get; init; } = "Not selected";
        public List<PdfSurveySample> Samples { get; init; } = new();
        public List<PdfWall> Walls { get; init; } = new();
        public double? RouterX { get; init; }
        public double? RouterY { get; init; }
        public double TargetRssi { get; set; } = -67;
        public PdfOptimization? Optimization { get; set; }
        public byte[] SurveyImage { get; set; } = Array.Empty<byte>();
        public byte[] PredictiveImage { get; set; } = Array.Empty<byte>();
    }

    public static class WifiPdfReportService
    {
        private const string Navy = "#172B3D";
        private const string Teal = "#137D78";
        private const string Slate = "#50667A";
        private const string Soft = "#EDF4F6";
        private const string Line = "#DCE6EB";

        public static void Generate(string path, PdfReportData data)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = true;

            string temporaryPath = path + ".tmp.pdf";

            try
            {
                Document.Create(document =>
                {
                    // ==========================================
                    // PAGE 1 - PROJECT OVERVIEW
                    // ==========================================

                    document.Page(page =>
                    {
                        SetPage(page, "OVERVIEW");

                        page.Content().Column(col =>
                        {
                            col.Spacing(13);

                            col.Item()
                                .Text("WiFi Coverage Analysis")
                                .FontSize(25)
                                .Bold()
                                .FontColor(Navy);

                            col.Item()
                                .Text("Project: " + Short(data.ProjectName, 110))
                                .FontSize(12)
                                .FontColor(Slate);

                            col.Item().Text(
                                "Generated: " +
                                data.ExportedAt.ToString(
                                    "yyyy-MM-dd HH:mm",
                                    CultureInfo.InvariantCulture));

                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Element(c =>
                                    Stat(c, "SURVEY SAMPLES",
                                        data.Samples.Count.ToString()));

                                row.ConstantItem(10);

                                row.RelativeItem().Element(c =>
                                    Stat(c, "WALL SEGMENTS",
                                        data.Walls.Count.ToString()));

                                row.ConstantItem(10);

                                row.RelativeItem().Element(c =>
                                    Stat(c, "VIRTUAL ROUTER",
                                        data.RouterX.HasValue
                                            ? "Placed"
                                            : "None"));
                            });

                            col.Item()
                                .Text("Project details")
                                .FontSize(15)
                                .Bold()
                                .FontColor(Teal);

                            col.Item().Text(
                                "Floor plan: " +
                                Short(data.FloorPlanName, 110));

                            col.Item().Text(
                                $"Image coordinates: " +
                                $"{data.MapWidth:F0} x " +
                                $"{data.MapHeight:F0} map units");

                            col.Item().Text(
                                $"Scale: {data.MapUnitsPerMeter:F2} " +
                                "map units/m " +
                                (data.IsCalibrated
                                    ? "(calibrated)"
                                    : "(uncalibrated default)"));

                            col.Item().Text(
                                "Survey SSID: " +
                                Short(data.SurveySsid, 110));

                            col.Item().Text(
                                "Survey BSSID: " +
                                Short(data.SurveyBssid, 110));

                            col.Item().Text(
                                $"Predictive coverage target: " +
                                $"{data.TargetRssi:F0} dBm");

                            col.Item()
                                .Text("How to interpret this report")
                                .FontSize(15)
                                .Bold()
                                .FontColor(Teal);

                            col.Item().Text(
                                "Measured coverage is estimated from real " +
                                "RSSI samples using inverse-distance " +
                                "weighting (IDW). Predictive coverage " +
                                "is calculated from a simplified " +
                                "distance-and-wall-loss model. " +
                                "The two workspaces are separate and " +
                                "are not fused into a single measurement.");

                            if (!data.IsCalibrated)
                            {
                                col.Item().Element(c =>
                                    Notice(c,
                                        "Warning: scale was not calibrated. " +
                                        "Physical distances and predictive " +
                                        "coverage scores may be misleading."));
                            }

                            if (data.HasUnsavedChanges)
                            {
                                col.Item().Element(c =>
                                    Notice(c,
                                        "This PDF includes the current " +
                                        "in-memory workspace, including " +
                                        "changes not yet saved to the " +
                                        ".wifimap project."));
                            }

                            col.Item().Element(c =>
                                Notice(c,
                                    "Privacy: this report may disclose " +
                                    "your floor plan, WiFi SSID/BSSID, " +
                                    "measurement positions and router " +
                                    "location. Review before sharing."));
                        });
                    });

                    // ==========================================
                    // PAGE 2 - LIVE SURVEY
                    // ==========================================

                    document.Page(page =>
                    {
                        SetPage(page, "LIVE SURVEY");

                        page.Content().Column(col =>
                        {
                            col.Spacing(10);

                            col.Item()
                                .Text("Measured WiFi Coverage")
                                .FontSize(21)
                                .Bold()
                                .FontColor(Navy);

                            col.Item()
                                .Text(
                                    "Actual RSSI sample points and the " +
                                    "IDW map (when 3 or more points exist).")
                                .FontColor(Slate);

                            col.Item()
                                .Height(330)
                                .Image(data.SurveyImage)
                                .FitArea();

                            col.Item()
                                .Text(
                                    $"Sample count: {data.Samples.Count}" +
                                    (data.Samples.Count >= 3
                                        ? " - IDW generated"
                                        : " - not enough samples for IDW"))
                                .FontColor(Teal)
                                .SemiBold();

                            if (data.Samples.Count > 0)
                            {
                                col.Item().Text(
                                    $"RSSI minimum: " +
                                    $"{data.Samples.Min(s => s.Rssi):F1} dBm" +
                                    "   |   " +
                                    $"maximum: " +
                                    $"{data.Samples.Max(s => s.Rssi):F1} dBm" +
                                    "   |   " +
                                    $"mean: " +
                                    $"{data.Samples.Average(s => s.Rssi):F1} dBm");

                                col.Item()
                                    .Text("Recorded measurements (first 10)")
                                    .FontSize(12)
                                    .Bold()
                                    .FontColor(Teal);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(40);
                                        c.RelativeColumn();
                                        c.RelativeColumn();
                                        c.RelativeColumn();
                                    });

                                    t.Header(h =>
                                    {
                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("#");

                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("X");

                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("Y");

                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("RSSI (dBm)");
                                    });

                                    foreach (PdfSurveySample s
                                             in data.Samples.Take(10))
                                    {
                                        t.Cell()
                                            .Element(TableCell)
                                            .Text(s.Number.ToString());

                                        t.Cell()
                                            .Element(TableCell)
                                            .Text(s.X.ToString("F1"));

                                        t.Cell()
                                            .Element(TableCell)
                                            .Text(s.Y.ToString("F1"));

                                        t.Cell()
                                            .Element(TableCell)
                                            .Text(s.Rssi.ToString("F1"));
                                    }
                                });
                            }
                            else
                            {
                                col.Item()
                                    .Text(
                                        "No survey measurements were " +
                                        "recorded in this project.")
                                    .FontColor(Slate);
                            }

                            if (data.Samples.Count > 10)
                            {
                                col.Item()
                                    .Text(
                                        $"{data.Samples.Count - 10} " +
                                        "additional measurements " +
                                        "are not listed here.")
                                    .FontSize(8)
                                    .FontColor(Slate);
                            }
                        });
                    });

                    // ==========================================
                    // PAGE 3 - PREDICTIVE PLAN
                    // ==========================================

                    document.Page(page =>
                    {
                        SetPage(page, "PREDICTIVE PLANNING");

                        page.Content().Column(col =>
                        {
                            col.Spacing(10);

                            col.Item()
                                .Text("Predictive WiFi Coverage")
                                .FontSize(21)
                                .Bold()
                                .FontColor(Navy);

                            col.Item()
                                .Text(
                                    "Red AP = current router; " +
                                    "green BEST = suggested grid candidate. " +
                                    "Walls use material colors.")
                                .FontColor(Slate);

                            col.Item()
                                .Height(330)
                                .Image(data.PredictiveImage)
                                .FitArea();

                            col.Item()
                                .Text(
                                    data.RouterX.HasValue
                                        ? $"Current router: " +
                                          $"({data.RouterX:F1}, " +
                                          $"{data.RouterY:F1}) map units"
                                        : "No virtual router placed; " +
                                          "the image shows the floor plan " +
                                          "and walls only.")
                                .FontColor(Teal)
                                .SemiBold();

                            col.Item()
                                .Text("Walls (first 10)")
                                .FontSize(12)
                                .Bold()
                                .FontColor(Teal);

                            if (data.Walls.Count > 0)
                            {
                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(35);
                                        c.RelativeColumn();
                                        c.ConstantColumn(65);
                                        c.RelativeColumn(2);
                                    });

                                    t.Header(h =>
                                    {
                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("#");

                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("Material");

                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("Loss");

                                        h.Cell()
                                            .Element(TableHead)
                                            .Text("Start -> End (map units)");
                                    });

                                    foreach (PdfWall w
                                             in data.Walls.Take(10))
                                    {
                                        t.Cell()
                                            .Element(TableCell)
                                            .Text(w.Number.ToString());

                                        t.Cell()
                                            .Element(TableCell)
                                            .Text(w.Material);

                                        t.Cell()
                                            .Element(TableCell)
                                            .Text($"{w.LossDb:F1} dB");

                                        t.Cell()
                                            .Element(TableCell)
                                            .Text(
                                                $"({w.X1:F0}, {w.Y1:F0})" +
                                                " -> " +
                                                $"({w.X2:F0}, {w.Y2:F0})");
                                    }
                                });
                            }
                            else
                            {
                                col.Item()
                                    .Text("No walls were drawn.")
                                    .FontColor(Slate);
                            }

                            if (data.Walls.Count > 10)
                            {
                                col.Item()
                                    .Text(
                                        $"{data.Walls.Count - 10} " +
                                        "additional wall segments " +
                                        "are not listed here.")
                                    .FontSize(8)
                                    .FontColor(Slate);
                            }
                        });
                    });

                    // ==========================================
                    // PAGE 4 - OPTIMIZATION & METHODOLOGY
                    // ==========================================

                    document.Page(page =>
                    {
                        SetPage(page, "ANALYSIS & METHODOLOGY");

                        page.Content().Column(col =>
                        {
                            col.Spacing(13);

                            col.Item()
                                .Text("Router Optimization & Methodology")
                                .FontSize(20)
                                .Bold()
                                .FontColor(Navy);

                            col.Item()
                                .Text("Optimization summary")
                                .FontSize(15)
                                .Bold()
                                .FontColor(Teal);

                            if (data.Optimization != null)
                            {
                                PdfOptimization o = data.Optimization;

                                col.Item().Row(row =>
                                {
                                    row.RelativeItem().Element(c =>
                                        Stat(c,
                                            "CURRENT COVERAGE",
                                            o.CurrentCoverage.HasValue
                                                ? $"{o.CurrentCoverage.Value:F1}%"
                                                : "N/A"));

                                    row.ConstantItem(10);

                                    row.RelativeItem().Element(c =>
                                        Stat(c,
                                            "BEST GRID CANDIDATE",
                                            $"{o.BestGridCoverage:F1}%"));
                                });

                                col.Item().Text(
                                    $"Suggested router: " +
                                    $"({o.BestX:F1}, {o.BestY:F1}) " +
                                    "map units");

                                col.Item().Text(
                                    $"Positions tested: " +
                                    $"{o.CandidateCount}; " +
                                    $"receiver points per candidate: " +
                                    $"{o.EvaluationPointCount}.");

                                if (o.CurrentCoverage.HasValue &&
                                    o.BestGridCoverage <
                                    o.CurrentCoverage.Value)
                                {
                                    col.Item().Element(c =>
                                        Notice(c,
                                            "The best searched grid point " +
                                            "scores worse than the existing " +
                                            "router. Keep the existing " +
                                            "router unless other constraints " +
                                            "justify moving it."));
                                }
                            }
                            else
                            {
                                col.Item()
                                    .Text(
                                        "Router optimization was not " +
                                        "calculated because no virtual " +
                                        "router has been placed.")
                                    .FontColor(Slate);
                            }

                            col.Item()
                                .Text("Predictive RSSI equation")
                                .FontSize(15)
                                .Bold()
                                .FontColor(Teal);

                            col.Item()
                                .Background(Soft)
                                .Padding(12)
                                .Text(
                                    "RSSI = 20 - " +
                                    "[20 * log10(max(0.5, distance_m)) + 40] " +
                                    "- sum(wall losses)")
                                .FontSize(10)
                                .FontColor(Navy);

                            col.Item().Text(
                                "The predicted RSSI is calculated " +
                                "using the router-to-point distance " +
                                "after map calibration, with attenuation " +
                                "subtracted for each crossed wall. " +
                                "Wall assumptions: glass 2 dB, " +
                                "drywall 3 dB, brick 6 dB, " +
                                "concrete 12 dB, or the entered " +
                                "custom value.");

                            col.Item()
                                .Text("Optimization objective")
                                .FontSize(15)
                                .Bold()
                                .FontColor(Teal);

                            col.Item().Text(
                                $"The program samples points across " +
                                "the entire rectangular image and counts " +
                                "what fraction have modeled RSSI >= " +
                                $"{data.TargetRssi:F0} dBm. " +
                                "The highest-scoring grid candidate " +
                                "is suggested; average RSSI breaks ties.");

                            col.Item()
                                .Text("Limitations")
                                .FontSize(15)
                                .Bold()
                                .FontColor(Teal);

                            col.Item().Text(
                                "This is a simplified mathematical " +
                                "simulation, not a certified radio survey. " +
                                "The algorithm does not recognize " +
                                "room boundaries, suitable mounting " +
                                "positions, antenna patterns, interference, " +
                                "furniture or channel congestion. " +
                                "The best grid candidate is not necessarily " +
                                "the globally optimal location.");

                            col.Item().Text(
                                "The report's optimization is recalculated " +
                                "from the captured project state. " +
                                "The green BEST marker on the predictive " +
                                "map indicates the new recommendation; " +
                                "the red AP marker indicates the " +
                                "actual virtual router.")
                                .FontSize(9)
                                .FontColor(Slate);
                        });
                    });

                }).GeneratePdf(temporaryPath);

                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        // ==========================================
        // PDF PAGE LAYOUT
        // ==========================================

        private static void SetPage(
            PageDescriptor page,
            string section)
        {
            page.Size(PageSizes.A4);
            page.Margin(34);
            page.PageColor(Colors.White);

            page.DefaultTextStyle(t =>
                t.FontFamily("Arial")
                 .FontSize(9)
                 .FontColor(Navy));

            page.Header()
                .BorderBottom(1)
                .BorderColor(Line)
                .PaddingBottom(10)
                .Row(r =>
                {
                    r.RelativeItem()
                        .Text("WIFI HEAT MAP / NETWORK ANALYSIS")
                        .Bold()
                        .FontSize(10)
                        .FontColor(Teal);

                    r.AutoItem()
                        .Text(section)
                        .FontSize(8)
                        .FontColor(Slate);
                });

            page.Footer()
                .BorderTop(1)
                .BorderColor(Line)
                .PaddingTop(8)
                .AlignRight()
                .Text(t =>
                {
                    t.Span("WiFi Heat Map  |  Page ");
                    t.CurrentPageNumber();
                });
        }

        // ==========================================
        // STATISTICS CARD
        // ==========================================

        private static void Stat(
            IContainer container,
            string label,
            string value)
        {
            container
                .Background(Soft)
                .Border(1)
                .BorderColor(Line)
                .Padding(11)
                .Column(col =>
                {
                    col.Spacing(6);

                    col.Item()
                        .Text(label)
                        .FontSize(8)
                        .FontColor(Slate);

                    col.Item()
                        .Text(value)
                        .FontSize(17)
                        .Bold()
                        .FontColor(Teal);
                });
        }

        // ==========================================
        // WARNING BOX
        // ==========================================

        private static void Notice(
            IContainer container,
            string message)
        {
            container
                .Background("#FFF1DC")
                .Border(1)
                .BorderColor("#E6CC9A")
                .Padding(10)
                .Text(message)
                .FontColor("#75501D");
        }

        // ==========================================
        // TABLE STYLES
        // ==========================================

        private static IContainer TableHead(
            IContainer container)
        {
            return container
                .Background(Soft)
                .BorderBottom(1)
                .BorderColor(Line)
                .Padding(5);
        }

        private static IContainer TableCell(
            IContainer container)
        {
            return container
                .BorderBottom(1)
                .BorderColor(Line)
                .Padding(5);
        }

        private static string Short(
            string text,
            int max)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(not provided)";
            }

            return text.Length <= max
                ? text
                : text.Substring(0, max - 3) + "...";
        }
    }
}
