
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

// FIX: Resolve the conflict between System.IO.Path
// and System.Windows.Shapes.Path.
using Path = System.IO.Path;

namespace WifiHeatMap1
{
    public partial class DashboardWindow : Window
    {
        // ==========================================
        // SHARED PROJECT STATE
        // ==========================================

        private readonly WifiScannerService _wifiScanner;

        private BitmapImage? _floorPlan;
        private byte[]? _floorPlanBytes;
        private string _floorPlanFileName = "floorplan.png";

        private double _mapUnitsPerMeter = 35.0;
        private bool _scaleCalibrated;

        // LIVE SURVEY

        private readonly List<SamplePoint> _samples = new();

        private string? _surveyBssid;
        private string? _surveySsid;

        // PREDICTIVE PLAN

        private readonly List<WallSegment> _walls = new();

        private Point? _routerPosition;
        private UIElement? _routerMarker;

        // DRAWING AND CALIBRATION

        private bool _isCalibrating;
        private bool _isDrawing;
        private Point _dragStart;
        private Canvas? _drawingCanvas;
        private Line? _previewLine;
        private Canvas? _calibrationCanvas;
        private Line? _calibrationLine;
        private double _calibrationLineLength;

        // SAVE / LOAD STATE

        private string? _projectPath;
        private bool _isDirty;
        private bool _allowClose;

        // OPTIMIZER STATE

        private int _planningRevision;
        private bool _isOptimizing;
        private RouterOptimizationResult? _suggestion;
        private UIElement? _suggestionMarker;

        // USER MANUAL

        private UserManualWindow? _userManualWindow;

        private bool IsSurveyActive =>
            ModeTabs.SelectedItem == TabSurvey;

        private bool HasFloorPlan =>
            _floorPlan != null;


        // ==========================================
        // CONSTRUCTOR
        // ==========================================

        public DashboardWindow()
        {
            InitializeComponent();

            _wifiScanner = new WifiScannerService();

            UpdateCounters();
            UpdateProjectLabel();
        }


        // ==========================================
        // USER MANUAL
        // ==========================================

        private void BtnUserManual_Click(
            object sender,
            RoutedEventArgs e)
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


        // ==========================================
        // SAVE PROJECT / SAVE AS
        // ==========================================

        private void BtnSaveProject_Click(
            object sender,
            RoutedEventArgs e)
        {
            SaveProject(false);
        }

        private void BtnSaveAs_Click(
            object sender,
            RoutedEventArgs e)
        {
            SaveProject(true);
        }

        private bool SaveProject(bool saveAs)
        {
            if (_floorPlanBytes == null)
            {
                MessageBox.Show(
                    "Load a floor plan before saving.",
                    "Nothing to Save",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return false;
            }

            string? path = _projectPath;

            if (saveAs || string.IsNullOrWhiteSpace(path))
            {
                SaveFileDialog dialog = new()
                {
                    Title = "Save WiFi Heat Map project",

                    Filter =
                        "WiFi Heat Map Project (*.wifimap)|" +
                        "*.wifimap",

                    DefaultExt = ".wifimap",
                    AddExtension = true,

                    FileName = _projectPath == null
                        ? "My WiFi Map.wifimap"
                        : Path.GetFileName(_projectPath)
                };

                if (dialog.ShowDialog() != true)
                    return false;

                path = dialog.FileName;
            }

            try
            {
                WifiMapProject data = new()
                {
                    FloorPlanFileName = _floorPlanFileName,

                    MapUnitsPerMeter = _mapUnitsPerMeter,
                    ScaleCalibrated = _scaleCalibrated,

                    SurveyBssid = _surveyBssid,
                    SurveySsid = _surveySsid,

                    RouterPosition = _routerPosition.HasValue
                        ? MapPointData.FromPoint(
                            _routerPosition.Value)
                        : null,

                    ShowPredictiveWorkspace =
                        !IsSurveyActive,

                    Samples = _samples.Select(
                        s => new SurveySampleData
                        {
                            X = s.Position.X,
                            Y = s.Position.Y,
                            Rssi = s.Rssi
                        }).ToList(),

                    Walls = _walls.Select(
                        w => new WallData
                        {
                            X1 = w.Start.X,
                            Y1 = w.Start.Y,
                            X2 = w.End.X,
                            Y2 = w.End.Y,

                            Material = w.Material.ToString(),

                            CustomAttenuationDb =
                                w.CustomAttenuationDb
                        }).ToList()
                };

                ProjectStorage.Save(
                    path!,
                    data,
                    _floorPlanBytes);

                _projectPath = path;
                _isDirty = false;

                UpdateProjectLabel();

                TxtStatus.Text =
                    "Project saved: " +
                    Path.GetFileName(path);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not save project:\n" +
                    ex.Message,
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }
        }


        // ==========================================
        // OPEN PROJECT
        // ==========================================

        private void BtnOpenProject_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!ConfirmDiscard())
                return;

