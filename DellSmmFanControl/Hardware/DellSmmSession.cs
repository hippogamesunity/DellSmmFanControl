using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DellSmmFanControl.Hardware;

internal sealed class DellSmmSession : IDisposable
{
    private const string ServiceName = "DellFanControlSmm";
    private const uint ServiceAccess = 0x10035; // DELETE, START, STOP, QUERY_STATUS, QUERY_CONFIG
    private const uint Ioctl = (0xB424u << 16) | (0xB42u << 2);
    private SafeServiceHandle? _service;
    private SafeFileHandle? _device;
    private bool _ownsService;
    private bool _restoreBiosControl;
    public string? CleanupError { get; private set; }

    public DellSmmSession()
    {
        if (!Environment.Is64BitProcess)
            throw new InvalidOperationException("Dell SMM requires a 64-bit application.");
        using RegistryKey? bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        if (bios?.GetValue("SystemManufacturer") is not string manufacturer ||
            !manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Direct SMM access is supported only on Dell motherboards.");

        string driver = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "bzh_dell_smm_io_x64.sys"));
        // Keep the verified file locked against replacement until the driver
        // is loaded and its device opened, rather than only during hashing.
        using FileStream driverFile = File.OpenRead(driver);
        string hash = Convert.ToHexString(SHA256.HashData(driverFile));
        if (hash != "C524435027B6C252464DAB58E78CB01F7A09C820DAD870677AA15B62B974B2AF")
            throw new InvalidDataException("Dell SMM driver checksum mismatch.");
        try
        {
            using SafeServiceHandle manager = OpenSCManager(null, null, 3);
            if (manager.IsInvalid)
                ThrowWin32("Cannot access Service Control Manager. Run the application as administrator");
            _service = CreateService(manager, ServiceName, "Dell SMM Fan Control (temporary)", ServiceAccess,
                1, 3, 1, driver, null, 0, null, null, null);
            if (_service.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                _service.Dispose();
                if (error != 1073)
                    throw new Win32Exception(error, $"Cannot create the temporary Dell SMM service (Windows error {error}).");
                _service = OpenService(manager, ServiceName, ServiceAccess);
                if (_service.IsInvalid)
                    ThrowWin32("Cannot open the temporary Dell SMM service");
                VerifyServicePath(_service, driver);
            }
            _ownsService = true;
            if (!StartService(_service, 0, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1056)
                {
                    string hint = error is 577 or 1275
                        ? " Windows is blocking this driver."
                        : "";
                    throw new Win32Exception(error, $"Cannot load Dell SMM (Windows error {error}).{hint}");
                }
            }
            WaitForState(_service, 4);
            _device = CreateFile(@"\\.\BZHDELLSMMIO", 0xC0000000, 0, 0, 3, 0, 0);
            if (_device.IsInvalid)
                ThrowWin32("Cannot connect to Dell SMM");
            if (!HasDellSignature(0xFEA3) && !HasDellSignature(0xFFA3))
                throw new InvalidDataException("BIOS did not confirm Dell SMM support.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public uint QueryFanRpm(int fanIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fanIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fanIndex, 1);
        return Query(DellFanMonitor.GetFanRpmCommand, (uint)fanIndex).Command;
    }

    public int QueryFanLevel(int fanIndex) => (int)(QueryValue(0x00A3, (uint)fanIndex) & 0xff);

    public int QueryNominalFanRpm(int fanIndex, int level) =>
        (int)(QueryValue(0x04A3, (uint)(fanIndex | (level << 8))) & 0xffff);

    public int QuerySensorType(int sensorIndex) => (int)(QueryValue(0x11A3, (uint)sensorIndex) & 0xff);

    public int QueryTemperature(int sensorIndex)
    {
        int temperature = (int)(QueryValue(0x10A3, (uint)sensorIndex) & 0xff);
        if (temperature is < 1 or > 127)
        {
            // Dell firmware can return a transient invalid value (notably 0x99).
            // dell-smm-hwmon also waits 100 ms and retries that response once.
            Thread.Sleep(100);
            temperature = (int)(QueryValue(0x10A3, (uint)sensorIndex) & 0xff);
        }
        if (temperature is < 1 or > 127)
            throw new InvalidDataException($"BIOS did not return a valid CPU temperature (sensor {sensorIndex}, value 0x{temperature:X2}).");
        return temperature;
    }

    public void TakeFanControl()
    {
        if (_restoreBiosControl)
            return;
        // Mark before the call: even an ambiguous failure must restore BIOS control.
        _restoreBiosControl = true;
        QueryValue(0x30A3, 0);
    }

    public void SetFanLevel(int fanIndex, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fanIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fanIndex, 1);
        // UI states 1..3 map to firmware levels 0..2. Level 3 is not a speed.
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 2);
        // Manual Min is allowed even when firmware cannot describe level 0.
        // The controller checks actual rotation and selects Auto's minimum.
        QueryValue(0x01A3, (uint)(fanIndex | (level << 8)));
        // Some Dell desktops reject Get Fan State (0x00A3) while accepting
        // Set Fan State. Verify the effect using measured RPM in the controller.
    }

    public void RestoreBiosControl()
    {
        if (!_restoreBiosControl)
            return;
        QueryValue(0x31A3, 0);
        _restoreBiosControl = false;
    }

    private uint QueryValue(uint command, uint data)
    {
        uint result = Query(command, data).Command;
        if (result == command || (result & 0xffff) == 0xffff)
            throw new InvalidDataException($"BIOS does not support command 0x{command:X4} (channel {data}).");
        return result;
    }

    private bool HasDellSignature(uint command)
    {
        try
        {
            SmmPacket result = Query(command, 0);
            return result.Command == 0x44494147 && result.Status2 == 0x44454C4C;
        }
        catch (Win32Exception) { return false; }
    }

    private SmmPacket Query(uint command, uint data)
    {
        ObjectDisposedException.ThrowIf(_device is null || _device.IsClosed, this);
        // The Dell legacy SMM interface must be called on CPU 0.
        nint thread = GetCurrentThread();
        nuint previousAffinity = SetThreadAffinityMask(thread, 1);
        if (previousAffinity == 0)
            ThrowWin32("Cannot select CPU 0 for Dell SMM");
        try
        {
            var input = new SmmPacket { Command = command, Data = data };
            if (!DeviceIoControl(_device, Ioctl, ref input, 16, out SmmPacket output, 16, out uint count, 0))
                ThrowWin32($"Cannot read Dell SMM 0x{command:X4}, channel {data}");
            if (count != 16)
                throw new InvalidDataException($"Dell SMM driver returned {count} bytes instead of 16.");
            return output;
        }
        finally
        {
            if (SetThreadAffinityMask(thread, previousAffinity) == 0)
                ThrowWin32("Cannot restore thread CPU affinity");
        }
    }

    private static void VerifyServicePath(SafeServiceHandle service, string expectedPath)
    {
        QueryServiceConfig(service, 0, 0, out uint size);
        if (size == 0 || size > 65536)
            throw new InvalidOperationException("Cannot verify the existing DellFanControlSmm service.");
        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!QueryServiceConfig(service, buffer, size, out _))
                ThrowWin32("Cannot verify the temporary service path");
            var config = Marshal.PtrToStructure<ServiceConfig>(buffer);
            string? actualPath = Marshal.PtrToStringUni(config.BinaryPath);
            if (config.ServiceType != 1 || config.StartType != 3 ||
                !string.Equals(actualPath?.Trim('"'), expectedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Another service is using the DellFanControlSmm name. That service will not be changed.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void WaitForState(SafeServiceHandle service, uint state)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            if (!QueryServiceStatus(service, out ServiceStatus status))
                ThrowWin32("Cannot query Dell SMM service status");
            if (status.State == state)
                return;
            if (timer.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Timed out waiting for the Dell SMM driver.");
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        if (_restoreBiosControl && _device is { IsInvalid: false, IsClosed: false })
        {
            try { RestoreBiosControl(); }
            catch (Exception error)
            {
                // Keep cooling at maximum if the firmware cannot resume control.
                for (int index = 0; index < 2; index++)
                {
                    try { SetFanLevel(index, 2); }
                    catch (Exception) { }
                }
                CleanupError = $"Failed to restore automatic firmware control: {error.Message}";
            }
        }
        _device?.Dispose();
        _device = null;
        if (_ownsService && _service is { IsInvalid: false, IsClosed: false })
        {
            try
            {
                if (!ControlService(_service, 1, out _))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 1062)
                        throw new Win32Exception(error, "Cannot unload the Dell SMM driver.");
                }
                WaitForState(_service, 1);
            }
            catch (Exception error) { CleanupError = string.Join(" ", CleanupError, error.Message).Trim(); }
            if (!DeleteService(_service))
                CleanupError = (CleanupError + " Cannot remove the temporary Dell SMM service.").Trim();
            _ownsService = false;
        }
        _service?.Dispose();
        _service = null;
    }

    private static void ThrowWin32(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        throw new Win32Exception(error, $"{operation} (Windows error {error}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmmPacket { public uint Command, Data, Status1, Status2; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public uint Type, State, Accepted, Win32Exit, ServiceExit, Checkpoint, WaitHint; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceConfig
    {
        public uint ServiceType, StartType, ErrorControl;
        public nint BinaryPath, LoadOrderGroup;
        public uint TagId;
        public nint Dependencies, StartName, DisplayName;
    }
    private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle CreateService(SafeServiceHandle manager, string name, string displayName,
        uint access, uint type, uint startType, uint errorControl, string binaryPath, string? group,
        nint tagId, string? dependencies, string? account, string? password);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(SafeServiceHandle service, uint count, nint arguments);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(SafeServiceHandle service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(SafeServiceHandle service, out ServiceStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(SafeServiceHandle service, nint config, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(SafeServiceHandle service);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint mode, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref SmmPacket input, uint inputSize,
        out SmmPacket output, uint outputSize, out uint count, nint overlapped);
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint SetThreadAffinityMask(nint thread, nuint mask);
}
