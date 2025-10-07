using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Monitorian.CLI;

public static class MonitorManager
{
    #region Win32 API

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor,
        out uint pdwNumberOfPhysicalMonitors);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor,
        uint dwPhysicalMonitorArraySize,
        [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyPhysicalMonitor(
        IntPtr hMonitor);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorCapabilities(
        SafePhysicalMonitorHandle hMonitor,
        out MC_CAPS pdwMonitorCapabilities,
        out MC_SUPPORTED_COLOR_TEMPERATURE pdwSupportedColorTemperatures);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(
        SafePhysicalMonitorHandle hMonitor,
        out uint pdwMinimumBrightness,
        out uint pdwCurrentBrightness,
        out uint pdwMaximumBrightness);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(
        SafePhysicalMonitorHandle hMonitor,
        uint dwNewBrightness);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        SafePhysicalMonitorHandle hMonitor,
        byte bVCPCode,
        out LPMC_VCP_CODE_TYPE pvct,
        out uint pdwCurrentValue,
        out uint pdwMaximumValue);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVCPFeature(
        SafePhysicalMonitorHandle hMonitor,
        byte bVCPCode,
        uint dwNewValue);

    [DllImport("User32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        ref RECT lprcMonitor,
        IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [Flags]
    private enum MC_CAPS
    {
        MC_CAPS_NONE = 0x00000000,
        MC_CAPS_BRIGHTNESS = 0x00000002,
        MC_CAPS_CONTRAST = 0x00000004,
    }

    [Flags]
    private enum MC_SUPPORTED_COLOR_TEMPERATURE
    {
        MC_SUPPORTED_COLOR_TEMPERATURE_NONE = 0x00000000,
    }

    private enum LPMC_VCP_CODE_TYPE
    {
        MC_MOMENTARY,
        MC_SET_PARAMETER
    }

    private enum VcpCode : byte
    {
        Luminance = 0x10,
        Contrast = 0x12,
    }

    #endregion

    public static Task<IEnumerable<IMonitor>> EnumerateMonitorsAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var monitors = new List<IMonitor>();
        var monitorHandles = new List<IntPtr>();

        // Enumerate display monitors
        MonitorEnumProc callback = (hMonitor, hdcMonitor, ref lprcMonitor, dwData) =>
        {
            monitorHandles.Add(hMonitor);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        foreach (var handle in monitorHandles)
        {
            try
            {
                var physicalMonitors = EnumeratePhysicalMonitors(handle);
                foreach (var physicalMonitor in physicalMonitors)
                {
                    var monitor = new DdcMonitor(physicalMonitor);
                    monitors.Add(monitor);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate physical monitors for handle {handle}: {ex.Message}");
            }
        }

        return Task.FromResult<IEnumerable<IMonitor>>(monitors);
    }

    private static IEnumerable<PhysicalMonitorInfo> EnumeratePhysicalMonitors(IntPtr monitorHandle)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitorHandle, out uint count))
        {
            yield break;
        }

        if (count == 0)
        {
            yield break;
        }

        var physicalMonitors = new PHYSICAL_MONITOR[count];

        if (!GetPhysicalMonitorsFromHMONITOR(monitorHandle, count, physicalMonitors))
        {
            yield break;
        }

        foreach (var physicalMonitor in physicalMonitors)
        {
            var handle = new SafePhysicalMonitorHandle(physicalMonitor.hPhysicalMonitor);
            var capability = GetMonitorCapability(handle);

            yield return new PhysicalMonitorInfo
            {
                Handle = handle,
                Description = physicalMonitor.szPhysicalMonitorDescription,
                Capability = capability
            };
        }
    }

    private static MonitorCapability GetMonitorCapability(SafePhysicalMonitorHandle handle)
    {
        var isBrightnessSupported = GetMonitorCapabilities(handle, out MC_CAPS caps, out _) &&
                                   caps.HasFlag(MC_CAPS.MC_CAPS_BRIGHTNESS);

        var isContrastSupported = GetVCPFeatureAndVCPFeatureReply(handle, (byte)VcpCode.Contrast, out _, out _, out _);

        return new MonitorCapability
        {
            IsBrightnessSupported = isBrightnessSupported,
            IsContrastSupported = isContrastSupported
        };
    }
}

public class SafePhysicalMonitorHandle : SafeHandle
{
    public SafePhysicalMonitorHandle(IntPtr handle) : base(IntPtr.Zero, true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        return MonitorManager.DestroyPhysicalMonitor(handle);
    }
}

public interface IMonitor
{
    string DeviceInstanceId { get; }
    string Description { get; }
    bool IsBrightnessSupported { get; }
    bool IsContrastSupported { get; }
    bool IsReachable { get; }
    int Brightness { get; }
    int Contrast { get; }
    AccessResult SetBrightness(int brightness);
    AccessResult SetContrast(int contrast);
}

public class DdcMonitor : IMonitor, IDisposable
{
    private readonly SafePhysicalMonitorHandle _handle;
    private readonly MonitorCapability _capability;
    private uint _minimumBrightness = 0;
    private uint _maximumBrightness = 100;
    private uint _minimumContrast = 0;
    private uint _maximumContrast = 100;