            OpenFileDialog dialog = new()
            {
                Title = "Open WiFi Heat Map project",

                Filter =
                    "WiFi Heat Map Project (*.wifimap)|" +
                    "*.wifimap"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                // Validate before changing current work.
                LoadedWifiMapProject loaded =
                    ProjectStorage.Load(dialog.FileName);

                BitmapImage image = DecodeImage(
                    loaded.FloorPlanBytes);

                ValidatePositions(
                    loaded.Project,
                    image.Width,
                    image.Height);

                // Everything was decoded successfully.
                ResetBothWorkflows();

                _floorPlan = image;
                _floorPlanBytes = loaded.FloorPlanBytes;

                _floorPlanFileName = Path.GetFileName(
                    loaded.Project.FloorPlanFileName);

                _mapUnitsPerMeter =
                    loaded.Project.MapUnitsPerMeter;

                _scaleCalibrated =
                    loaded.Project.ScaleCalibrated;

                ConfigureFloorPlan(image);

                // Restore the network identity.
                _surveyBssid =
                    loaded.Project.SurveyBssid;

                _surveySsid =
                    loaded.Project.SurveySsid;

                RestoreSavedNetwork();

                // Restore actual RSSI samples.
                foreach (SurveySampleData s
                         in loaded.Project.Samples)
                {
                    Point position = new Point(s.X, s.Y);

                    _samples.Add(new SamplePoint
                    {
                        Position = position,
                        Rssi = s.Rssi
                    });

                    AddSurveyMarker(
                        _samples.Count,
                        position,
                        s.Rssi,
                        _surveySsid ?? "Saved network",
                        _surveyBssid ?? "");
                }

                // Restore walls and materials.
                foreach (WallData w
                         in loaded.Project.Walls)
                {
                    WallMaterial material =
                        Enum.Parse<WallMaterial>(
                            w.Material);

                    WallSegment segment = new()
                    {
                        Start = new Point(w.X1, w.Y1),
                        End = new Point(w.X2, w.Y2),

                        Material = material,

                        CustomAttenuationDb =
                            w.CustomAttenuationDb
                    };

                    _walls.Add(segment);
                    AddWallVisual(segment);
                }

                // Restore the virtual router.
                if (loaded.Project.RouterPosition != null)
                {
                    PlaceRouter(
                        loaded.Project.RouterPosition.ToPoint(),
                        false);
                }

                // Restore the last selected workspace.
                ModeTabs.SelectedItem =
                    loaded.Project.ShowPredictiveWorkspace
                        ? TabPlanning
                        : TabSurvey;

                UpdateScaleLabel();
                UpdateCounters();
                RefreshCurrentOverlay(false);

                _projectPath = dialog.FileName;
                _isDirty = false;

                UpdateProjectLabel();

                TxtStatus.Text =
                    "Opened " +
                    Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not open project:\n" +
                    ex.Message,
                    "Open Project Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        // ==========================================
        // VALIDATE LOADED MAP OBJECT POSITIONS
        // ==========================================

        private static void ValidatePositions(
            WifiMapProject project,
            double width,
            double height)
        {
            static bool Inside(
                double x,
                double y,
                double w,
                double h)
            {
                return x >= 0 &&
                       y >= 0 &&
                       x <= w &&
                       y <= h;
            }

            foreach (SurveySampleData s in project.Samples)
            {
                if (!Inside(
                    s.X, s.Y, width, height))
                {
                    throw new InvalidDataException(
                        "A measurement is outside the floor plan.");
                }
            }

            foreach (WallData wall in project.Walls)
            {
                if (!Inside(
                        wall.X1,
                        wall.Y1,
                        width,
                        height) ||
                    !Inside(
                        wall.X2,
                        wall.Y2,
                        width,
                        height))
                {
                    throw new InvalidDataException(
                        "A wall is outside the floor plan.");
                }
            }

            if (project.RouterPosition != null &&
                !Inside(
                    project.RouterPosition.X,
                    project.RouterPosition.Y,
                    width,
                    height))
            {
                throw new InvalidDataException(
                    "The router is outside the floor plan.");
            }
        }


        // ==========================================
        // RESTORE SAVED NETWORK SELECTION
        // ==========================================

        private void RestoreSavedNetwork()
        {
            CmbSelectedNetwork.Items.Clear();

            if (string.IsNullOrWhiteSpace(_surveyBssid))
                return;

            ComboBoxItem saved = new()
            {
                Content =
                    $"{_surveySsid ?? "Saved AP"} | " +
                    $"{_surveyBssid} (saved)",

                Tag = _surveyBssid
            };

            CmbSelectedNetwork.Items.Add(saved);

            CmbSelectedNetwork.SelectedItem = saved;
        }


        // ==========================================
        // UNSAVED CHANGES
        // ==========================================

        private bool ConfirmDiscard()
        {
            if (!_isDirty)
                return true;

            return MessageBox.Show(
                "You have unsaved changes. " +
                "Continue without saving them?\n" +
                "Choose No to return and save your project.",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
                == MessageBoxResult.Yes;
        }

        private void DashboardWindow_Closing(
            object sender,
            CancelEventArgs e)
        {
            if (!_allowClose && !ConfirmDiscard())
            {
                e.Cancel = true;
            }
        }

        private void UpdateProjectLabel()
        {
            if (TxtProjectName == null)
                return;

            TxtProjectName.Text =
                (_projectPath == null
                    ? "Untitled project"
                    : Path.GetFileName(_projectPath))
                +
                (_isDirty ? "  * unsaved" : "");
        }

        private void MarkDirty(bool planningChanged = false)
        {
            _isDirty = true;

            if (planningChanged)
            {
                _planningRevision++;

                ClearSuggestion();
            }

            UpdateProjectLabel();
        }


        // ==========================================
        // LOAD FLOOR PLAN IMAGE
        // ==========================================

        private void BtnLoadMap_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Choose a floor plan",

                Filter =
                    "Image Files|" +
                    "*.png;*.jpg;*.jpeg;*.bmp"
            };

            if (dialog.ShowDialog() != true ||
                !ConfirmDiscard())
            {
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(
                    dialog.FileName);

                if (bytes.Length > 25 * 1024 * 1024)
                {
                    throw new InvalidDataException(
                        "Floor plan image exceeds 25 MB.");
                }

                BitmapImage image = DecodeImage(bytes);

                ResetBothWorkflows();

                _floorPlan = image;
                _floorPlanBytes = bytes;

                _floorPlanFileName = Path.GetFileName(
                    dialog.FileName);

                _mapUnitsPerMeter = 35.0;
                _scaleCalibrated = false;
                _projectPath = null;

                ConfigureFloorPlan(image);
                UpdateScaleLabel();

                MarkDirty(true);

                TxtStatus.Text =
                    "Floor plan loaded. Save it as a " +
                    "new .wifimap project.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to load image:\n" +
                    ex.Message,
                    "Floor Plan Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        // ==========================================
        // DECODE EMBEDDED FLOOR PLAN
        // ==========================================

        private static BitmapImage DecodeImage(
            byte[] bytes)
        {
            using MemoryStream stream = new(
                bytes,
                writable: false);

            BitmapImage image = new();

            image.BeginInit();

            image.CacheOption =
                BitmapCacheOption.OnLoad;

            image.StreamSource = stream;

            image.EndInit();

            if ((long)image.PixelWidth *
                image.PixelHeight > 20_000_000L)
            {
                throw new InvalidDataException(
                    "Floor plan exceeds 20 million pixels.");
            }

            image.Freeze();

            return image;
        }


        // ==========================================
        // CONFIGURE SHARED MAP DIMENSIONS
        // ==========================================

        private void ConfigureFloorPlan(BitmapImage image)
        {
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
        }

        private void UpdateScaleLabel()
        {
            TxtScaleDisplay.Text =
                $"Scale: {_mapUnitsPerMeter:F1} map units/m"
                +
                (_scaleCalibrated
                    ? " (calibrated)"
                    : " (default — calibrate)");
        }


        // ==========================================
        // SWITCH WORKSPACES
        // ==========================================

        private void ModeTabs_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(
                    e.OriginalSource,
                    ModeTabs) ||
                SurveyCanvas == null ||
                PlanningCanvas == null ||
                TxtWorkspaceTitle == null ||
                TxtStatus == null)
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
                : "Simulated router placement and wall-aware coverage";

            TxtModeBadge.Text = survey
                ? "MEASURED"
                : "SIMULATED";

            TxtMapHint.Text = survey
                ? "Stand at a real location, then click " +
                  "the matching spot on the plan."
                : "Click to place a router, or select " +
                  "Draw Walls and drag a segment.";

            RefreshCurrentOverlay(false);
        }


