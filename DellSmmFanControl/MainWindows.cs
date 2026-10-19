using DellSmmFanControl.Hardware;
using DellSmmFanControl.Settings;
using DellSmmFanControl.SystemIntegration;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace DellSmmFanControl;

public partial class MainWindows : Form
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _controlChanged = new(0, 1);
    private Task _monitoring = Task.CompletedTask;
    private bool _closing;
    private bool _canClose;
    private int _requestRevision;
    private readonly StartupRegistration? _startupRegistration;
    private readonly UserPreferences? _preferences;
    private readonly bool _startMinimized;
    private bool _startupEnabled;
    private bool _hasBeenShown;
    private bool _changingTrayVisibility;
    private bool _powerEventsSubscribed;
    private int _monitorRestartRequested;
    private NotifyIcon _trayIcon = null!;
    private FanSnapshot? _latestSnapshot;
    private FanControlResult? _latestControl;
    private int? _maximumCpuTemperature;

    public MainWindows() : this(startMinimized: false) { }

    public MainWindows(bool startMinimized)
    {
        _startMinimized = startMinimized;
        InitializeComponent();
        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime || DesignMode)
            return;

        subtitle.Text = ComputerInfo.Summary;
        controlToolTip.SetToolTip(subtitle, subtitle.Text);
        cpuTitleLabel.Text = CpuInfo.TemperatureTitle;
        controlToolTip.SetToolTip(cpuTitleLabel, cpuTitleLabel.Text);
        UpdateFanCards(null);
        _startupRegistration = new StartupRegistration();
        _preferences = new UserPreferences();
        using (Stream? iconStream = typeof(MainWindows).Assembly.GetManifestResourceStream("DellSmmFanControl.FanIcon"))
        {
            if (iconStream is not null)
            {
                using var icon = new Icon(iconStream);
                _applicationIcon = (Icon)icon.Clone();
                Icon = _applicationIcon;
            }
        }
        InitializeTray();
        if (_startMinimized)
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
        }
        Resize += (_, _) =>
        {
            if (_hasBeenShown && !_changingTrayVisibility && WindowState == FormWindowState.Minimized)
                HideToTray();
        };
        startupCheckBox.Click += async (_, _) => await ChangeStartupAsync();
        foreach (RadioButton button in new[] { mode1Button, mode2Button, mode3Button, autoModeButton })
        {
            button.Click += (_, _) =>
            {
                if (_closing)
                    return;
                _requestRevision++;
                if (_controlChanged.CurrentCount == 0)
                    _controlChanged.Release();
                SaveSelectedMode();
                UpdateTrayText();
            };
        }
        Shown += async (_, _) =>
        {
            _hasBeenShown = true;
            if (!_powerEventsSubscribed)
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                _powerEventsSubscribed = true;
            }
            RestoreSelectedMode();
            _trayIcon.Visible = true;
            if (_startMinimized)
                HideToTray();
            StartMonitoring();
            await LoadStartupAsync();
        };
    }

    private void InitializeTray()
    {
        var menu = new ContextMenuStrip(components!);
        menu.Items.Add("Open Dell SMM Fan Control", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        _trayIcon = new NotifyIcon(components!)
        {
            Icon = _applicationIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = false
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                RestoreFromTray();
        };
        UpdateTrayText();
    }

    private void HideToTray()
    {
        if (_closing || _changingTrayVisibility)
            return;
        _changingTrayVisibility = true;
        try
        {
            _trayIcon.Visible = true;
            Hide();
            ShowInTaskbar = false;
        }
        finally { _changingTrayVisibility = false; }
    }

    internal void ShowFromAnotherInstance() => RestoreFromTray();

    private void RestoreFromTray()
    {
        if (_closing || _changingTrayVisibility)
            return;
        _changingTrayVisibility = true;
        try
        {
            // ShowInTaskbar recreates the window handle and can raise Resize with
            // the previous minimized state. Do not hide the window again mid-restore.
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }
        finally { _changingTrayVisibility = false; }
    }

    protected override void WndProc(ref Message message)
    {
        const int wmSysCommand = 0x0112;
        const long scMinimize = 0xF020;
        // Treat taskbar minimize requests as activation, even when we already
        // have focus. Caption-button clicks still perform normal minimization.
        if (message.Msg == wmSysCommand && (message.WParam.ToInt64() & 0xFFF0) == scMinimize &&
            _hasBeenShown && !_closing && !_changingTrayVisibility &&
            WindowState != FormWindowState.Minimized &&
            (GetForegroundWindow() != message.HWnd ||
                (message.LParam == 0 && IsPointerOverTaskbar())))
        {
            message.Result = 0;
            // Restore outside WndProc: ShowInTaskbar may recreate the handle.
            BeginInvoke((Action)RestoreFromTray);
            return;
        }
        base.WndProc(ref message);
    }

    private static bool IsPointerOverTaskbar()
    {
        // GA_ROOT resolves XAML/task-list child windows to their taskbar.
        nint root = GetAncestor(WindowFromPoint(Cursor.Position), 2);
        var className = new System.Text.StringBuilder(64);
        return GetClassName(root, className, className.Capacity) > 0 &&
            className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder className, int capacity);

    private FanMode SelectedMode => autoModeButton.Checked ? FanMode.Auto
        : mode1Button.Checked ? FanMode.Min : mode2Button.Checked ? FanMode.Normal : FanMode.Max;

    private void RestoreSelectedMode()
    {
        FanMode mode = _preferences?.LoadMode() ?? FanMode.Auto;
        RadioButton button = mode switch
        {
            FanMode.Min => mode1Button, FanMode.Normal => mode2Button,
            FanMode.Max => mode3Button, _ => autoModeButton
        };
        button.Checked = true;
        UpdateTrayText();
    }

    private void SaveSelectedMode()
    {
        try { _preferences?.SaveMode(SelectedMode); }
        catch (Exception error)
        {
            MessageBox.Show(this, $"Cannot save the selected fan mode: {error.Message}",
                "Dell SMM Fan Control: settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadStartupAsync()
    {
        if (_startupRegistration is null)
            return;
        try
        {
            bool enabled = await Task.Run(_startupRegistration.IsEnabled);
            if (_closing || IsDisposed)
                return;
            _startupEnabled = enabled;
            startupCheckBox.Checked = enabled;
            controlToolTip.SetToolTip(startupCheckBox, "Start in the notification area when you sign in to Windows.");
        }
        catch (Exception error)
        {
            if (!_closing && !IsDisposed)
                controlToolTip.SetToolTip(startupCheckBox,
                    $"Cannot read startup settings: {error.Message} (0x{error.HResult:X8}). Click to try enabling startup.");
        }
        finally
        {
            // A failed status query must not permanently disable the control.
            if (!_closing && !IsDisposed)
                startupCheckBox.Enabled = true;
        }
    }

    private async Task ChangeStartupAsync()
    {
        if (_startupRegistration is null || _closing)
            return;
        bool enabled = startupCheckBox.Checked;
        startupCheckBox.Enabled = false;
        try
        {
            await Task.Run(() => _startupRegistration.SetEnabled(enabled));
            _startupEnabled = enabled;
            if (_closing || IsDisposed)
                return;
            controlToolTip.SetToolTip(startupCheckBox, "Start in the notification area when you sign in to Windows.");
        }
        catch (Exception error)
        {
            if (!_closing && !IsDisposed)
            {
                startupCheckBox.Checked = _startupEnabled;
                MessageBox.Show(this, $"Cannot update startup settings: {error.Message} (0x{error.HResult:X8})",
                    "Dell SMM Fan Control: startup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (!_closing && !IsDisposed)
                startupCheckBox.Enabled = true;
        }
    }

    private void UpdateTrayText()
    {
        string mode = SelectedMode.ToString();
        if (_latestControl is { CanControl: false })
            mode += " (inactive)";
        else if (_latestControl?.Message.StartsWith("Maximum cooling", StringComparison.Ordinal) == true)
            mode = "Max (overheat protection)";
        else if (SelectedMode == FanMode.Auto && _latestControl is not null)
        {
            foreach (string state in new[] { "Min", "Normal", "Max" })
            {
                if (_latestControl.Message.Contains($"· {state}", StringComparison.Ordinal))
                {
                    mode += $" ({state})";
                    break;
                }
            }
        }
        string fanSpeeds = string.Join(" · ", (_latestSnapshot?.Fans ?? [])
            .Where(fan => fan.Rpm is > 0).OrderBy(fan => fan.Index).Take(2)
            .Select(fan => $"Fan {fan.Index + 1}: {fan.Rpm:N0} RPM"));
        string temperature = _latestSnapshot?.CpuTemperature is int value ? value.ToString() : "—";
        string text = $"Dell SMM Fan Control\nMode: {mode}\n" +
            (fanSpeeds.Length > 0 ? fanSpeeds + "\n" : "") + $"CPU: {temperature} °C";
        _trayIcon.Text = text.Length <= 127 ? text : text[..127];
    }

    private void StartMonitoring()
    {
        if (!_monitoring.IsCompleted || _closing)
            return;
        _monitoring = MonitorAsync(_shutdown.Token);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || _closing || IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke((Action)(() =>
            {
                if (_closing)
                    return;
                Interlocked.Exchange(ref _monitorRestartRequested, 1);
                if (_controlChanged.CurrentCount == 0)
                    _controlChanged.Release();
                StartMonitoring();
            }));
        }
        catch (InvalidOperationException) when (_closing || IsDisposed || !IsHandleCreated) { }
    }

    private void UnsubscribePowerEvents()
    {
        if (!_powerEventsSubscribed)
            return;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _powerEventsSubscribed = false;
    }

    private FanControlRequest ReadControlRequest() => new(autoModeButton.Checked,
        mode1Button.Checked ? 0 : mode2Button.Checked ? 1 : 2, _requestRevision);

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        ClearReadings();
        DellFanMonitor? monitor = null;
        FanController? controller = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool reconnect = Interlocked.Exchange(ref _monitorRestartRequested, 0) != 0;
                if (controller is null || reconnect)
                {
                    if (controller is not null)
                    {
                        // Serialize disposal and reconnection with polling: never close
                        // the driver's handle while a hardware call is in flight.
                        FanController previousController = controller;
                        DellFanMonitor? previousMonitor = monitor;
                        controller = null;
                        monitor = null;
                        await Task.Run(previousController.Dispose);
                        if (previousMonitor?.CleanupError is string error)
                            throw new InvalidOperationException(error);
                    }
                    ClearReadings();
                    controlModeLabel.Text = reconnect ? "Reconnecting after sleep…" : "Connecting…";
                    try
                    {
                        monitor = await Task.Run(() => new DellFanMonitor(), cancellationToken);
                        controller = new FanController(monitor);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        controlModeLabel.Text = $"Cannot connect to Dell BIOS · {error.Message} · retrying…";
                        controlModeLabel.ForeColor = Color.Firebrick;
                        controlToolTip.SetToolTip(controlModeLabel, controlModeLabel.Text);
                        await _controlChanged.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                        continue;
                    }
                }
                FanControlRequest request = ReadControlRequest();
                FanControlResult result = await Task.Run(() => controller.Tick(request), cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                    break;
                if (Volatile.Read(ref _monitorRestartRequested) != 0)
                    continue;
                RenderSnapshot(result.Snapshot);
                RenderControl(result);
                await _controlChanged.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            ClearReadings();
            mode1Button.Enabled = mode2Button.Enabled = mode3Button.Enabled = autoModeButton.Enabled = false;
            controlModeLabel.Text = $"Cannot communicate with Dell BIOS · {error.Message}";
            controlModeLabel.ForeColor = Color.Firebrick;
            controlToolTip.SetToolTip(controlModeLabel, controlModeLabel.Text);
        }
        finally
        {
            await Task.Run(() =>
            {
                if (controller is not null)
                    controller.Dispose();
                else
                    monitor?.Dispose();
            });
            if (monitor?.CleanupError is string cleanupError)
            {
                MessageBox.Show(cleanupError, "Dell SMM Fan Control: driver cleanup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void RenderSnapshot(FanSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        UpdateFanCards(snapshot);
        cpuTemperatureLabel.Text = snapshot.CpuTemperature is int temperature ? $"{temperature} °C" : "— °C";
        if (snapshot.CpuTemperature is int currentTemperature &&
            (_maximumCpuTemperature is null || currentTemperature > _maximumCpuTemperature))
        {
            _maximumCpuTemperature = currentTemperature;
            cpuMaximumTemperatureLabel.Text = $"max {currentTemperature}°";
        }
        controlToolTip.SetToolTip(cpuTemperatureLabel, snapshot.TemperatureError);
        UpdateTrayText();
    }

    private void RenderControl(FanControlResult result)
    {
        _latestControl = result;
        // Visual speeds: 1 / 2 / 3 revolutions per second, converted to degrees.
        foreach (Controls.FanRpmLabel label in new[] { fan1RpmLabel, fan2RpmLabel })
        {
            // A temperature/control error does not mean the physical fan stopped.
            FanReading? reading = result.Snapshot.Fans.FirstOrDefault(fan =>
                fan.Index == (label == fan1RpmLabel ? 0 : 1));
            int? level = result.CanControl ? result.AppliedLevel : null;
            level ??= reading?.Percent switch { >= 70 => 2, >= 30 => 1, _ => 0 };
            label.RotationDegreesPerSecond = level switch { 0 => 360, 1 => 720, _ => 1080 };
        }
        // A failed mode must not prevent selecting another mode or retrying it.
        mode1Button.Enabled = mode2Button.Enabled = mode3Button.Enabled = autoModeButton.Enabled = !_closing;
        controlToolTip.SetToolTip(mode1Button, result.LowestLevelAvailable
            ? "Minimum speed" : "Minimum speed · unconfirmed by BIOS; Auto uses Normal instead");
        controlModeLabel.Text = result.Message;
        controlModeLabel.ForeColor = result.CanControl && result.Snapshot.CpuTemperature.HasValue ? Color.DimGray : Color.Firebrick;
        controlToolTip.SetToolTip(controlModeLabel, result.Message);
        UpdateTrayText();
    }

    private void ClearReadings()
    {
        _latestSnapshot = null;
        _latestControl = null;
        fan1RpmLabel.Text = fan2RpmLabel.Text = "— RPM";
        UpdateFanCards(null);
        cpuTemperatureLabel.Text = "— °C";
        UpdateTrayText();
    }

    private void UpdateFanCards(FanSnapshot? snapshot)
    {
        Controls.FanRpmLabel[] labels = [fan1RpmLabel, fan2RpmLabel];
        Label[] percentLabels = [fan1PercentLabel, fan2PercentLabel];
        fanCardsLayout.SuspendLayout();
        try
        {
            for (int index = 0; index < labels.Length; index++)
            {
                FanReading? fan = snapshot?.Fans.FirstOrDefault(fan => fan.Index == index);
                bool running = fan?.Rpm is > 0;
                labels[index].Rpm = fan?.Rpm;
                percentLabels[index].Text = fan?.Percent is int percent ? $"{percent}%" : "—%";
                labels[index].Parent!.Visible = running;
                fanCardsLayout.ColumnStyles[index * 2].Width = running ? 100F : 0F;
                fanCardsLayout.ColumnStyles[index * 2 + 1].Width = running ? 8F : 0F;
            }
            // Equal-width cards share the space left after fixed 8-pixel gaps.
            // Both a hidden fan's column and its gap collapse to zero.
            fanCardsLayout.ColumnStyles[4].Width = 100F;
        }
        finally { fanCardsLayout.ResumeLayout(true); }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime || DesignMode)
        {
            base.OnFormClosing(e);
            return;
        }
        if (!_canClose)
        {
            e.Cancel = true;
            base.OnFormClosing(e);
            if (_closing)
                return;
            _closing = true;
            UnsubscribePowerEvents();
            _trayIcon.Visible = false;
            _shutdown.Cancel();
            await _monitoring;
            await Task.Yield();
            _canClose = true;
            Close();
            return;
        }
        base.OnFormClosing(e);
    }
}
