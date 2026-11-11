
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WifiHeatMap1
{
    // Preserves the data contract used by DashboardWindow.xaml.cs.
    public class AccessPointInfo
    {
        public string Ssid { get; set; } = string.Empty;
        public string Bssid { get; set; } = string.Empty;
        public int SignalQuality { get; set; }
        public int Rssi { get; set; }
    }

    /// <summary>
    /// A native Windows WLAN scanner implemented with C# P/Invoke.
    /// The radio driver measures RSSI; this class requests scans, reads the
    /// native BSS list and converts its results into AccessPointInfo objects.
    /// No third-party Wi-Fi library is used.
    /// </summary>
    public sealed class WifiScannerService
    {
        private const uint ClientVersion = 2;
        private const uint NotificationSourceAcm = 0x00000008;
        private const int ScanCompletedCode = 7;
        private const int ScanFailedCode = 8;
        private const uint BssTypeAny = 3;
        private const int ScanTimeoutMilliseconds = 4500;

        // Native WLAN_INTERFACE_INFO_LIST = 8-byte header + 532-byte entries.
        private const int InterfaceListHeaderSize = 8;
        private const int InterfaceEntrySize = 532;

        // Native WLAN_BSS_LIST = 8-byte header + 360-byte WLAN_BSS_ENTRY items.
        // The offsets below are from the Windows SDK's WLAN_BSS_ENTRY layout.
        private const int BssListHeaderSize = 8;
        private const int BssEntrySize = 360;
        private const int SsidLengthOffset = 0;
        private const int SsidBytesOffset = 4;
        private const int BssidOffset = 40;
        private const int RssiOffset = 56;

        private sealed class ScanState : IDisposable
        {
            public ManualResetEventSlim Completed { get; } = new(false);
            public int FailedFlag;
            public void Dispose() => Completed.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WlanNotificationData
        {
            public uint NotificationSource;
            public int NotificationCode;
            public Guid InterfaceGuid;
            public uint DataSize;
            public IntPtr Data;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void WlanNotificationCallback(
            ref WlanNotificationData notification, IntPtr context);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanOpenHandle(
            uint clientVersion, IntPtr reserved,
            out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanCloseHandle(
            IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanEnumInterfaces(
            IntPtr clientHandle, IntPtr reserved,
            out IntPtr interfaceList);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanScan(
            IntPtr clientHandle, ref Guid interfaceGuid,
            IntPtr ssid, IntPtr informationElements, IntPtr reserved);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanGetNetworkBssList(
            IntPtr clientHandle, ref Guid interfaceGuid,
            IntPtr ssid, uint bssType,
            [MarshalAs(UnmanagedType.Bool)] bool securityEnabled,
            IntPtr reserved, out IntPtr bssList);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern uint WlanRegisterNotification(
            IntPtr clientHandle, uint notificationSource,
            [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate,
            WlanNotificationCallback callback,
            IntPtr callbackContext, IntPtr reserved,
            out uint previousNotificationSource);

        [DllImport("wlanapi.dll", ExactSpelling = true)]
        private static extern void WlanFreeMemory(IntPtr memory);

        /// <summary>
        /// Initiates new scans, waits for completion and returns measured RSSI.
        /// This synchronous compatibility method can pause the UI for up to
        /// roughly 4.5 seconds; an async dashboard can call it via Task.Run.
        /// </summary>
        public List<AccessPointInfo> ScanNetworks()
        {
            Check(WlanOpenHandle(ClientVersion, IntPtr.Zero, out _,
                out IntPtr handle), "WlanOpenHandle");

            // Create notification events before registering the native callback.
            var scanStates = new Dictionary<Guid, ScanState>();
            WlanNotificationCallback? callback = null;

            try
            {
                List<Guid> interfaces = GetInterfaceGuids(handle);
                if (interfaces.Count == 0)
                    throw new InvalidOperationException(
                        "Windows did not find an enabled Wi-Fi adapter.");

                foreach (Guid guid in interfaces)
                    scanStates.Add(guid, new ScanState());

                callback = (ref WlanNotificationData notification,
                            IntPtr context) =>
                {
                    try
                    {
                        if (notification.NotificationSource != NotificationSourceAcm)
                            return;

                        if (notification.NotificationCode != ScanCompletedCode &&
                            notification.NotificationCode != ScanFailedCode)
                            return;

                        if (!scanStates.TryGetValue(notification.InterfaceGuid,
                            out ScanState? state))
                            return;

                        if (notification.NotificationCode == ScanFailedCode)
                            Interlocked.Exchange(ref state.FailedFlag, 1);

                        state.Completed.Set();
                    }
                    catch (Exception ex)
                    {
                        // Never let an exception escape a native callback.
                        Debug.WriteLine("WLAN notification error: " + ex.Message);
                    }
                };

                Check(WlanRegisterNotification(handle, NotificationSourceAcm,
                    false, callback, IntPtr.Zero, IntPtr.Zero, out _),
                    "WlanRegisterNotification");

                // Start all adapters before waiting, avoiding one long scan
                // delay per adapter when more than one Wi-Fi card is present.
                foreach (Guid guidValue in interfaces)
                {
                    Guid guid = guidValue;
                    Check(WlanScan(handle, ref guid, IntPtr.Zero,
                        IntPtr.Zero, IntPtr.Zero), "WlanScan");
                }

                var all = new List<AccessPointInfo>();

                foreach (Guid guidValue in interfaces)
                {
                    ScanState state = scanStates[guidValue];
                    if (!state.Completed.Wait(ScanTimeoutMilliseconds))
                        throw new TimeoutException(
                            "The Wi-Fi scan did not finish within 4.5 seconds. " +
                            "No potentially stale RSSI sample was recorded.");

                    if (Volatile.Read(ref state.FailedFlag) != 0)
                        throw new InvalidOperationException(
                            "Windows reported that the Wi-Fi scan failed.");

                    Guid guid = guidValue;
                    all.AddRange(ReadBssEntries(handle, ref guid));
                }

                // The dashboard selects an AP by BSSID. If multiple adapters
                // detect that BSSID, retain the strongest result in this scan.
                return all.GroupBy(ap => ap.Bssid,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(ap => ap.Rssi).First())
                    .OrderByDescending(ap => ap.Rssi)
                    .ToList();
            }
            finally
            {
                // Closing the WLAN handle unregisters the callback.
                WlanCloseHandle(handle, IntPtr.Zero);
                GC.KeepAlive(callback);

                foreach (ScanState state in scanStates.Values)
                    state.Dispose();
            }
        }

        private static List<Guid> GetInterfaceGuids(IntPtr handle)
        {
            IntPtr nativeList = IntPtr.Zero;
            try
            {
                Check(WlanEnumInterfaces(handle, IntPtr.Zero,
                    out nativeList), "WlanEnumInterfaces");

                uint count = unchecked((uint)Marshal.ReadInt32(nativeList));
                if (count > 64)
                    throw new InvalidDataException(
                        "Windows returned an invalid Wi-Fi interface count.");

                var result = new List<Guid>();
                for (int i = 0; i < (int)count; i++)
                {
                    IntPtr entry = IntPtr.Add(nativeList,
                        InterfaceListHeaderSize + i * InterfaceEntrySize);
                    result.Add(Marshal.PtrToStructure<Guid>(entry));
                }
                return result;
            }
            finally
            {
                if (nativeList != IntPtr.Zero)
                    WlanFreeMemory(nativeList);
            }
        }

        private static List<AccessPointInfo> ReadBssEntries(
            IntPtr handle, ref Guid guid)
        {
            IntPtr nativeList = IntPtr.Zero;
            try
            {
                Check(WlanGetNetworkBssList(handle, ref guid,
                    IntPtr.Zero, BssTypeAny, false, IntPtr.Zero,
                    out nativeList), "WlanGetNetworkBssList");

                int totalSize = Marshal.ReadInt32(nativeList, 0);
                uint count = unchecked((uint)Marshal.ReadInt32(nativeList, 4));

                if (totalSize < BssListHeaderSize ||
                    count > (uint)((totalSize - BssListHeaderSize) / BssEntrySize))
                {
                    throw new InvalidDataException(
                        "Windows returned a malformed Wi-Fi BSS list.");
                }

                var result = new List<AccessPointInfo>();
                for (int i = 0; i < (int)count; i++)
                {
                    IntPtr entry = IntPtr.Add(nativeList,
                        BssListHeaderSize + i * BssEntrySize);

                    int ssidLength = Marshal.ReadInt32(entry, SsidLengthOffset);
                    if (ssidLength < 0 || ssidLength > 32)
                        continue;

                    int rssi = Marshal.ReadInt32(entry, RssiOffset);
                    if (rssi >= 0 || rssi < -120)
                        continue;

                    byte[] ssidBytes = new byte[ssidLength];
                    if (ssidLength > 0)
                    {
                        Marshal.Copy(IntPtr.Add(entry, SsidBytesOffset),
                            ssidBytes, 0, ssidLength);
                    }

                    byte[] mac = new byte[6];
                    Marshal.Copy(IntPtr.Add(entry, BssidOffset), mac, 0, 6);

                    string ssid = Encoding.UTF8.GetString(ssidBytes);
                    if (string.IsNullOrWhiteSpace(ssid))
                        ssid = "[Hidden network]";

                    result.Add(new AccessPointInfo
                    {
                        Ssid = ssid,
                        Bssid = BitConverter.ToString(mac).Replace('-', ':'),
                        Rssi = rssi,
                        // This percentage is OUR heuristic, not Windows link quality.
                        SignalQuality = Math.Clamp(2 * (rssi + 100), 0, 100)
                    });
                }
                return result;
            }
            finally
            {
                if (nativeList != IntPtr.Zero)
                    WlanFreeMemory(nativeList);
            }
        }

        private static void Check(uint result, string operation)
        {
            if (result == 0)
                return;

            if (result == 5) // ERROR_ACCESS_DENIED
            {
                throw new UnauthorizedAccessException(
                    operation + " was denied by Windows. Enable Location " +
                    "Services and desktop-app location access under " +
                    "Windows Settings > Privacy & security > Location.");
            }

            throw new Win32Exception((int)result,
                operation + " failed (Windows error " + result + "): " +
                new Win32Exception((int)result).Message);
        }
    }
}
