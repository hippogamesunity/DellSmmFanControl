using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace DellSmmFanControl.SystemIntegration;

internal sealed class StartupRegistration
{
    private const string Source = "DellFanControl.Startup";
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly string _userSid;
    private readonly string _taskName;
    private readonly string _executablePath;

    public StartupRegistration()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        _userSid = identity.User?.Value ?? throw new InvalidOperationException("Cannot identify the current user.");
        _taskName = $"DellFanControl.Startup.{_userSid}";
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the application path.");
        _executablePath = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppContext.BaseDirectory, "DellSmmFanControl.exe") : processPath;
    }

    public bool IsEnabled()
    {
        using var scheduler = new SchedulerConnection();
        dynamic? task = scheduler.FindTask(_taskName);
        if (task is null)
            return false;
        EnsureOwned((string)task.Xml);
        if ((bool)task.Enabled)
        {
            XElement? command = XDocument.Parse((string)task.Xml).Root?
                .Element(TaskNamespace + "Actions")?.Element(TaskNamespace + "Exec")?
                .Element(TaskNamespace + "Command");
            if (!string.Equals(command?.Value, _executablePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Startup points to another location. Enable Startup to update the application path.");
        }
        return (bool)task.Enabled;
    }

    public void SetEnabled(bool enabled)
    {
        using var scheduler = new SchedulerConnection();
        dynamic? existing = scheduler.FindTask(_taskName);
        if (existing is not null)
            EnsureOwned((string)existing.Xml);
        if (!enabled)
        {
            if (existing is not null)
                scheduler.Root.DeleteTask(_taskName, 0);
            return;
        }
        if (!File.Exists(_executablePath))
            throw new FileNotFoundException("The application executable is missing.", _executablePath);
        // TASK_CREATE_OR_UPDATE = 6, TASK_LOGON_INTERACTIVE_TOKEN = 3.
        scheduler.Track(scheduler.Root.RegisterTask(_taskName, BuildTaskXml(_userSid, _executablePath),
            6, _userSid, null, 3, null));
    }

    private void EnsureOwned(string xml)
    {
        XElement task = XDocument.Parse(xml).Root ?? throw new InvalidOperationException("Invalid startup task.");
        string? source = task.Element(TaskNamespace + "RegistrationInfo")?.Element(TaskNamespace + "Source")?.Value;
        string? user = task.Element(TaskNamespace + "Principals")?.Element(TaskNamespace + "Principal")?
            .Element(TaskNamespace + "UserId")?.Value;
        if (source != Source || !string.Equals(user, _userSid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A different task is using the Dell SMM Fan Control startup task name.");
    }

    internal static string BuildTaskXml(string userSid, string executablePath)
    {
        XElement Element(string name, object value) => new(TaskNamespace + name, value);
        var task = new XElement(TaskNamespace + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", new[]
            {
                Element("Source", Source),
                Element("Description", "Starts Dell SMM Fan Control in the notification area when this user signs in.")
            }),
            Element("Triggers", Element("LogonTrigger", new[]
            {
                Element("Enabled", true), Element("UserId", userSid), Element("Delay", "PT10S")
            })),
            Element("Principals", new XElement(TaskNamespace + "Principal", new XAttribute("id", "User"),
                Element("UserId", userSid), Element("LogonType", "InteractiveToken"), Element("RunLevel", "HighestAvailable"))),
            Element("Settings", new[]
            {
                Element("MultipleInstancesPolicy", "IgnoreNew"),
                Element("DisallowStartIfOnBatteries", false), Element("StopIfGoingOnBatteries", false),
                Element("AllowHardTerminate", false), Element("StartWhenAvailable", true),
                Element("RunOnlyIfNetworkAvailable", false), Element("AllowStartOnDemand", true),
                Element("Enabled", true), Element("Hidden", false), Element("RunOnlyIfIdle", false),
                Element("WakeToRun", false), Element("ExecutionTimeLimit", "PT0S"), Element("Priority", 7),
                Element("RestartOnFailure", new[] { Element("Interval", "PT1M"), Element("Count", 3) })
            }),
            new XElement(TaskNamespace + "Actions", new XAttribute("Context", "User"),
                Element("Exec", new[]
                {
                    Element("Command", executablePath), Element("Arguments", "--startup --minimized"),
                    Element("WorkingDirectory", Path.GetDirectoryName(executablePath)!)
                })));
        return new XDocument(new XDeclaration("1.0", "utf-16", null), task).ToString();
    }

    private sealed class SchedulerConnection : IDisposable
    {
        private readonly List<object> _objects = [];
        public dynamic Root { get; }

        public SchedulerConnection()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
                dynamic service = Track(Activator.CreateInstance(type)!);
                service.Connect();
                Root = Track(service.GetFolder("\\"));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public dynamic Track(object value)
        {
            if (!_objects.Any(existing => ReferenceEquals(existing, value)))
                _objects.Add(value);
            return value;
        }

        public dynamic? FindTask(string name)
        {
            try { return Track(Root.GetTask(name)); }
            // COM interop can map these HRESULTs to FileNotFoundException or
            // DirectoryNotFoundException, rather than leaving them as COMException.
            catch (Exception error) when (error.HResult is unchecked((int)0x80070002) or unchecked((int)0x80070003))
            {
                return null;
            }
        }

        public void Dispose()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (Marshal.IsComObject(_objects[index]))
                    Marshal.FinalReleaseComObject(_objects[index]);
            }
            _objects.Clear();
        }
    }
}
