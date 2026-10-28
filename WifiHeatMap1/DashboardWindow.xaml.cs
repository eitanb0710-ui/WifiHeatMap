
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace WifiHeatMap1
{
    public partial class DashboardWindow : Window
    {
        // Both workflows share one floor plan and one scale.
        private readonly WifiScannerService _wifiScanner;
        private BitmapImage? _floorPlan;
        private double _mapUnitsPerMeter = 35.0;

        // LIVE SURVEY: measured samples and selected access point.
        private readonly List<SamplePoint> _samples = new();
        private string? _surveyBssid;

        // PREDICTIVE PLAN: simulated router and wall geometry.
        private readonly List<WallSegment> _walls = new();
        private Point? _routerPosition;
        private UIElement? _routerMarker;

        // Shared calibration/drawing interaction.
        private bool _isCalibrating;
        private bool _isDrawing;
        private Point _dragStart;
        private Canvas? _drawingCanvas;
        private Line? _previewLine;
        private Canvas? _calibrationCanvas;
        private Line? _calibrationLine;
        private double _calibrationLineLength;

        private UserManualWindow? _userManualWindow;

        private bool IsSurveyActive =>
            ModeTabs.SelectedItem == TabSurvey;

        private bool HasFloorPlan => _floorPlan != null;

        public DashboardWindow()
        {
            InitializeComponent();
            _wifiScanner = new WifiScannerService();
            UpdateCounters();
        }

        // =====================================================
        // USER MANUAL
        // =====================================================

        private void BtnUserManual_Click(
            object sender, RoutedEventArgs e)
        {
            if (_userManualWindow != null &&
                _userManualWindow.IsVisible)
            {
                if (_userManualWindow.WindowState ==
                    WindowState.Minimized)
                {
                    _userManualWindow.WindowState =
                        WindowState.Normal;
                }

                _userManualWindow.Activate();
                return;
            }

            _userManualWindow = new UserManualWindow
            {
                Owner = this
            };

            _userManualWindow.Closed +=
                (s, args) => _userManualWindow = null;

            _userManualWindow.Show();
        }

        // =====================================================
        // FLOOR PLAN: SHARED BY BOTH WORKFLOWS
        // =====================================================

        private void BtnLoadMap_Click(
            object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Choose a floor plan",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp"
            };

            if (dialog.ShowDialog() != true)
                return;

            if (_samples.Count > 0 ||
                _walls.Count > 0 ||
                _routerPosition.HasValue)
            {
                MessageBoxResult answer = MessageBox.Show(
                    "Loading another floor plan will erase both " +
                    "workflows' measurements, walls, and router " +
                    "position. Continue?",
                    "Replace Floor Plan",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                BitmapImage image = new();

                image.BeginInit();
                image.UriSource = new Uri(dialog.FileName);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();

                // Load successfully before discarding old data.
                ResetBothWorkflows();

                _floorPlan = image;
                _mapUnitsPerMeter = 35.0;

                // All positions and renderer dimensions use
                // the same WPF coordinate system.
                double width = image.Width;
                double height = image.Height;

                CanvasContainer.Width = width;
                CanvasContainer.Height = height;

                ImgFloorPlan.Width = width;
                ImgFloorPlan.Height = height;

                ImgHeatMapOverlay.Width = width;
                ImgHeatMapOverlay.Height = height;

                SurveyCanvas.Width = width;
                SurveyCanvas.Height = height;

                PlanningCanvas.Width = width;
                PlanningCanvas.Height = height;

                ImgFloorPlan.Source = image;

                TxtPlaceholder.Visibility =
                    Visibility.Collapsed;

                TxtScaleDisplay.Text =
                    "Scale: 35.0 map units/m (default)";

                RefreshCurrentOverlay(false);

                TxtStatus.Text =
                    "Floor plan loaded. Select a workflow to begin.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not open the floor plan:\n" +
                    ex.Message,
                    "Image Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // =====================================================
        // TWO WORKFLOWS: SWITCH WITHOUT MIXING DATA
        // =====================================================

        private void ModeTabs_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            // Ignore events bubbling up from combo boxes.
            if (!ReferenceEquals(
                    e.OriginalSource, ModeTabs) ||
                SurveyCanvas == null ||
                PlanningCanvas == null ||
                ImgHeatMapOverlay == null ||
                TxtWorkspaceTitle == null)
            {
                return;
            }

            CancelCalibration();
            AbortDrawing();

            bool survey = IsSurveyActive;

            SurveyCanvas.Visibility = survey
                ? Visibility.Visible
                : Visibility.Collapsed;

            PlanningCanvas.Visibility = survey
                ? Visibility.Collapsed
                : Visibility.Visible;

            TxtWorkspaceTitle.Text = survey
                ? "Live Wi-Fi Survey"
                : "Predictive Wi-Fi Planning";

            TxtWorkspaceSubtitle.Text = survey
                ? "Actual measurements and IDW interpolation"
                : "Virtual router placement and wall-aware simulation";

            TxtModeBadge.Text = survey
                ? "MEASURED"
                : "SIMULATED";

            TxtMapHint.Text = survey
                ? "Stand at a real location, then click the same " +
                  "position on the plan."
                : "Click to place a router, or select Draw Walls " +
                  "and drag a wall segment.";

            RefreshCurrentOverlay(false);

            TxtStatus.Text = survey
                ? "Live Survey selected. Only real measurement " +
                  "markers are shown."
                : "Predictive Plan selected. Only the virtual " +
                  "router and walls are shown.";
        }

        private void PlanTool_Changed(
            object sender, RoutedEventArgs e)
        {
            if (PanelWallMaterial == null ||
                RadioDrawWalls == null ||
                TxtMapHint == null)
            {
                return;
            }

            bool walls = RadioDrawWalls.IsChecked == true;

            PanelWallMaterial.Visibility = walls
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (ModeTabs != null &&
                !IsSurveyActive &&
                !_isCalibrating)
            {
                TxtMapHint.Text = walls
                    ? "Drag from one end of a wall to the other."
                    : "Click where you want the virtual router " +
                      "to be placed.";
            }
        }

        private void CmbWallMaterial_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (PanelCustomWallLoss == null ||
                CmbWallMaterial == null)
            {
                return;
            }

            bool custom =
                CmbWallMaterial.SelectedItem
                    is ComboBoxItem item &&
                item.Tag?.ToString() == "Custom";

            PanelCustomWallLoss.Visibility = custom
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // =====================================================
        // LIVE SURVEY: BSSID SELECTOR
        // =====================================================

        private void BtnRefreshNetworks_Click(
            object sender, RoutedEventArgs e)
        {
            string? prior =
                (CmbSelectedNetwork.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString();

            CmbSelectedNetwork.Items.Clear();

            try
            {
                var accessPoints =
                    _wifiScanner.ScanNetworks();

                HashSet<string> uniqueBssids =
                    new(StringComparer.OrdinalIgnoreCase);

                ComboBoxItem? preferred = null;

                foreach (var ap in accessPoints)
                {
                    if (string.IsNullOrWhiteSpace(ap.Bssid) ||
                        !uniqueBssids.Add(ap.Bssid))
                    {
                        continue;
                    }

                    string ssid =
                        string.IsNullOrWhiteSpace(ap.Ssid)
                        ? "[Hidden network]"
                        : ap.Ssid;

                    ComboBoxItem option = new()
                    {
                        Content =
                            $"{ssid}  |  {ap.Rssi} dBm  |  " +
                            $"{ap.Bssid}",

                        Tag = ap.Bssid,

                        ToolTip =
                            $"SSID: {ssid}\n" +
                            $"BSSID: {ap.Bssid}\n" +
                            $"RSSI: {ap.Rssi} dBm"
                    };

                    CmbSelectedNetwork.Items.Add(option);

                    if (string.Equals(
                        ap.Bssid,
                        prior ?? _surveyBssid,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        preferred = option;
                    }
                }

                if (preferred != null)
                {
                    CmbSelectedNetwork.SelectedItem =
                        preferred;
                }

                TxtStatus.Text =
                    CmbSelectedNetwork.Items.Count > 0
                    ? $"Found {CmbSelectedNetwork.Items.Count} " +
                      "access points. Select one."
                    : "No access points found. Check Wi-Fi " +
                      "and Windows location permissions.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text =
                    "Wi-Fi scanning failed: " + ex.Message;
            }
        }

        private void SurveyCanvas_MouseLeftButtonDown(
            object sender, MouseButtonEventArgs e)
        {
            if (!HasFloorPlan ||
                ModalScalePrompt.Visibility ==
                    Visibility.Visible)
            {
                return;
            }

            Point point = e.GetPosition(SurveyCanvas);

            if (_isCalibrating)
            {
                BeginLine(SurveyCanvas, point, true);
                return;
            }

            RecordSurveyPoint(point);
        }

        private void RecordSurveyPoint(Point position)
        {
            if (CmbSelectedNetwork.SelectedItem
                    is not ComboBoxItem selection ||
                selection.Tag is not string bssid ||
                string.IsNullOrWhiteSpace(bssid))
            {
                MessageBox.Show(
                    "First select a Wi-Fi access point " +
                    "from the list.",
                    "Select Access Point",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (_samples.Count > 0 &&
                !string.Equals(
                    _surveyBssid,
                    bssid,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "This survey already contains measurements " +
                    "from another BSSID. Clear Measurements " +
                    "before switching access points.",
                    "Different Access Point",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                AccessPointInfo? ap =
                    _wifiScanner.ScanNetworks()
                        .FirstOrDefault(n =>
                            string.Equals(
                                n.Bssid,
                                bssid,
                                StringComparison.OrdinalIgnoreCase));

                if (ap == null)
                {
                    TxtStatus.Text =
                        "Selected access point is not " +
                        "currently detected.";

                    MessageBox.Show(
                        "Try refreshing the access point list.",
                        "Access Point Unavailable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                if (ap.Rssi >= 0 || ap.Rssi < -120)
                {
                    TxtStatus.Text =
                        "Invalid RSSI reading; " +
                        "no sample recorded.";

                    return;
                }

                _samples.Add(new SamplePoint
                {
                    Position = position,
                    Rssi = ap.Rssi
                });

                _surveyBssid = bssid;

                Grid marker =
                    CreateSurveyMarker(_samples.Count, ap.Rssi);

                marker.ToolTip =
                    $"Sample {_samples.Count}\n" +
                    $"SSID: {ap.Ssid}\n" +
                    $"BSSID: {ap.Bssid}\n" +
                    $"RSSI: {ap.Rssi} dBm";

                Canvas.SetLeft(marker, position.X - 12);
                Canvas.SetTop(marker, position.Y - 12);

                SurveyCanvas.Children.Add(marker);

                UpdateCounters();
                RefreshCurrentOverlay(false);

                TxtStatus.Text =
                    _samples.Count < 3
                    ? $"Recorded {ap.Rssi} dBm. Add " +
                      $"{3 - _samples.Count} more point(s) " +
                      "for IDW."
                    : $"Recorded {ap.Rssi} dBm. IDW map " +
                      $"updated ({_samples.Count} points).";
            }
            catch (Exception ex)
            {
                TxtStatus.Text =
                    "Measurement failed: " + ex.Message;
            }
        }

        private void BtnGenerateSurvey_Click(
            object sender, RoutedEventArgs e)
        {
            RefreshCurrentOverlay(true);
        }

        private void BtnClearSurvey_Click(
            object sender, RoutedEventArgs e)
        {
            if (_samples.Count == 0)
                return;

            if (MessageBox.Show(
                    "Delete all survey measurements?",
                    "Clear Survey",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            CancelCalibration();

            SurveyCanvas.Children.Clear();

            _samples.Clear();
            _surveyBssid = null;

            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                "Survey cleared. Router and walls were kept.";
        }

        // =====================================================
        // PREDICTIVE PLAN: ROUTER AND WALLS
        // =====================================================

        private void PlanningCanvas_MouseLeftButtonDown(
            object sender, MouseButtonEventArgs e)
        {
            if (!HasFloorPlan ||
                ModalScalePrompt.Visibility ==
                    Visibility.Visible)
            {
                return;
            }

            Point point = e.GetPosition(PlanningCanvas);

            if (_isCalibrating)
            {
                BeginLine(PlanningCanvas, point, true);
            }
            else if (RadioDrawWalls.IsChecked == true)
            {
                BeginLine(PlanningCanvas, point, false);
            }
            else
            {
                PlaceRouter(point);
            }
        }

        private void PlaceRouter(Point point)
        {
            _routerPosition = point;

            if (_routerMarker != null)
            {
                PlanningCanvas.Children.Remove(
                    _routerMarker);
            }

            _routerMarker = CreateRouterMarker();

            Canvas.SetLeft(_routerMarker, point.X - 18);
            Canvas.SetTop(_routerMarker, point.Y - 18);

            PlanningCanvas.Children.Add(_routerMarker);

            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                "Virtual router placed. Prediction updated.";
        }

        private void BtnGeneratePrediction_Click(
            object sender, RoutedEventArgs e)
        {
            RefreshCurrentOverlay(true);
        }

        private void BtnClearPlanning_Click(
            object sender, RoutedEventArgs e)
        {
            if (_walls.Count == 0 &&
                !_routerPosition.HasValue)
            {
                return;
            }

            if (MessageBox.Show(
                    "Delete the virtual router and all " +
                    "drawn walls?",
                    "Clear Prediction",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            CancelCalibration();
            AbortDrawing();

            PlanningCanvas.Children.Clear();

            _walls.Clear();
            _routerPosition = null;
            _routerMarker = null;

            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                "Predictive plan cleared. Survey " +
                "measurements were kept.";
        }

        // =====================================================
        // DRAW WALLS OR A CALIBRATION REFERENCE LINE
        // =====================================================

        private void BeginLine(
            Canvas canvas, Point start, bool calibration)
        {
            AbortDrawing();

            _isDrawing = true;
            _drawingCanvas = canvas;
            _dragStart = start;

            _previewLine = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = start.X,
                Y2 = start.Y,

                Stroke = calibration
                    ? Brushes.Magenta
                    : GetWallColor(),

                StrokeThickness = calibration ? 3 : 4,
                IsHitTestVisible = false
            };

            if (calibration)
            {
                _previewLine.StrokeDashArray =
                    new DoubleCollection { 5, 3 };
            }

            canvas.Children.Add(_previewLine);
            canvas.CaptureMouse();
        }

        private void MapCanvas_MouseMove(
            object sender, MouseEventArgs e)
        {
            if (!_isDrawing ||
                _drawingCanvas == null ||
                _previewLine == null ||
                !ReferenceEquals(sender, _drawingCanvas))
            {
                return;
            }

            Point end = ClampToCanvas(
                e.GetPosition(_drawingCanvas),
                _drawingCanvas);

            _previewLine.X2 = end.X;
            _previewLine.Y2 = end.Y;
        }

        private void MapCanvas_MouseLeftButtonUp(
            object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing ||
                _drawingCanvas == null ||
                _previewLine == null ||
                !ReferenceEquals(sender, _drawingCanvas))
            {
                return;
            }

            Canvas canvas = _drawingCanvas;
            Line line = _previewLine;

            Point end = ClampToCanvas(
                e.GetPosition(canvas), canvas);

            line.X2 = end.X;
            line.Y2 = end.Y;

            _isDrawing = false;
            _drawingCanvas = null;
            _previewLine = null;

            if (canvas.IsMouseCaptured)
                canvas.ReleaseMouseCapture();

            double distance = Math.Sqrt(
                Math.Pow(end.X - _dragStart.X, 2) +
                Math.Pow(end.Y - _dragStart.Y, 2));

            if (distance <= 5)
            {
                canvas.Children.Remove(line);

                TxtStatus.Text =
                    "Line too short. Click and drag farther.";

                return;
            }

            // CALIBRATION REFERENCE LINE
            if (_isCalibrating)
            {
                _calibrationCanvas = canvas;
                _calibrationLine = line;
                _calibrationLineLength = distance;

                ModalScalePrompt.Visibility =
                    Visibility.Visible;

                TxtMetersInput.Focus();
                TxtMetersInput.SelectAll();

                return;
            }

            // WALL DRAWING ONLY IN PREDICTIVE WORKSPACE
            if (!ReferenceEquals(canvas, PlanningCanvas) ||
                RadioDrawWalls.IsChecked != true)
            {
                canvas.Children.Remove(line);
                return;
            }

            WallMaterial material = GetWallMaterial();
            double customLoss = 3.0;

            if (material == WallMaterial.Custom)
            {
                string raw =
                    TxtCustomDb.Text.Trim().Replace(',', '.');

                if (!double.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out customLoss) ||
                    double.IsNaN(customLoss) ||
                    double.IsInfinity(customLoss) ||
                    customLoss < 0)
                {
                    canvas.Children.Remove(line);

                    MessageBox.Show(
                        "Enter a valid non-negative wall " +
                        "loss, e.g. 8.5 dB.",
                        "Invalid Wall Attenuation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }
            }

            _walls.Add(new WallSegment
            {
                Start = _dragStart,
                End = end,
                Material = material,
                CustomAttenuationDb = customLoss
            });

            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                $"Added {material} wall. " +
                $"{_walls.Count} walls in this plan.";
        }

        private static Point ClampToCanvas(
            Point p, Canvas canvas)
        {
            return new Point(
                Math.Max(0, Math.Min(canvas.Width, p.X)),
                Math.Max(0, Math.Min(canvas.Height, p.Y)));
        }

        private void AbortDrawing()
        {
            if (_drawingCanvas != null)
            {
                if (_previewLine != null)
                {
                    _drawingCanvas.Children.Remove(
                        _previewLine);
                }

                if (_drawingCanvas.IsMouseCaptured)
                {
                    _drawingCanvas.ReleaseMouseCapture();
                }
            }

            _isDrawing = false;
            _previewLine = null;
            _drawingCanvas = null;
        }

        private WallMaterial GetWallMaterial()
        {
            if (CmbWallMaterial.SelectedItem
                    is ComboBoxItem item &&
                Enum.TryParse(
                    item.Tag?.ToString(),
                    out WallMaterial material))
            {
                return material;
            }

            return WallMaterial.Drywall;
        }

        private Brush GetWallColor()
        {
            return GetWallMaterial() switch
            {
                WallMaterial.Glass => Brushes.LightBlue,
                WallMaterial.Brick => Brushes.Orange,
                WallMaterial.Concrete => Brushes.Red,
                WallMaterial.Custom => Brushes.MediumPurple,
                _ => Brushes.Gold
            };
        }

        // =====================================================
        // SHARED DISTANCE CALIBRATION
        // =====================================================

        private void BtnCalibrate_Click(
            object sender, RoutedEventArgs e)
        {
            if (!HasFloorPlan)
            {
                MessageBox.Show(
                    "Load a floor plan before calibrating " +
                    "distance.",
                    "No Floor Plan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (_isCalibrating)
            {
                CancelCalibration();

                TxtStatus.Text =
                    "Calibration canceled; " +
                    "the previous scale remains.";

                return;
            }

            AbortDrawing();

            _isCalibrating = true;
            BtnCalibrate.Content = "Cancel Calibration";

            TxtStatus.Text =
                "Draw a line across a known " +
                "real-world distance.";

            TxtMapHint.Text =
                "CALIBRATION: Click and drag along " +
                "a known distance.";
        }

        private void BtnApplyScale_Click(
            object sender, RoutedEventArgs e)
        {
            string raw =
                TxtMetersInput.Text.Trim().Replace(',', '.');

            if (!double.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double meters) ||
                double.IsNaN(meters) ||
                double.IsInfinity(meters) ||
                meters <= 0)
            {
                MessageBox.Show(
                    "Enter a positive distance in " +
                    "meters (e.g. 2.5).",
                    "Invalid Distance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _mapUnitsPerMeter =
                _calibrationLineLength / meters;

            TxtScaleDisplay.Text =
                $"Scale: {_mapUnitsPerMeter:F1} " +
                $"map units/m ({meters:g} m reference)";

            CancelCalibration();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                $"Scale updated to {_mapUnitsPerMeter:F1} " +
                "units/m. Both workflows now use " +
                "this calibration.";
        }

        private void BtnCancelScale_Click(
            object sender, RoutedEventArgs e)
        {
            CancelCalibration();

            TxtStatus.Text =
                "Calibration canceled. Existing scale kept.";
        }

        private void CancelCalibration()
        {
            if (_isDrawing)
                AbortDrawing();

            if (_calibrationCanvas != null &&
                _calibrationLine != null)
            {
                _calibrationCanvas.Children.Remove(
                    _calibrationLine);
            }

            _calibrationLine = null;
            _calibrationCanvas = null;
            _calibrationLineLength = 0;
            _isCalibrating = false;

            if (ModalScalePrompt != null)
            {
                ModalScalePrompt.Visibility =
                    Visibility.Collapsed;
            }

            if (BtnCalibrate != null)
            {
                BtnCalibrate.Content =
                    "Calibrate Distance";
            }
        }

        // =====================================================
        // RENDER ONLY THE ACTIVE WORKFLOW
        // =====================================================

        private void RefreshCurrentOverlay(bool notify)
        {
            ImgHeatMapOverlay.Source = null;

            if (!HasFloorPlan)
            {
                if (notify)
                {
                    MessageBox.Show(
                        "Load a floor plan first.",
                        "No Floor Plan",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            // Keep rendering coordinates consistent
            // with the WPF canvas, not image PixelWidth.
            Canvas activeCanvas = IsSurveyActive
                ? SurveyCanvas
                : PlanningCanvas;

            int width = Math.Max(
                1,
                (int)Math.Ceiling(activeCanvas.Width));

            int height = Math.Max(
                1,
                (int)Math.Ceiling(activeCanvas.Height));

            try
            {
                if (IsSurveyActive)
                {
                    if (_samples.Count < 3)
                    {
                        if (notify)
                        {
                            MessageBox.Show(
                                "Take at least 3 measurements " +
                                "in different places first.",
                                "More Measurements Needed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }

                        return;
                    }

                    ImgHeatMapOverlay.Source =
                        HeatMapRenderer.RenderIdwHeatMap(
                            width,
                            height,
                            _samples,
                            _mapUnitsPerMeter);

                    TxtStatus.Text =
                        $"IDW heat map shown " +
                        $"({_samples.Count} measurements).";
                }
                else
                {
                    if (!_routerPosition.HasValue)
                    {
                        if (notify)
                        {
                            MessageBox.Show(
                                "Choose Place / move virtual " +
                                "router and click the floor " +
                                "plan first.",
                                "Router Needed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }

                        return;
                    }

                    ImgHeatMapOverlay.Source =
                        HeatMapRenderer.RenderPredictiveHeatMap(
                            width,
                            height,
                            _routerPosition.Value,
                            _walls,
                            _mapUnitsPerMeter);

                    TxtStatus.Text =
                        $"Prediction shown with " +
                        $"{_walls.Count} walls.";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text =
                    "Heat map rendering failed: " +
                    ex.Message;

                if (notify)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Rendering Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        // =====================================================
        // VISUAL MARKERS
        // =====================================================

        private static Grid CreateSurveyMarker(
            int number, double rssi)
        {
            Grid marker = new()
            {
                Width = 24,
                Height = 24,
                IsHitTestVisible = true
            };

            marker.Children.Add(new Ellipse
            {
                Fill = Brushes.DeepSkyBlue,
                Stroke = Brushes.White,
                StrokeThickness = 2
            });

            marker.Children.Add(new TextBlock
            {
                Text = number.ToString(),
                Foreground = Brushes.Black,
                FontSize = 11,
                FontWeight = FontWeights.Bold,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

            marker.ToolTip =
                $"Sample {number}: {rssi} dBm";

            // Show tooltips without recording an
            // extra sample when clicking an old marker.
            marker.PreviewMouseLeftButtonDown +=
                (s, args) => args.Handled = true;

            return marker;
        }

        private static Grid CreateRouterMarker()
        {
            Grid marker = new()
            {
                Width = 36,
                Height = 36,
                IsHitTestVisible = true
            };

            marker.Children.Add(new Ellipse
            {
                Fill = Brushes.DarkRed,
                Stroke = Brushes.White,
                StrokeThickness = 2.5
            });

            marker.Children.Add(new TextBlock
            {
                Text = "AP",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

            marker.ToolTip = "Virtual Wi-Fi router";

            marker.PreviewMouseLeftButtonDown +=
                (s, args) => args.Handled = true;

            return marker;
        }

        private void UpdateCounters()
        {
            TxtSurveyCount.Text = _samples.Count == 1
                ? "1 measurement (3 needed for IDW)"
                : $"{_samples.Count} measurements" +
                  (_samples.Count < 3
                      ? " (3 needed for IDW)"
                      : " (IDW ready)");

            TxtPlanningCount.Text =
                $"{_walls.Count} walls  |  " +
                (_routerPosition.HasValue
                    ? "Router placed"
                    : "No virtual router");
        }

        // =====================================================
        // RESET + LOGOUT
        // =====================================================

        private void ResetBothWorkflows()
        {
            CancelCalibration();
            AbortDrawing();

            _samples.Clear();
            _surveyBssid = null;

            _walls.Clear();
            _routerPosition = null;
            _routerMarker = null;

            SurveyCanvas.Children.Clear();
            PlanningCanvas.Children.Clear();

            ImgHeatMapOverlay.Source = null;

            UpdateCounters();
        }

        private void BtnLogout_Click(
            object sender, RoutedEventArgs e)
        {
            SessionService.ClearSession();

            MainWindow login = new();
            login.Show();

            Close();
        }
    }
}
