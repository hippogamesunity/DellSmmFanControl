using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DellSmmFanControl.SystemIntegration;

// The event retains a request made while the first instance is still opening.
internal sealed class InstanceActivation : IDisposable
{
    private readonly EventWaitHandle _showWindow = new(false, EventResetMode.AutoReset,
        @"Global\DellFanControlPortable.ShowWindow");
    private RegisteredWaitHandle? _registration;

    public void RequestShowWindow()
    {
        // The newly launched process can pass its foreground permission to the
        // existing process before exiting, so Activate can bring its window forward.
        using Process current = Process.GetCurrentProcess();
        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id != current.Id && process.SessionId == current.SessionId)
                        AllowSetForegroundWindow((uint)process.Id);
                }
                catch (InvalidOperationException) { } // The other process exited.
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        _showWindow.Set();
    }

    public void Listen(MainWindows window)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_showWindow, (_, _) =>
        {
            try
            {
                if (!window.IsDisposed && !window.Disposing)
                    window.BeginInvoke((Action)window.ShowFromAnotherInstance);
            }
            catch (InvalidOperationException) { } // The window closed during dispatch.
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showWindow.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