        // ==========================================
        // PREDICTIVE DRAWING TOOL CHANGED
        // ==========================================

        private void PlanTool_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (PanelWallMaterial == null ||
                RadioDrawWalls == null)
            {
                return;
            }

            PanelWallMaterial.Visibility =
                RadioDrawWalls.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void CmbWallMaterial_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (PanelCustomWallLoss == null)
                return;

            bool custom =
                CmbWallMaterial.SelectedItem
                    is ComboBoxItem item &&
                item.Tag?.ToString() == "Custom";

            PanelCustomWallLoss.Visibility = custom
                ? Visibility.Visible
                : Visibility.Collapsed;
        }


        // ==========================================
        // SCAN WIFI NETWORKS
        // ==========================================

        private void BtnRefreshNetworks_Click(
            object sender,
            RoutedEventArgs e)
        {
            string? previous =
                (CmbSelectedNetwork.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString();

            CmbSelectedNetwork.Items.Clear();

            try
            {
                HashSet<string> seen = new(
                    StringComparer.OrdinalIgnoreCase);

                ComboBoxItem? preferred = null;

                foreach (AccessPointInfo ap
                         in _wifiScanner.ScanNetworks())
                {
                    if (string.IsNullOrWhiteSpace(ap.Bssid) ||
                        !seen.Add(ap.Bssid))
                    {
                        continue;
                    }

                    string ssid =
                        string.IsNullOrWhiteSpace(ap.Ssid)
                            ? "[Hidden network]"
                            : ap.Ssid;

                    ComboBoxItem item = new()
                    {
                        Content =
                            $"{ssid} | {ap.Rssi} dBm | " +
                            $"{ap.Bssid}",

                        Tag = ap.Bssid,

                        ToolTip =
                            $"SSID: {ssid}\n" +
                            $"BSSID: {ap.Bssid}\n" +
                            $"RSSI: {ap.Rssi} dBm"
                    };

                    CmbSelectedNetwork.Items.Add(item);

                    if (string.Equals(
                        ap.Bssid,
                        previous ?? _surveyBssid,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        preferred = item;
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
                        : "No access points detected. " +
                          "Check Wi-Fi and Windows location permissions.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text =
                    "Wi-Fi scan failed: " + ex.Message;
            }
        }


        // ==========================================
        // LIVE SURVEY CANVAS
        // ==========================================

        private void SurveyCanvas_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!HasFloorPlan ||
                ModalScalePrompt.Visibility ==
                    Visibility.Visible)
            {
                return;
            }

            Point point =
                e.GetPosition(SurveyCanvas);

            if (_isCalibrating)
            {
                BeginLine(SurveyCanvas, point, true);
            }
            else
            {
                RecordSurveyPoint(point);
            }
        }


        // ==========================================
        // RECORD REAL RSSI SAMPLE
        // ==========================================

        private void RecordSurveyPoint(Point position)
        {
            if (CmbSelectedNetwork.SelectedItem
                    is not ComboBoxItem selected ||
                selected.Tag is not string bssid ||
                string.IsNullOrWhiteSpace(bssid))
            {
                MessageBox.Show(
                    "Select an access point first.",
                    "No Network",
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
                    "This survey contains readings " +
                    "from another BSSID. Clear Measurements " +
                    "before switching.",
                    "Different Access Point",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                AccessPointInfo? ap =
                    _wifiScanner.ScanNetworks()
                        .FirstOrDefault(
                            n => string.Equals(
                                n.Bssid,
                                bssid,
                                StringComparison.OrdinalIgnoreCase));

                if (ap == null)
                {
                    TxtStatus.Text =
                        "Selected access point not found. " +
                        "Refresh the list.";

                    return;
                }

                if (ap.Rssi >= 0 || ap.Rssi < -120)
                {
                    TxtStatus.Text =
                        "Invalid RSSI; no sample recorded.";

                    return;
                }

                _samples.Add(new SamplePoint
                {
                    Position = position,
                    Rssi = ap.Rssi
                });

                _surveyBssid = bssid;
                _surveySsid = ap.Ssid;

                AddSurveyMarker(
                    _samples.Count,
                    position,
                    ap.Rssi,
                    ap.Ssid,
                    bssid);

                MarkDirty();
                UpdateCounters();
                RefreshCurrentOverlay(false);

                TxtStatus.Text =
                    _samples.Count < 3
                        ? $"Recorded {ap.Rssi} dBm. " +
                          $"{3 - _samples.Count} more for IDW."
                        : $"Recorded {ap.Rssi} dBm. " +
                          $"IDW updated ({_samples.Count} points).";
            }
            catch (Exception ex)
            {
                TxtStatus.Text =
                    "Measurement failed: " +
                    ex.Message;
            }
        }


        // ==========================================
        // DRAW A SURVEY MARKER
        // ==========================================

        private void AddSurveyMarker(
            int number,
            Point position,
            double rssi,
            string ssid,
            string bssid)
        {
            Grid marker = new()
            {
                Width = 24,
                Height = 24
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
                FontWeight = FontWeights.Bold,
                FontSize = 11,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

            marker.ToolTip =
                $"Sample {number}\n" +
                $"SSID: {ssid}\n" +
                $"BSSID: {bssid}\n" +
                $"RSSI: {rssi} dBm";

            marker.PreviewMouseLeftButtonDown +=
                (s, args) => args.Handled = true;

            Canvas.SetLeft(
                marker,
                position.X - 12);

            Canvas.SetTop(
                marker,
                position.Y - 12);

            SurveyCanvas.Children.Add(marker);
        }


        // ==========================================
        // GENERATE LIVE SURVEY HEAT MAP
        // ==========================================

        private void BtnGenerateSurvey_Click(
            object sender,
            RoutedEventArgs e)
        {
            RefreshCurrentOverlay(true);
        }


        // ==========================================
        // CLEAR LIVE SURVEY
        // ==========================================

        private void BtnClearSurvey_Click(
            object sender,
            RoutedEventArgs e)
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
            _surveySsid = null;

            MarkDirty();

            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                "Survey cleared. Predictive plan " +
                "remains unchanged.";
        }


        // ==========================================
        // PREDICTIVE PLANNING CANVAS
        // ==========================================

        private void PlanningCanvas_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!HasFloorPlan ||
                ModalScalePrompt.Visibility ==
                    Visibility.Visible)
            {
                return;
            }

            Point point =
                e.GetPosition(PlanningCanvas);

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
                PlaceRouter(point, true);
            }
        }


