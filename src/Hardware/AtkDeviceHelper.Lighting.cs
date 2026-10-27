// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
using System;
using System.Diagnostics;
using System.IO;

namespace RazerBatteryTray
{
    public static partial class AtkDeviceHelper
    {
        public static bool SupportsLightingDevice(string deviceId)
        {
            return string.Equals(deviceId, "ATK:320F:5088", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(deviceId, "HID:320F:5088", StringComparison.OrdinalIgnoreCase);
        }

        public static bool SupportsLightingSpeed(int mode)
        {
            return mode >= 1 && mode <= 15 && mode != 6;
        }

        public static bool SupportsLightingColor(int mode)
        {
            return mode >= 1 && mode <= 16 && mode != 4 && mode != 11 && mode != 12;
        }

        public static AtkLightingSettings DecodeLightingConfig(byte[] config)
        {
            ValidateLightingConfig(config);
            return new AtkLightingSettings
            {
                Enabled = config[28] == 0,
                Mode = config[1], Brightness = config[2], Speed = 4 - config[3],
                Rainbow = config[5] != 0, Red = config[6], Green = config[7], Blue = config[8],
                Profile = config[0]
            };
        }

        public static byte[] PatchLightingConfig(byte[] original, AtkLightingSettings settings)
        {
            ValidateLightingConfig(original);
            if (settings == null) throw new ArgumentNullException("settings");
            if (settings.Mode < 1 || settings.Mode > 20 || settings.Brightness < 0 || settings.Brightness > 4 ||
                settings.Speed < 0 || settings.Speed > 4) throw new ArgumentOutOfRangeException("settings");
            byte[] result = (byte[])original.Clone();
            result[1] = (byte)settings.Mode;
            result[28] = (byte)(settings.Enabled ? 0 : 1);
            if (settings.Mode != 20) result[2] = (byte)settings.Brightness;
            if (SupportsLightingSpeed(settings.Mode)) result[3] = (byte)(4 - settings.Speed);
            if (SupportsLightingColor(settings.Mode))
            {
                result[5] = (byte)(settings.Rainbow ? 255 : 0);
                result[6] = settings.Red;
                result[7] = settings.Green;
                result[8] = settings.Blue;
            }
            return result;
        }

        public static byte[] BuildLightingReport(ushort requestCode, byte command, int offset, int count, byte[] config)
        {
            if (requestCode == 0 || (command != 5 && command != 6) || offset < 0 || offset >= 56 ||
                count < 1 || count > 24 || offset + count > 56) throw new ArgumentOutOfRangeException("offset");
            byte[] report = new byte[64];
            report[0] = 4;
            report[1] = (byte)requestCode;
            report[2] = (byte)(requestCode >> 8);
            report[3] = command;
            report[4] = (byte)count;
            report[5] = (byte)offset;
            if (command == 6)
            {
                if (config == null || config.Length != 56) throw new ArgumentException("需要完整配置快照。", "config");
                Array.Copy(config, offset, report, 8, count);
            }
            return report;
        }

        public static bool IsLightingResponse(byte[] report, ushort requestCode, byte command, int offset, int count)
        {
            return requestCode != 0 && report != null && report.Length >= (command == 5 ? 8 + count : 8) &&
                report[0] == 4 && report[1] == (byte)requestCode && report[2] == (byte)(requestCode >> 8) &&
                report[3] == command && report[4] == count && report[5] == offset && report[6] == 0 && report[7] == 0;
        }

        public static AtkLightingSettings ReadLightingSettings(string deviceId)
        {
            lock (hidLock)
            {
                HidDeviceDescriptor device = FindLightingDevice(deviceId, null);
                IntPtr handle = OpenLightingDevice(device);
                try
                {
                    AtkLightingSettings settings = DecodeLightingConfig(ReadLightingConfig(handle));
                    settings.DevicePath = device.DevicePath;
                    return settings;
                }
                finally { HidTransport.Close(handle); }
            }
        }

        public static AtkLightingSettings ApplyLightingSettings(string deviceId, AtkLightingSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.DevicePath))
                throw new ArgumentException("请先读取目标键盘的灯光配置。", "settings");
            lock (hidLock)
            {
                HidDeviceDescriptor device = FindLightingDevice(deviceId, settings.DevicePath);
                IntPtr handle = OpenLightingDevice(device);
                try
                {
                    byte[] original = ReadLightingConfig(handle);
                    if (original[0] != settings.Profile) throw new IOException("键盘配置档已改变，请重新读取后再应用。");
                    byte[] updated = PatchLightingConfig(original, settings);
                    byte[] verified = CommitLightingConfig(original, updated,
                        delegate(byte[] value) { WriteLightingConfig(handle, value); },
                        delegate { return ReadLightingConfig(handle); });
                    AtkLightingSettings result = DecodeLightingConfig(verified);
                    result.DevicePath = device.DevicePath;
                    return result;
                }
                finally { HidTransport.Close(handle); }
            }
        }

