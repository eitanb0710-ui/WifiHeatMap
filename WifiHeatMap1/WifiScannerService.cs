using System;
using System.Collections.Generic;
using System.Linq;
using ManagedNativeWifi;

namespace WifiHeatMap1
{
    public class AccessPointInfo
    {
        public string Ssid { get; set; } = string.Empty;
        public string Bssid { get; set; } = string.Empty; // MAC-адрес
        public int SignalQuality { get; set; } // Качество сигнала в % (0 - 100)
        public int Rssi { get; set; } // Мощность сигнала в dBm (например, -50 dBm)
    }

    public class WifiScannerService
    {
        public List<AccessPointInfo> ScanNetworks()
        {
            var networkList = new List<AccessPointInfo>();

            try
            {
                var availableNetworks = NativeWifi.EnumerateBssNetworks();

                foreach (var network in availableNetworks)
                {
                    int rssi = network.Rssi;

                    // Перевод RSSI (dBm) в процент качества сигнала (0 - 100%)
                    int quality = Math.Max(0, Math.Min(100, 2 * (rssi + 100)));

                    networkList.Add(new AccessPointInfo
                    {
                        Ssid = string.IsNullOrWhiteSpace(network.Ssid.ToString()) ? "[Скрытая сеть]" : network.Ssid.ToString(),
                        Bssid = network.Bssid.ToString(),
                        SignalQuality = quality,
                        Rssi = rssi
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Wi-Fi Scan Error: {ex.Message}");
            }

            return networkList.OrderByDescending(net => net.SignalQuality).ToList();
        }
    }
}