        // ==========================================
        // PLACE OR REPOSITION ROUTER
        // ==========================================

        private void PlaceRouter(
            Point position,
            bool changed)
        {
            _routerPosition = position;

            if (_routerMarker != null)
            {
                PlanningCanvas.Children.Remove(
                    _routerMarker);
            }

            Grid marker = new()
            {
                Width = 36,
                Height = 36
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
                FontWeight = FontWeights.Bold,
                FontSize = 12,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

            marker.ToolTip =
                "Virtual Wi-Fi router";

            marker.PreviewMouseLeftButtonDown +=
                (s, e) => e.Handled = true;

            _routerMarker = marker;

            Canvas.SetLeft(
                marker,
                position.X - 18);

            Canvas.SetTop(
                marker,
                position.Y - 18);

            PlanningCanvas.Children.Add(marker);

            if (changed)
            {
                MarkDirty(true);
            }

            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                "Virtual router placed. Prediction updated.";
        }


        // ==========================================
        // GENERATE PREDICTIVE HEAT MAP
        // ==========================================

        private void BtnGeneratePrediction_Click(
            object sender,
            RoutedEventArgs e)
        {
            RefreshCurrentOverlay(true);
        }


        // ==========================================
        // CLEAR PREDICTIVE PLAN
        // ==========================================

        private void BtnClearPlanning_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_walls.Count == 0 &&
                !_routerPosition.HasValue)
            {
                return;
            }

