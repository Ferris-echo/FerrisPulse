// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// ATK Phantom Ultimate battery via the shared 373B:12CD nano receiver.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    internal static class AtkPhantomDeviceHelper
    {
        private const int VendorId = 0x373B;
        private const int ReceiverPid = 0x12CD;
        private const byte IdentityCommand = 0x4A;
        private const int QueryTimeoutMs = 450;

        internal static byte[] BuildRequest(byte command)
        {
            byte[] frame = VxeNearLinkDeviceHelper.BuildReport64(command, VxeNearLinkDeviceHelper.TargetMouse, 0);
            if (command == IdentityCommand)
            {
                frame[3] = 8;
                frame[7] = 6;
                frame[1] += 11;
            }
            return frame;
        }

        internal static bool IsResponse(byte[] frame, byte command, int payloadLength)
        {
            return frame != null && frame.Length == VxeNearLinkDeviceHelper.ReportLength &&
                   frame[0] == VxeNearLinkDeviceHelper.FrameReportId &&
                   frame[1] == VxeNearLinkDeviceHelper.FrameMagic && frame[2] == 0 &&
                   frame[5] == command && frame[6] == payloadLength;
        }

        internal static bool IsPhantomIdentity(byte[] frame)
        {
            // ATK HUB 3.2.33 lists BITMOUSE variants CID 1 / MID 10 or 21.
            return IsResponse(frame, IdentityCommand, 6) && frame[8] == 1 &&
                   (frame[9] == 10 || frame[9] == 21) &&
                   frame[10] == 0 && frame[11] == 0 && frame[12] == 0;
        }

        private static byte[] Query(IntPtr handle, byte command, int payloadLength)
        {
            byte[] request = BuildRequest(command);
            if (!HidTransport.WriteReport(handle, request, request.Length, QueryTimeoutMs)) return null;

            Stopwatch timer = Stopwatch.StartNew();
            int remaining;
            while ((remaining = QueryTimeoutMs - (int)timer.ElapsedMilliseconds) > 0)
            {
                byte[] response;
                if (!HidTransport.ReadReportExact(handle, request.Length, remaining, out response)) return null;
                if (IsResponse(response, command, payloadLength)) return response;
            }
            return null;
        }

        public static List<MouseBatteryInfo> QueryAllDevices()
        {
            var devices = new List<MouseBatteryInfo>();
            foreach (HidDeviceDescriptor descriptor in HidTransport.Enumerate(VendorId, ReceiverPid))
            {
                if (descriptor.UsagePage != VxeNearLinkDeviceHelper.TargetUsagePage ||
                    descriptor.Usage != VxeNearLinkDeviceHelper.TargetUsage ||
                    descriptor.InputReportLength != VxeNearLinkDeviceHelper.ReportLength ||
                    descriptor.OutputReportLength != VxeNearLinkDeviceHelper.ReportLength ||
                    HardwareDriverRegistry.IsExternallyOwnedPath(descriptor.DevicePath)) continue;

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) continue;
                try
                {
                    if (!IsPhantomIdentity(Query(handle, IdentityCommand, 6))) continue;
                    byte[] battery = Query(handle, VxeNearLinkDeviceHelper.CmdGetBatteryLevel, 1);
                    if (battery == null || battery[7] > 100) continue;
                    byte[] charging = Query(handle, VxeNearLinkDeviceHelper.CmdGetBatteryChargingStatus, 1);
                    if (charging == null || charging[7] > 1) continue;

                    var info = new MouseBatteryInfo
                    {
                        DeviceId = "Gaming:373B:12CD",
                        HardwareFingerprint = "HID:373B:12CD",
                        HardwareId = descriptor.InstanceId,
                        DriverId = "BuiltIn:AtkPhantom",
                        DeviceName = "ATK Phantom Ultimate",
                        Brand = "ATK",
                        Category = DeviceCategory.Mouse,
                        Transport = "2.4G 无线",
                        IsDonglePresent = true,
                        IsConnected = true,
                        BatteryPercent = battery[7],
                        ExactBatteryPercent = battery[7],
                        IsCharging = charging[7] == 1,
                        LastUpdated = DateTime.Now,
                        MaxPollingRate = 8000
                    };
                    info.Capabilities.SupportsBattery = true;
                    devices.Add(info);
                    break;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
            return devices;
        }
    }
}