    public string DeviceInstanceId { get; }
    public string Description { get; }
    public bool IsBrightnessSupported => _capability.IsBrightnessSupported;
    public bool IsContrastSupported => _capability.IsContrastSupported;
    public bool IsReachable => !_handle.IsClosed;

    public int Brightness { get; private set; }
    public int Contrast { get; private set; }

    public DdcMonitor(PhysicalMonitorInfo info)
    {
        _handle = info.Handle;
        _capability = info.Capability;
        Description = info.Description;
        DeviceInstanceId = $"MONITOR\\{Description}\\{_handle.DangerousGetHandle():X8}";

        UpdateBrightness();
        UpdateContrast();
    }

    private void UpdateBrightness()
    {
        if (!IsBrightnessSupported)
        {
            Brightness = -1;
            return;
        }

        if (GetMonitorBrightness(_handle, out uint minimum, out uint current, out uint maximum))
        {
            if (minimum < maximum && minimum <= current && current <= maximum)
            {
                Brightness = (int)Math.Round((double)(current - minimum) / (maximum - minimum) * 100D, MidpointRounding.AwayFromZero);
                _minimumBrightness = minimum;
                _maximumBrightness = maximum;
            }
            else
            {
                Brightness = -1;
            }
        }
        else
        {
            Brightness = -1;
        }
    }

    private void UpdateContrast()
    {
        if (!IsContrastSupported)
        {
            Contrast = -1;
            return;
        }

        if (GetVCPFeatureAndVCPFeatureReply(_handle, (byte)VcpCode.Contrast, out _, out uint current, out uint maximum))
        {
            if (0 < maximum && 0 <= current && current <= maximum)
            {
                Contrast = (int)Math.Round((double)current / maximum * 100D, MidpointRounding.AwayFromZero);
                _minimumContrast = 0;
                _maximumContrast = maximum;
            }
            else
            {
                Contrast = -1;
            }
        }
        else
        {
            Contrast = -1;
        }
    }

    public AccessResult SetBrightness(int brightness)
    {
        if (brightness < 0 || brightness > 100)
            throw new ArgumentOutOfRangeException(nameof(brightness), brightness, "The brightness must be from 0 to 100.");

        if (!IsBrightnessSupported)
            return new AccessResult(AccessStatus.NotSupported, "Brightness not supported");

        var rawValue = (uint)Math.Round(brightness / 100D * (_maximumBrightness - _minimumBrightness) + _minimumBrightness, MidpointRounding.AwayFromZero);

        if (SetMonitorBrightness(_handle, rawValue))
        {
            Brightness = brightness;
            return AccessResult.Succeeded;
        }

        return new AccessResult(AccessStatus.Failed, "Failed to set brightness");
    }

    public AccessResult SetContrast(int contrast)
    {
        if (contrast < 0 || contrast > 100)
            throw new ArgumentOutOfRangeException(nameof(contrast), contrast, "The contrast must be from 0 to 100.");

        if (!IsContrastSupported)
            return new AccessResult(AccessStatus.NotSupported, "Contrast not supported");

        var rawValue = (uint)Math.Round(contrast / 100D * (_maximumContrast - _minimumContrast) + _minimumContrast, MidpointRounding.AwayFromZero);

        if (SetVCPFeature(_handle, (byte)VcpCode.Contrast, rawValue))
        {
            Contrast = contrast;
            return AccessResult.Succeeded;
        }

        return new AccessResult(AccessStatus.Failed, "Failed to set contrast");
    }

    public void Dispose()
    {
        _handle?.Dispose();
    }

    // Import the required Win32 functions
    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(
        SafePhysicalMonitorHandle hMonitor,
        out uint pdwMinimumBrightness,
        out uint pdwCurrentBrightness,
        out uint pdwMaximumBrightness);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(
        SafePhysicalMonitorHandle hMonitor,
        uint dwNewBrightness);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        SafePhysicalMonitorHandle hMonitor,
        byte bVCPCode,
        out LPMC_VCP_CODE_TYPE pvct,
        out uint pdwCurrentValue,
        out uint pdwMaximumValue);

    [DllImport("Dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVCPFeature(
        SafePhysicalMonitorHandle hMonitor,
        byte bVCPCode,
        uint dwNewValue);

    private enum LPMC_VCP_CODE_TYPE
    {
        MC_MOMENTARY,
        MC_SET_PARAMETER
    }

    private enum VcpCode : byte
    {
        Luminance = 0x10,
        Contrast = 0x12,
    }
}

public enum AccessStatus
{
    None = 0,
    Succeeded,
    Failed,
    NotSupported
}

public class AccessResult
{
    public AccessStatus Status { get; }
    public string Message { get; }

    public AccessResult(AccessStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    public static readonly AccessResult Succeeded = new(AccessStatus.Succeeded, string.Empty);
    public static readonly AccessResult Failed = new(AccessStatus.Failed, string.Empty);
    public static readonly AccessResult NotSupported = new(AccessStatus.NotSupported, string.Empty);
}

public class PhysicalMonitorInfo
{
    public SafePhysicalMonitorHandle Handle { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public MonitorCapability Capability { get; set; } = null!;
}

public class MonitorCapability
{
    public bool IsBrightnessSupported { get; set; }
    public bool IsContrastSupported { get; set; }
}
