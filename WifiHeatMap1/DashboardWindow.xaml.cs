
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
        // ==========================================
        // FIELDS
        // ==========================================

        private readonly WifiScannerService _wifiScanner;

        private bool _isMapLoaded = false;

        private BitmapImage? _currentFloorPlanBitmap;

        private double _pixelsPerMeter = 35.0;

        private double _lastDrawnPixelDistance = 0;

        private readonly List<SamplePoint> _samples =
            new List<SamplePoint>();

        private readonly List<WallSegment> _walls =
            new List<WallSegment>();

        private Point? _apPosition = null;

        private UIElement? _apMarkerVisual = null;

        // Identifies the access point used by
        // the current live survey.
        private string? _surveyBssid = null;

        // Line drawing state
        private bool _isDrawingLine = false;

        private Point _lineStartPoint;

        private Line? _currentPreviewLine;

        // NEW: Reference to the User Manual window.
        private UserManualWindow? _userManualWindow;


        // ==========================================
        // CONSTRUCTOR
        // ==========================================

        public DashboardWindow()
        {
            InitializeComponent();

            _wifiScanner = new WifiScannerService();
        }


        // ==========================================
        // NEW: OPEN USER MANUAL
        // ==========================================

        private void BtnUserManual_Click(
            object sender,
            RoutedEventArgs e)
        {
            // If the manual is already open,
            // focus that window instead of opening
            // another copy.
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

            // Create a new manual window.
            _userManualWindow = new UserManualWindow
            {
                Owner = this
            };

            // Clear the reference when the manual closes.
            _userManualWindow.Closed += (s, args) =>
            {
                _userManualWindow = null;
            };

            // Show a non-modal window so the user
            // can continue using the dashboard.
            _userManualWindow.Show();
        }


        // ==========================================
        // LOAD FLOOR PLAN
        // ==========================================

        private void BtnLoadMap_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter =
                    "Image Files (*.png;*.jpeg;*.jpg;*.bmp)|" +
                    "*.png;*.jpeg;*.jpg;*.bmp"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                BitmapImage bitmap = new BitmapImage();

                bitmap.BeginInit();

                bitmap.UriSource = new Uri(dialog.FileName);

                bitmap.CacheOption = BitmapCacheOption.OnLoad;

                bitmap.EndInit();

                // Clear data belonging to the previous map.
                ClearWorkspaceData();

                _pixelsPerMeter = 35.0;
                _lastDrawnPixelDistance = 0;

                TxtScaleDisplay.Text =
                    "Scale: 35.0 px/m (Default)";

                _currentFloorPlanBitmap = bitmap;

                ImgFloorPlan.Source = bitmap;

                double dipWidth = bitmap.Width;
                double dipHeight = bitmap.Height;

                ImgHeatMapOverlay.Width = dipWidth;
                ImgHeatMapOverlay.Height = dipHeight;

                CanvasContainer.Width = dipWidth;
                CanvasContainer.Height = dipHeight;

                HeatMapCanvas.Width = dipWidth;
                HeatMapCanvas.Height = dipHeight;

                TxtPlaceholder.Visibility =
                    Visibility.Collapsed;

                _isMapLoaded = true;

                TxtStatus.Text =
                    $"Loaded floor plan " +
                    $"({bitmap.PixelWidth}x{bitmap.PixelHeight}px).";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to load floor plan:\n{ex.Message}",
                    "Image Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }


        // ==========================================
        // MAPPING MODE SELECTION
        // ==========================================

        private void Mode_Changed(
            object sender,
            RoutedEventArgs e)
        {
            // Events can fire while XAML initializes.
            if (PanelWallMaterial == null ||
                PanelSurveyInfo == null)
            {
                return;
            }

            if (RadioDrawWalls.IsChecked == true)
            {
                PanelWallMaterial.Visibility =
                    Visibility.Visible;

                PanelSurveyInfo.Visibility =
                    Visibility.Collapsed;

                TxtStatus.Text =
                    "Mode: Draw Walls. " +
                    "Click and drag to place walls.";
            }
            else if (RadioLiveSurvey.IsChecked == true)
            {
                PanelWallMaterial.Visibility =
                    Visibility.Collapsed;

                PanelSurveyInfo.Visibility =
                    Visibility.Visible;

                TxtStatus.Text =
                    "Mode: Live Survey. " +
                    "Select an access point and click your location.";
            }
            else if (RadioPlaceAp.IsChecked == true)
            {
                PanelWallMaterial.Visibility =
                    Visibility.Collapsed;

                PanelSurveyInfo.Visibility =
                    Visibility.Collapsed;

                TxtStatus.Text =
                    "Mode: Virtual Router. " +
                    "Click on the map to place the router.";
            }
            else if (RadioCalibrate.IsChecked == true)
            {
                PanelWallMaterial.Visibility =
                    Visibility.Collapsed;

                PanelSurveyInfo.Visibility =
                    Visibility.Collapsed;

                TxtStatus.Text =
                    "Mode: Calibration. " +
                    "Draw a line across a known distance.";
            }
        }


        // ==========================================
        // WALL MATERIAL SELECTION
        // ==========================================

        private void CmbWallMaterial_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (PanelCustomWallLoss == null)
                return;

            bool isCustom =
                CmbWallMaterial.SelectedItem is ComboBoxItem item
                && item.Tag?.ToString() == "Custom";

            PanelCustomWallLoss.Visibility = isCustom
                ? Visibility.Visible
                : Visibility.Collapsed;
        }


        // ==========================================
        // REFRESH WIFI NETWORKS
        // ==========================================

        private void BtnRefreshNetworks_Click(
            object sender,
            RoutedEventArgs e)
        {
            // Remember the previous selection.
            string? previouslySelectedBssid =
                (CmbSelectedNetwork.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString();

            CmbSelectedNetwork.Items.Clear();

            try
            {
                var networks = _wifiScanner.ScanNetworks();

                // Avoid adding one BSSID multiple times.
                HashSet<string> seenBssids =
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                ComboBoxItem? previousSelection = null;

                foreach (var network in networks)
                {
                    string bssid = network.Bssid;

                    if (string.IsNullOrWhiteSpace(bssid))
                        continue;

                    if (!seenBssids.Add(bssid))
                        continue;

                    string ssid =
                        string.IsNullOrWhiteSpace(network.Ssid)
                        ? "[Hidden Network]"
                        : network.Ssid;

                    // Show SSID, signal strength and BSSID.
                    ComboBoxItem item = new ComboBoxItem
                    {
                        Content =
                            $"{ssid} | {network.Rssi} dBm | {bssid}",

                        Tag = bssid,

                        ToolTip =
                            $"SSID: {ssid}\n" +
                            $"BSSID: {bssid}\n" +
                            $"RSSI: {network.Rssi} dBm"
                    };

                    CmbSelectedNetwork.Items.Add(item);

                    if (string.Equals(
                        bssid,
                        previouslySelectedBssid,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        previousSelection = item;
                    }
                }

                // Restore previous selection if possible.
                if (previousSelection != null)
                {
                    CmbSelectedNetwork.SelectedItem =
                        previousSelection;
                }
                else if (!string.IsNullOrWhiteSpace(
                    _surveyBssid))
                {
                    // Try to reselect the AP used by this survey.
                    foreach (ComboBoxItem item
                             in CmbSelectedNetwork.Items)
                    {
                        if (string.Equals(
                            item.Tag?.ToString(),
                            _surveyBssid,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            CmbSelectedNetwork.SelectedItem = item;
                            break;
                        }
                    }
                }

                int count = CmbSelectedNetwork.Items.Count;

                if (count == 0)
                {
                    TxtStatus.Text =
                        "No WiFi access points detected. " +
                        "Check WiFi and location permissions.";
                }
                else
                {
                    TxtStatus.Text =
                        $"Found {count} WiFi access points. " +
                        "Select one to begin surveying.";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "WiFi scan failed.";

                MessageBox.Show(
                    $"Could not scan WiFi networks:\n{ex.Message}",
                    "WiFi Scan Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }


        // ==========================================
        // MOUSE LEFT BUTTON DOWN
        // ==========================================

        private void HeatMapCanvas_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_isMapLoaded)
                return;

            Point clickPos = e.GetPosition(HeatMapCanvas);


            // --------------------------------------
            // MODE 1: LIVE WIFI SURVEY
            // --------------------------------------

            if (RadioLiveSurvey.IsChecked == true)
            {
                // Require the user to select an access point.
                if (CmbSelectedNetwork.SelectedItem
                    is not ComboBoxItem selectedItem)
                {
                    MessageBox.Show(
                        "Refresh the WiFi network list and " +
                        "select an access point first.",
                        "No Network Selected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                string selectedBssid =
                    selectedItem.Tag?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(selectedBssid))
                {
                    MessageBox.Show(
                        "Invalid WiFi access point selection.",
                        "Invalid Network",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                // Prevent mixing access points in one survey.
                if (_samples.Count > 0 &&
                    !string.Equals(
                        _surveyBssid,
                        selectedBssid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "Your current survey contains measurements " +
                        "from another access point.\n\n" +
                        "Clear the canvas before switching BSSIDs.",
                        "Different Access Point",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                try
                {
                    // Take a fresh measurement.
                    var networks = _wifiScanner.ScanNetworks();

                    // Find the selected physical access point.
                    var measuredAp = networks.FirstOrDefault(
                        network => string.Equals(
                            network.Bssid,
                            selectedBssid,
                            StringComparison.OrdinalIgnoreCase));

                    if (measuredAp == null)
                    {
                        TxtStatus.Text =
                            "Selected access point not detected.";

                        MessageBox.Show(
                            "The selected access point was not " +
                            "found in the latest scan.\n\n" +
                            "Try refreshing the network list.",
                            "Access Point Unavailable",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }

                    // Real RSSI measurement only.
                    double rssi = measuredAp.Rssi;

                    _samples.Add(new SamplePoint
                    {
                        Position = clickPos,
                        Rssi = rssi
                    });

                    _surveyBssid = selectedBssid;

                    // Create measurement marker.
                    Grid sampleBadge = CreateSampleMarker(
                        _samples.Count,
                        rssi);

                    sampleBadge.ToolTip =
                        $"Sample #{_samples.Count}\n" +
                        $"SSID: {measuredAp.Ssid}\n" +
                        $"BSSID: {measuredAp.Bssid}\n" +
                        $"RSSI: {rssi} dBm";

                    Canvas.SetLeft(
                        sampleBadge,
                        clickPos.X - 12);

                    Canvas.SetTop(
                        sampleBadge,
                        clickPos.Y - 12);

                    HeatMapCanvas.Children.Add(sampleBadge);

                    TxtStatus.Text =
                        $"Recorded Sample #{_samples.Count}: " +
                        $"{measuredAp.Ssid} " +
                        $"[{rssi} dBm, BSSID: {measuredAp.Bssid}]";
                }
                catch (Exception ex)
                {
                    TxtStatus.Text =
                        "WiFi measurement failed.";

                    MessageBox.Show(
                        $"Unable to measure WiFi signal:\n" +
                        $"{ex.Message}",
                        "Measurement Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }


            // --------------------------------------
            // MODE 2: DRAW WALLS
            // --------------------------------------

            else if (RadioDrawWalls.IsChecked == true)
            {
                _isDrawingLine = true;

                _lineStartPoint = clickPos;

                _currentPreviewLine = new Line
                {
                    X1 = clickPos.X,
                    Y1 = clickPos.Y,
                    X2 = clickPos.X,
                    Y2 = clickPos.Y,

                    Stroke = GetSelectedWallColor(),
                    StrokeThickness = 4
                };

                HeatMapCanvas.Children.Add(
                    _currentPreviewLine);
            }


            // --------------------------------------
            // MODE 3: VIRTUAL ROUTER
            // --------------------------------------

            else if (RadioPlaceAp.IsChecked == true)
            {
                _apPosition = clickPos;

                // Remove the old router marker.
                if (_apMarkerVisual != null)
                {
                    HeatMapCanvas.Children.Remove(
                        _apMarkerVisual);
                }

                _apMarkerVisual = CreateApMarker();

                Canvas.SetLeft(
                    _apMarkerVisual,
                    clickPos.X - 18);

                Canvas.SetTop(
                    _apMarkerVisual,
                    clickPos.Y - 18);

                HeatMapCanvas.Children.Add(
                    _apMarkerVisual);

                RenderPredictiveMap();
            }


            // --------------------------------------
            // MODE 4: CALIBRATE SCALE
            // --------------------------------------

            else if (RadioCalibrate.IsChecked == true)
            {
                _isDrawingLine = true;

                _lineStartPoint = clickPos;

                _currentPreviewLine = new Line
                {
                    X1 = clickPos.X,
                    Y1 = clickPos.Y,
                    X2 = clickPos.X,
                    Y2 = clickPos.Y,

                    Stroke = Brushes.Magenta,
                    StrokeThickness = 3,

                    StrokeDashArray =
                        new DoubleCollection { 4, 2 }
                };

                HeatMapCanvas.Children.Add(
                    _currentPreviewLine);
            }
        }


        // ==========================================
        // MOUSE MOVE
        // ==========================================

        private void HeatMapCanvas_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!_isDrawingLine ||
                _currentPreviewLine == null)
            {
                return;
            }

            Point currentPos =
                e.GetPosition(HeatMapCanvas);

            _currentPreviewLine.X2 = currentPos.X;
            _currentPreviewLine.Y2 = currentPos.Y;
        }


        // ==========================================
        // MOUSE LEFT BUTTON UP
        // ==========================================

        private void HeatMapCanvas_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_isDrawingLine ||
                _currentPreviewLine == null)
            {
                return;
            }

            Point endPoint = e.GetPosition(HeatMapCanvas);


            // --------------------------------------
            // FINISH WALL DRAWING
            // --------------------------------------

            if (RadioDrawWalls.IsChecked == true)
            {
                WallMaterial selectedMat =
                    WallMaterial.Drywall;

                double customDb = 3.0;

                if (CmbWallMaterial.SelectedItem
                    is ComboBoxItem item &&
                    Enum.TryParse(
                        item.Tag?.ToString(),
                        out WallMaterial mat))
                {
                    selectedMat = mat;

                    if (selectedMat == WallMaterial.Custom)
                    {
                        string rawDb =
                            TxtCustomDb.Text.Trim()
                            .Replace(',', '.');

                        bool isValid = double.TryParse(
                            rawDb,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out customDb);

                        if (!isValid ||
                            double.IsNaN(customDb) ||
                            double.IsInfinity(customDb) ||
                            customDb < 0)
                        {
                            HeatMapCanvas.Children.Remove(
                                _currentPreviewLine);

                            _isDrawingLine = false;
                            _currentPreviewLine = null;

                            MessageBox.Show(
                                "Enter a valid non-negative " +
                                "signal loss, such as 8.5.",
                                "Invalid Wall Attenuation",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                            return;
                        }
                    }
                }

                _walls.Add(new WallSegment
                {
                    Start = _lineStartPoint,
                    End = endPoint,

                    Material = selectedMat,
                    CustomAttenuationDb = customDb
                });

                if (selectedMat == WallMaterial.Custom)
                {
                    TxtStatus.Text =
                        $"Added Custom wall " +
                        $"(-{customDb} dB loss).";
                }
                else
                {
                    TxtStatus.Text =
                        $"Added {selectedMat} wall segment.";
                }

                if (_apPosition.HasValue)
                {
                    RenderPredictiveMap();
                }
            }


            // --------------------------------------
            // FINISH SCALE REFERENCE LINE
            // --------------------------------------

            else if (RadioCalibrate.IsChecked == true)
            {
                double deltaX =
                    endPoint.X - _lineStartPoint.X;

                double deltaY =
                    endPoint.Y - _lineStartPoint.Y;

                _lastDrawnPixelDistance = Math.Sqrt(
                    deltaX * deltaX +
                    deltaY * deltaY);

                if (_lastDrawnPixelDistance > 5)
                {
                    ModalScalePrompt.Visibility =
                        Visibility.Visible;

                    TxtMetersInput.Focus();
                    TxtMetersInput.SelectAll();
                }
                else
                {
                    HeatMapCanvas.Children.Remove(
                        _currentPreviewLine);
                }
            }

            _isDrawingLine = false;
            _currentPreviewLine = null;
        }


        // ==========================================
        // APPLY CALIBRATED SCALE
        // ==========================================

        private void BtnApplyScale_Click(
            object sender,
            RoutedEventArgs e)
        {
            string rawMeters =
                TxtMetersInput.Text.Trim().Replace(',', '.');

            bool valid = double.TryParse(
                rawMeters,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double meters);

            if (!valid ||
                double.IsNaN(meters) ||
                double.IsInfinity(meters) ||
                meters <= 0)
            {
                MessageBox.Show(
                    "Please enter a valid positive distance " +
                    "such as 2.5 or 1,8 meters.",
                    "Invalid Distance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _pixelsPerMeter =
                _lastDrawnPixelDistance / meters;

            TxtScaleDisplay.Text =
                $"Scale: {Math.Round(_pixelsPerMeter, 1)} px/m " +
                $"({meters} m reference line)";

            TxtStatus.Text =
                $"Scale calibrated: 1 meter = " +
                $"{Math.Round(_pixelsPerMeter, 1)} pixels.";

            ModalScalePrompt.Visibility =
                Visibility.Collapsed;

            // Refresh the current heat map when appropriate.
            if (RadioLiveSurvey.IsChecked == true &&
                _samples.Count > 0)
            {
                BtnGenerateHeatmap_Click(sender, e);
            }
            else if (_apPosition.HasValue)
            {
                RenderPredictiveMap();
            }
        }


        // ==========================================
        // CANCEL SCALE DIALOG
        // ==========================================

        private void BtnCancelScale_Click(
            object sender,
            RoutedEventArgs e)
        {
            ModalScalePrompt.Visibility =
                Visibility.Collapsed;
        }


        // ==========================================
        // GENERATE HEAT MAP
        // ==========================================

        private void BtnGenerateHeatmap_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isMapLoaded ||
                _currentFloorPlanBitmap == null)
            {
                return;
            }

            int pixelWidth =
                _currentFloorPlanBitmap.PixelWidth;

            int pixelHeight =
                _currentFloorPlanBitmap.PixelHeight;

            if (RadioLiveSurvey.IsChecked == true)
            {
                if (_samples.Count == 0)
                {
                    MessageBox.Show(
                        "Record WiFi measurements before " +
                        "generating the survey heat map.",
                        "No Samples Recorded",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                ImgHeatMapOverlay.Source =
                    HeatMapRenderer.RenderIdwHeatMap(
                        pixelWidth,
                        pixelHeight,
                        _samples,
                        _pixelsPerMeter);

                TxtStatus.Text =
                    $"Generated IDW heat map from " +
                    $"{_samples.Count} measurements.";
            }
            else
            {
                RenderPredictiveMap();
            }
        }


        // ==========================================
        // PREDICTIVE HEAT MAP
        // ==========================================

        private void RenderPredictiveMap()
        {
            if (!_apPosition.HasValue ||
                !_isMapLoaded ||
                _currentFloorPlanBitmap == null)
            {
                return;
            }

            int pixelWidth =
                _currentFloorPlanBitmap.PixelWidth;

            int pixelHeight =
                _currentFloorPlanBitmap.PixelHeight;

            ImgHeatMapOverlay.Source =
                HeatMapRenderer.RenderPredictiveHeatMap(
                    pixelWidth,
                    pixelHeight,
                    _apPosition.Value,
                    _walls,
                    _pixelsPerMeter);

            TxtStatus.Text =
                $"Rendered predictive heat map using " +
                $"{_walls.Count} walls at " +
                $"{Math.Round(_pixelsPerMeter, 1)} px/m.";
        }


        // ==========================================
        // CREATE SURVEY POINT MARKER
        // ==========================================

        private Grid CreateSampleMarker(
            int index,
            double rssi)
        {
            Grid container = new Grid
            {
                Width = 24,
                Height = 24
            };

            Ellipse circle = new Ellipse
            {
                Fill = Brushes.DeepSkyBlue,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };

            TextBlock label = new TextBlock
            {
                Text = index.ToString(),

                Foreground = Brushes.Black,

                FontWeight = FontWeights.Bold,
                FontSize = 11,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

            container.Children.Add(circle);
            container.Children.Add(label);

            container.ToolTip =
                $"Sample #{index}: {rssi} dBm";

            return container;
        }


        // ==========================================
        // CREATE VIRTUAL ROUTER MARKER
        // ==========================================

        private Grid CreateApMarker()
        {
            Grid container = new Grid
            {
                Width = 36,
                Height = 36
            };

            Ellipse outer = new Ellipse
            {
                Fill = new SolidColorBrush(
                    Color.FromArgb(100, 255, 0, 0))
            };

            Ellipse inner = new Ellipse
            {
                Width = 20,
                Height = 20,

                Fill = Brushes.Red,

                Stroke = Brushes.White,
                StrokeThickness = 2
            };

            TextBlock label = new TextBlock
            {
                Text = "📡",
                FontSize = 12,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

            container.Children.Add(outer);
            container.Children.Add(inner);
            container.Children.Add(label);

            container.ToolTip =
                "Virtual Access Point (Router)";

            return container;
        }


        // ==========================================
        // WALL MATERIAL COLOR
        // ==========================================

        private Brush GetSelectedWallColor()
        {
            return CmbWallMaterial.SelectedIndex switch
            {
                1 => Brushes.LightBlue,  // Glass

                2 => Brushes.Orange,     // Brick

                3 => Brushes.Red,        // Concrete

                4 => Brushes.Purple,     // Custom

                _ => Brushes.Yellow      // Drywall
            };
        }


        // ==========================================
        // CLEAR WORKSPACE DATA
        // ==========================================

        private void ClearWorkspaceData()
        {
            _samples.Clear();

            // Forget the previously surveyed BSSID.
            _surveyBssid = null;

            _walls.Clear();

            _apPosition = null;
            _apMarkerVisual = null;

            _isDrawingLine = false;
            _currentPreviewLine = null;

            HeatMapCanvas.Children.Clear();

            ImgHeatMapOverlay.Source = null;

            ModalScalePrompt.Visibility =
                Visibility.Collapsed;
        }


        // ==========================================
        // CLEAR CANVAS BUTTON
        // ==========================================

        private void BtnClear_Click(
            object sender,
            RoutedEventArgs e)
        {
            ClearWorkspaceData();

            TxtStatus.Text =
                "Canvas cleared. Ready for a new survey.";
        }


        // ==========================================
        // LOGOUT
        // ==========================================

        private void BtnLogout_Click(
            object sender,
            RoutedEventArgs e)
        {
            SessionService.ClearSession();

            MainWindow login = new MainWindow();

            login.Show();

            this.Close();
        }
    }
}