        private static void ValidateLightingConfig(byte[] config)
        {
            if (config == null || config.Length != 56 || config[1] < 1 || config[1] > 20 ||
                config[2] > 4 || config[3] > 4 || config[28] > 1)
                throw new ArgumentException("无法识别此固件的灯光配置；未写入任何数据。", "config");
        }

        private static byte[] CommitLightingConfig(byte[] original, byte[] updated, Action<byte[]> write, Func<byte[]> read)
        {
            bool writing = false;
            try
            {
                if (!ChunkEquals(original, updated, 0, 56))
                {
                    writing = true;
                    write(updated);
                }
                byte[] verified = read();
                if (!ChunkEquals(updated, verified, 0, 56)) throw new IOException("灯光回读与目标配置不一致。");
                return verified;
            }
            catch (Exception error)
            {
                if (!writing) throw;
                try
                {
                    write(original);
                    if (!ChunkEquals(original, read(), 0, 56)) throw new IOException("恢复校验失败。");
                }
                catch (Exception rollbackError)
                {
                    throw new IOException("应用失败，且无法确认原配置已恢复；请保持键盘连接并重新读取。" + rollbackError.Message, error);
                }
                throw new IOException("应用失败，已恢复原配置。" + error.Message, error);
            }
        }

        private static bool ChunkEquals(byte[] first, byte[] second, int offset, int count)
        {
            for (int index = offset; index < offset + count; index++) if (first[index] != second[index]) return false;
            return true;
        }

        private static HidDeviceDescriptor FindLightingDevice(string deviceId, string path)
        {
            if (!SupportsLightingDevice(deviceId)) throw new NotSupportedException("目前仅实测支持 ATK V75X 2.4G 灯光控制。");
            HidDeviceDescriptor match = null;
            foreach (HidDeviceDescriptor device in HidTransport.Enumerate(0x320F, 0x5088))
            {
                if (!IsSupportedDevice(device) || device.UsagePage != 0xFF1C || device.Usage != 0x92 ||
                    device.InputReportLength != 64 || device.OutputReportLength != 64) continue;
                if (path != null && !string.Equals(device.DevicePath, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (match != null) throw new IOException("检测到多个同型号键盘，暂不执行配置写入。");
                match = device;
            }
            if (match == null) throw new IOException("原 ATK V75X 接收器已断开或通道已改变，请重新打开灯光控制。");
            return match;
        }

        private static IntPtr OpenLightingDevice(HidDeviceDescriptor device)
        {
            IntPtr handle = HidTransport.Open(device.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) throw new IOException("无法打开键盘厂商通信通道，请关闭其他键盘驱动后重试。");
            return handle;
        }

        private static byte[] ReadLightingConfig(IntPtr handle)
        {
            byte[] config = new byte[56];
            for (int offset = 0; offset < 56; offset += 24)
            {
                int count = Math.Min(24, 56 - offset);
                byte[] response = ExchangeLighting(handle, 5, offset, count, null);
                Array.Copy(response, 8, config, offset, count);
            }
            ValidateLightingConfig(config);
            return config;
        }

        private static void WriteLightingConfig(IntPtr handle, byte[] config)
        {
            for (int offset = 0; offset < 56; offset += 24)
                ExchangeLighting(handle, 6, offset, Math.Min(24, 56 - offset), config);
        }

        private static byte[] ExchangeLighting(IntPtr handle, byte command, int offset, int count, byte[] config)
        {
            sequence = (ushort)(sequence == ushort.MaxValue ? 1 : sequence + 1);
            ushort requestCode = sequence;
            byte[] report = BuildLightingReport(requestCode, command, offset, count, config);
            if (!HidTransport.WriteReport(handle, report, report.Length, 300)) throw new IOException("键盘通信写入超时。");
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 800)
            {
                byte[] response;
                if (!HidTransport.ReadReport(handle, 64, Math.Max(1, 800 - (int)timer.ElapsedMilliseconds), out response)) break;
                if (IsLightingResponse(response, requestCode, command, offset, count)) return response;
            }
            throw new IOException("键盘未返回有效应答，请唤醒键盘并关闭官方驱动后重试。");
        }
    }
}