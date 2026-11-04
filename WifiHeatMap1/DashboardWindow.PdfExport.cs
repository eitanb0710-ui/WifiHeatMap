
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace WifiHeatMap1
{
    // This is another part of the EXISTING DashboardWindow class.
    // The original DashboardWindow.xaml.cs is not modified.
    public partial class DashboardWindow
    {
        private bool _exportInProgress;

        private async void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_exportInProgress)
                return;

            if (_floorPlan == null)
            {
                MessageBox.Show(
                    "Load a floor plan before exporting a PDF report.",
                    "No Floor Plan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dialog = new()
            {
                Title = "Export WiFi Coverage PDF Report",
                Filter = "PDF Report (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = _projectPath == null
                    ? "WiFi_Heat_Map_Report.pdf"
                    : Path.GetFileNameWithoutExtension(_projectPath) + "_Report.pdf"
            };

            if (dialog.ShowDialog() != true)
                return;

            _exportInProgress = true;
            BtnExportPdf.IsEnabled = false;
            TxtStatus.Text = "Preparing PDF report...";

            try
            {
                // Capture a consistent copy of the current project state.
                // The WPF bitmap operations below must run on the UI thread.
                List<SamplePoint> samples = _samples.Select(s => new SamplePoint
                {
                    Position = s.Position,
                    Rssi = s.Rssi
                }).ToList();

                List<WallSegment> walls = _walls.Select(w => new WallSegment
                {
                    Start = w.Start,
                    End = w.End,
                    Material = w.Material,
                    CustomAttenuationDb = w.CustomAttenuationDb
                }).ToList();

                Point? router = _routerPosition;
                double scale = _mapUnitsPerMeter;
                double width = _floorPlan.Width;
                double height = _floorPlan.Height;

                PdfReportData report = new()
                {
                    ProjectName = _projectPath == null
                        ? "Untitled project"
                        : Path.GetFileNameWithoutExtension(_projectPath),
                    FloorPlanName = _floorPlanFileName,
                    ExportedAt = DateTime.Now,
                    MapWidth = width,
                    MapHeight = height,
                    MapUnitsPerMeter = scale,
                    IsCalibrated = _scaleCalibrated,
                    HasUnsavedChanges = _isDirty,
                    SurveySsid = _surveySsid ?? "Not selected",
                    SurveyBssid = _surveyBssid ?? "Not selected",
                    Samples = samples.Select((s, i) => new PdfSurveySample
                    {
                        Number = i + 1,
                        X = s.Position.X,
                        Y = s.Position.Y,
                        Rssi = s.Rssi
                    }).ToList(),
                    Walls = walls.Select((w, i) => new PdfWall
                    {
                        Number = i + 1,
                        Material = w.Material.ToString(),
                        LossDb = w.AttenuationDb,
                        X1 = w.Start.X,
                        Y1 = w.Start.Y,
                        X2 = w.End.X,
                        Y2 = w.End.Y
                    }).ToList(),
                    RouterX = router?.X,
                    RouterY = router?.Y
                };

                // Read the target from WPF before leaving the UI thread.
                string thresholdText = TxtCoverageThreshold.Text.Trim()
                    .Replace(',', '.');

                if (!double.TryParse(thresholdText, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double threshold) ||
                    !double.IsFinite(threshold) ||
                    threshold < -100 || threshold > -30)
                {
                    threshold = -67;
                }

                report.TargetRssi = threshold;

                // Recalculate comparable scores from this exact snapshot.
                if (router.HasValue)
                {
                    TxtStatus.Text = "Evaluating router coverage for report...";

                    RouterOptimizationResult result = await Task.Run(() =>
                        RouterOptimizer.FindBest(width, height, walls,
                            scale, threshold, router));

                    report.Optimization = new PdfOptimization
                    {
                        CurrentCoverage = result.PreviousCoveragePercent,
                        BestGridCoverage = result.BestCoveragePercent,
                        BestX = result.BestPosition.X,
                        BestY = result.BestPosition.Y,
                        CandidateCount = result.CandidateCount,
                        EvaluationPointCount = result.EvaluationPointCount
                    };
                }

                // WPF drawings must be created on the UI thread.
                TxtStatus.Text = "Capturing both map views for PDF...";
                report.SurveyImage = MapReportRenderer.Create(
                    _floorPlan, samples, walls, router, scale, true);

                Point? best = report.Optimization == null
                    ? null
                    : new Point(report.Optimization.BestX,
                                report.Optimization.BestY);

                report.PredictiveImage = MapReportRenderer.Create(
                    _floorPlan, samples, walls, router, scale, false, best);

                TxtStatus.Text = "Writing PDF pages...";
                await Task.Run(() =>
                    WifiPdfReportService.Generate(dialog.FileName, report));

                TxtStatus.Text = "PDF report exported successfully.";

                MessageBoxResult open = MessageBox.Show(
                    "PDF report created:\n" + dialog.FileName +
                    "\n\nOpen the PDF now?",
                    "Export Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (open == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dialog.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "PDF export failed: " + ex.Message;
                MessageBox.Show(
                    "The PDF could not be exported:\n" + ex.Message,
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _exportInProgress = false;
                BtnExportPdf.IsEnabled = true;
            }
        }
    }
}