            if (MessageBox.Show(
                "Delete the virtual router and all walls?",
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

            MarkDirty(true);

            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                "Predictive plan cleared. " +
                "Live Survey remains unchanged.";
        }


        // ==========================================
        // RUN AUTOMATIC ROUTER OPTIMIZATION
        // ==========================================

        private async void BtnOptimize_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!HasFloorPlan)
            {
                MessageBox.Show(
                    "Load a floor plan first.",
                    "No Floor Plan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            string raw =
                TxtCoverageThreshold.Text.Trim()
                    .Replace(',', '.');

            if (!double.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double threshold) ||
                !double.IsFinite(threshold) ||
                threshold < -100 ||
                threshold > -30)
            {
                MessageBox.Show(
                    "Enter an RSSI target between " +
                    "-100 and -30 dBm.",
                    "Invalid Coverage Target",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!_scaleCalibrated &&
                MessageBox.Show(
                    "The floor plan is not calibrated. " +
                    "Optimization may be misleading. " +
                    "Continue anyway?",
                    "Calibration Recommended",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            {
                return;
            }

            if (_isOptimizing)
                return;

            ClearSuggestion();

            _isOptimizing = true;
            BtnOptimize.IsEnabled = false;

            TxtOptimizerResult.Text =
                "Evaluating candidate locations...";

            TxtStatus.Text =
                "Finding best virtual router location...";

            // Copy wall geometry before background work.
            List<WallSegment> snapshot =
                _walls.Select(
                    w => new WallSegment
                    {
                        Start = w.Start,
                        End = w.End,
                        Material = w.Material,

                        CustomAttenuationDb =
                            w.CustomAttenuationDb
                    }).ToList();

            double width = PlanningCanvas.Width;
            double height = PlanningCanvas.Height;
            double scale = _mapUnitsPerMeter;

            Point? current = _routerPosition;
            int revision = _planningRevision;

            try
            {
                RouterOptimizationResult result =
                    await Task.Run(
                        () => RouterOptimizer.FindBest(
                            width,
                            height,
                            snapshot,
                            scale,
                            threshold,
                            current));

                // Do not apply an outdated result.
                if (revision != _planningRevision ||
                    !HasFloorPlan)
                {
                    TxtOptimizerResult.Text =
                        "Plan changed. Run optimization again.";

                    return;
                }

                _suggestion = result;

                AddSuggestionMarker(
                    result.BestPosition);

                BtnAcceptBest.IsEnabled = true;

                string before =
                    result.PreviousCoveragePercent.HasValue
                        ? $"Current: " +
                          $"{result.PreviousCoveragePercent.Value:F1}% | "
                        : "No current router | ";

                TxtOptimizerResult.Text =
                    before +
                    $"Suggested: {result.BestCoveragePercent:F1}% " +
                    $"covered at {threshold:F0} dBm. " +
                    $"Checked {result.CandidateCount} locations.";

                TxtStatus.Text =
                    "Green marker shows suggested router " +
                    "position. Click Apply to use it.";
            }
            catch (Exception ex)
            {
                TxtOptimizerResult.Text =
                    "Optimization failed: " +
                    ex.Message;
            }
            finally
            {
                _isOptimizing = false;
                BtnOptimize.IsEnabled = true;
            }
        }


        // ==========================================
        // APPLY OPTIMIZER RECOMMENDATION
        // ==========================================

        private void BtnAcceptBest_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_suggestion == null)
                return;

            Point best = _suggestion.BestPosition;
            double score =
                _suggestion.BestCoveragePercent;

            // Moves the actual simulated router.
            PlaceRouter(best, true);

            RadioPlaceAp.IsChecked = true;

            TxtOptimizerResult.Text =
                $"Applied suggested router position " +
                $"({score:F1}% modeled coverage).";
        }


        // ==========================================
        // DRAW SUGGESTED ROUTER MARKER
        // ==========================================

        private void AddSuggestionMarker(Point position)
        {
            Grid marker = new()
            {
                Width = 40,
                Height = 40
            };

            marker.Children.Add(new Ellipse
            {
                Fill = Brushes.ForestGreen,
                Stroke = Brushes.White,
                StrokeThickness = 3
            });

            marker.Children.Add(new TextBlock
            {
                Text = "BEST",
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeights.Bold,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

            marker.IsHitTestVisible = false;

            marker.ToolTip =
                "Suggested router position " +
                "(not applied yet)";

            Canvas.SetLeft(
                marker,
                position.X - 20);

            Canvas.SetTop(
                marker,
                position.Y - 20);

            PlanningCanvas.Children.Add(marker);

            _suggestionMarker = marker;
        }


        // ==========================================
        // INVALIDATE OLD OPTIMIZATION RESULT
        // ==========================================

        private void ClearSuggestion()
        {
            if (PlanningCanvas != null &&
                _suggestionMarker != null)
            {
                PlanningCanvas.Children.Remove(
                    _suggestionMarker);
            }

            _suggestionMarker = null;
            _suggestion = null;

            if (BtnAcceptBest != null)
            {
                BtnAcceptBest.IsEnabled = false;
            }

            if (TxtOptimizerResult != null)
            {
                TxtOptimizerResult.Text =
                    "Run optimization to compare coverage.";
            }
        }


        // ==========================================
        // BEGIN WALL OR CALIBRATION LINE
        // ==========================================

        private void BeginLine(
            Canvas canvas,
            Point start,
            bool calibration)
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

                StrokeThickness =
                    calibration ? 3 : 4,

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


        // ==========================================
        // UPDATE LINE WHILE DRAGGING
        // ==========================================

        private void MapCanvas_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!_isDrawing ||
                _drawingCanvas == null ||
                _previewLine == null ||
                !ReferenceEquals(sender, _drawingCanvas))
            {
                return;
            }

            Point p = ClampToCanvas(
                e.GetPosition(_drawingCanvas),
                _drawingCanvas);

            _previewLine.X2 = p.X;
            _previewLine.Y2 = p.Y;
        }


        // ==========================================
        // FINISH WALL OR CALIBRATION LINE
        // ==========================================

        private void MapCanvas_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
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
                e.GetPosition(canvas),
                canvas);

