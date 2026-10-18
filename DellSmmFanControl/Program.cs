using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using DellSmmFanControl.SystemIntegration;

namespace DellSmmFanControl;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        // Let the debugger start the process normally; request elevation ourselves.
        // A requireAdministrator manifest prevents a non-elevated IDE from starting it.
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return RestartElevated(args);

        using var instance = new Mutex(true, @"Global\DellFanControlPortable", out bool firstInstance);
        using var activation = new InstanceActivation();
        if (!firstInstance)
        {
            if (args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
                return 0;
            activation.RequestShowWindow();
            return 0;
        }
        try
        {
            ApplicationConfiguration.Initialize();
            using var window = new MainWindows(startMinimized: args.Contains("--minimized", StringComparer.OrdinalIgnoreCase));
            // Subscribe after the form's own Shown handler, which may hide a
            // startup window in the tray. Pending activation then restores it.
            window.Shown += (_, _) => activation.Listen(window);
            Application.Run(window);
            return 0;
        }
        finally { instance.ReleaseMutex(); }
    }

    private static int RestartElevated(string[] args)
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the application path.");
            // Some IDE debug configurations start a DLL through the dotnet host.
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                executable = Path.Combine(AppContext.BaseDirectory, "DellSmmFanControl.exe");

            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (string argument in args)
                start.ArgumentList.Add(argument);
            using Process elevated = Process.Start(start)
                ?? throw new InvalidOperationException("Cannot start the application as administrator.");
            return 0;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            MessageBox.Show("Launch canceled. Administrator permission is required to access Dell fan controls.",
                "Dell SMM Fan Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
        catch (Exception error)
        {
            string message = error is Win32Exception windowsError
                ? $"Windows could not start the application (error {windowsError.NativeErrorCode})."
                : error.Message;
            MessageBox.Show(message, "Dell SMM Fan Control: launch error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
