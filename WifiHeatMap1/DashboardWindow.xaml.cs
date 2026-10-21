using System;
using System.Collections.Generic;
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
        private readonly WifiScannerService _wifiScanner;
        private bool _isMapLoaded = false;

        public DashboardWindow()
        {
            InitializeComponent();
            _wifiScanner = new WifiScannerService();
        }

        // 1. Load Floor Plan Image
        private void BtnLoadMap_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Select Floor Plan Image",
                Filter = "Image Files (*.png;*.jpeg;*.jpg;*.bmp)|*.png;*.jpeg;*.jpg;*.bmp"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                BitmapImage bitmap = new BitmapImage(new Uri(openFileDialog.FileName));
                ImgFloorPlan.Source = bitmap;

                // Match Canvas size to floor plan dimensions
                HeatMapCanvas.Width = bitmap.PixelWidth;
                HeatMapCanvas.Height = bitmap.PixelHeight;

                TxtPlaceholder.Visibility = Visibility.Collapsed;
                _isMapLoaded = true;
                TxtStatus.Text = $"Floor plan loaded ({bitmap.PixelWidth}x{bitmap.PixelHeight}px). Click on the map to sample Wi-Fi!";
            }
        }

        // 2. Click Canvas to Sample Signal and Draw Heat Marker
        private void HeatMapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isMapLoaded)
            {
                MessageBox.Show("Please load a floor plan image first.", "No Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Point clickPos = e.GetPosition(HeatMapCanvas);

            // Scan Wi-Fi networks
            var networks = _wifiScanner.ScanNetworks();

            if (networks.Count == 0)
            {
                // Fallback for testing on desktop PCs without active Wi-Fi adapters
                DrawSignalPoint(clickPos, 75, "Simulated Network (-50 dBm)");
                TxtStatus.Text = $"Sampled point at ({Math.Round(clickPos.X)}, {Math.Round(clickPos.Y)}): Simulated signal (75%)";
                return;
            }

            var strongest = networks[0];
            DrawSignalPoint(clickPos, strongest.SignalQuality, $"{strongest.Ssid} ({strongest.Rssi} dBm)");
            TxtStatus.Text = $"Sampled ({Math.Round(clickPos.X)}, {Math.Round(clickPos.Y)}): {strongest.Ssid} - {strongest.SignalQuality}% ({strongest.Rssi} dBm)";
        }

        // 3. Draw Colored Signal Point on Canvas
        private void DrawSignalPoint(Point position, int signalQuality, string tooltipText)
        {
            double radius = 25;

            // Determine color based on signal percentage (Green = Strong, Yellow = Fair, Red = Weak)
            Color pointColor;
            if (signalQuality >= 70)
                pointColor = Color.FromArgb(180, 0, 255, 0); // Green
            else if (signalQuality >= 40)
                pointColor = Color.FromArgb(180, 255, 255, 0); // Yellow
            else
                pointColor = Color.FromArgb(180, 255, 0, 0); // Red

            Ellipse marker = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new RadialGradientBrush
                {
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(pointColor, 0.0),
                        new GradientStop(Colors.Transparent, 1.0)
                    }
                },
                ToolTip = tooltipText
            };

            Canvas.SetLeft(marker, position.X - radius);
            Canvas.SetTop(marker, position.Y - radius);

            HeatMapCanvas.Children.Add(marker);
        }

        // 4. Standalone Wi-Fi Scan Test
        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            var networks = _wifiScanner.ScanNetworks();

            if (networks.Count == 0)
            {
                MessageBox.Show("No Wi-Fi networks found or Wi-Fi adapter is turned off.", "Wi-Fi Scan", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var top = networks[0];
            MessageBox.Show($"Found {networks.Count} networks.\nStrongest: {top.Ssid} ({top.SignalQuality}% / {top.Rssi} dBm)", "Scan Results");
        }

        // 5. Clear All Markers
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            HeatMapCanvas.Children.Clear();
            TxtStatus.Text = "Heat map markers cleared.";
        }

        // 6. Logout
        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            // Clear local persistent session state
            SessionService.ClearSession();

            MainWindow loginWindow = new MainWindow();
            loginWindow.Show();
            this.Close();
        }
    }
}