            line.X2 = end.X;
            line.Y2 = end.Y;

            _isDrawing = false;
            _drawingCanvas = null;
            _previewLine = null;

            if (canvas.IsMouseCaptured)
            {
                canvas.ReleaseMouseCapture();
            }

            double length =
                (end - _dragStart).Length;

            if (length <= 5)
            {
                canvas.Children.Remove(line);

                TxtStatus.Text =
                    "Line too short. Drag farther.";

                return;
            }

            // CALIBRATION

            if (_isCalibrating)
            {
                _calibrationCanvas = canvas;
                _calibrationLine = line;
                _calibrationLineLength = length;

                ModalScalePrompt.Visibility =
                    Visibility.Visible;

                TxtMetersInput.Focus();
                TxtMetersInput.SelectAll();

                return;
            }

            // WALL DRAWING IS PREDICTIVE ONLY

            if (!ReferenceEquals(
                    canvas,
                    PlanningCanvas) ||
                RadioDrawWalls.IsChecked != true)
            {
                canvas.Children.Remove(line);
                return;
            }

            WallMaterial material =
                GetWallMaterial();

            double customLoss = 3.0;

            if (material == WallMaterial.Custom)
            {
                string raw =
                    TxtCustomDb.Text.Trim()
                        .Replace(',', '.');

                if (!double.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out customLoss) ||
                    !double.IsFinite(customLoss) ||
                    customLoss < 0 ||
                    customLoss > 100)
                {
                    canvas.Children.Remove(line);

                    MessageBox.Show(
                        "Custom wall loss must be " +
                        "between 0 and 100 dB.",
                        "Invalid Wall Loss",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }
            }

            WallSegment wall = new()
            {
                Start = _dragStart,
                End = end,
                Material = material,

                CustomAttenuationDb =
                    customLoss
            };

            _walls.Add(wall);

            MarkDirty(true);
            UpdateCounters();
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                $"Added {material} wall " +
                $"({_walls.Count} walls total).";
        }


        // ==========================================
        // RESTORE SAVED WALL VISUAL
        // ==========================================

        private void AddWallVisual(WallSegment wall)
        {
            Line line = new()
            {
                X1 = wall.Start.X,
                Y1 = wall.Start.Y,

                X2 = wall.End.X,
                Y2 = wall.End.Y,

                Stroke = WallColor(wall.Material),
                StrokeThickness = 4,

                IsHitTestVisible = false
            };

            PlanningCanvas.Children.Add(line);
        }


        // ==========================================
        // CLAMP MOUSE POSITION
        // ==========================================

        private static Point ClampToCanvas(
            Point p,
            Canvas canvas)
        {
            return new Point(
                Math.Clamp(p.X, 0, canvas.Width),
                Math.Clamp(p.Y, 0, canvas.Height));
        }


        // ==========================================
        // CANCEL INCOMPLETE LINE
        // ==========================================

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


        // ==========================================
        // WALL MATERIAL AND COLOR
        // ==========================================

        private WallMaterial GetWallMaterial()
        {
            if (CmbWallMaterial.SelectedItem
                    is ComboBoxItem item &&
                Enum.TryParse(
                    item.Tag?.ToString(),
                    out WallMaterial result))
            {
                return result;
            }

            return WallMaterial.Drywall;
        }

        private Brush GetWallColor()
        {
            return WallColor(GetWallMaterial());
        }

        private static Brush WallColor(
            WallMaterial material)
        {
            return material switch
            {
                WallMaterial.Glass =>
                    Brushes.LightBlue,

                WallMaterial.Brick =>
                    Brushes.Orange,

                WallMaterial.Concrete =>
                    Brushes.Red,

                WallMaterial.Custom =>
                    Brushes.MediumPurple,

                _ => Brushes.Gold
            };
        }


        // ==========================================
        // START SHARED DISTANCE CALIBRATION
        // ==========================================

        private void BtnCalibrate_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!HasFloorPlan)
            {
                MessageBox.Show(
                    "Load a floor plan first.",
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
                    "existing scale kept.";

                return;
            }

            AbortDrawing();

            _isCalibrating = true;

            BtnCalibrate.Content =
                "Cancel Calibration";

            TxtStatus.Text =
                "Drag along a known distance " +
                "on the floor plan.";

            TxtMapHint.Text =
                "CALIBRATION: Drag across a known length.";
        }


        // ==========================================
        // APPLY CALIBRATED SCALE
        // ==========================================

        private void BtnApplyScale_Click(
            object sender,
            RoutedEventArgs e)
        {
            string raw =
                TxtMetersInput.Text.Trim()
                    .Replace(',', '.');

            if (!double.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double meters) ||
                !double.IsFinite(meters) ||
                meters <= 0)
            {
                MessageBox.Show(
                    "Enter a positive distance in meters.",
                    "Invalid Distance",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            double scale =
                _calibrationLineLength / meters;

            if (!double.IsFinite(scale) ||
                scale <= 0 ||
                scale > 100000)
            {
                MessageBox.Show(
                    "The resulting scale is invalid.",
                    "Invalid Scale",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _mapUnitsPerMeter = scale;
            _scaleCalibrated = true;

            CancelCalibration();

            UpdateScaleLabel();
            MarkDirty(true);
            RefreshCurrentOverlay(false);

            TxtStatus.Text =
                $"Calibrated: {_mapUnitsPerMeter:F1} " +
                "map units per meter.";
        }


        // ==========================================
        // CANCEL CALIBRATION
        // ==========================================

        private void BtnCancelScale_Click(
            object sender,
            RoutedEventArgs e)
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

            _calibrationCanvas = null;
            _calibrationLine = null;

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


        // ==========================================
        // RENDER CURRENT WORKSPACE
        // ==========================================

        private void RefreshCurrentOverlay(bool notify)
        {
            if (ImgHeatMapOverlay == null)
                return;

            ImgHeatMapOverlay.Source = null;

            if (!HasFloorPlan)
            {
                if (notify)
                {
                    MessageBox.Show(
                        "Load a floor plan first.",
                        "No Floor Plan");
                }

                return;
            }

            Canvas canvas = IsSurveyActive
                ? SurveyCanvas
                : PlanningCanvas;

            int width = Math.Max(
                1,
                (int)Math.Ceiling(canvas.Width));

            int height = Math.Max(
                1,
                (int)Math.Ceiling(canvas.Height));

            try
            {
                if (IsSurveyActive)
                {
                    if (_samples.Count < 3)
                    {
                        if (notify)
                        {
                            MessageBox.Show(
                                "Collect at least three measurements " +
                                "in different locations.",
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
                }
                else
                {
                    if (!_routerPosition.HasValue)
                    {
                        if (notify)
                        {
                            MessageBox.Show(
                                "Place a virtual router first.",
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


        // ==========================================
        // UPDATE WORKSPACE COUNTERS
        // ==========================================

        private void UpdateCounters()
        {
            TxtSurveyCount.Text =
                $"{_samples.Count} measurements"
                +
                (_samples.Count < 3
                    ? " (3 needed for IDW)"
                    : " (IDW ready)");

            TxtPlanningCount.Text =
                $"{_walls.Count} walls | "
                +
                (_routerPosition.HasValue
                    ? "Router placed"
                    : "No virtual router");
        }


        // ==========================================
        // RESET BOTH WORKSPACES
        // ==========================================

        private void ResetBothWorkflows()
        {
            CancelCalibration();
            AbortDrawing();

            _samples.Clear();
            _surveyBssid = null;
            _surveySsid = null;

            _walls.Clear();
            _routerPosition = null;
            _routerMarker = null;

            SurveyCanvas.Children.Clear();
            PlanningCanvas.Children.Clear();

            CmbSelectedNetwork.Items.Clear();

            ImgHeatMapOverlay.Source = null;

            _planningRevision++;

            ClearSuggestion();

            UpdateCounters();
        }


        // ==========================================
        // LOGOUT
        // ==========================================

        private void BtnLogout_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!ConfirmDiscard())
                return;

            _allowClose = true;

            SessionService.ClearSession();

            MainWindow login = new();

            login.Show();

            Close();
        }
    }
}
