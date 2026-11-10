using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Collections.Concurrent;
using ScrollWM.Core.Layout;
using ScrollWM.Core.Management;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using ScrollWM.Windows;
using ScrollWM.Windows.Recovery;

namespace ScrollWM.App;

public partial class MainWindow : Window
{
    private readonly NativeWindowSystem native = new();
    private readonly JsonRecoveryStore store;
    private readonly RecoverySession recovery;
    private readonly StripSession stripSession;
    private readonly DispatcherTimer displayTimer;
    private readonly DispatcherTimer eventTimer;
    private readonly ConcurrentQueue<DesktopWindowEvent> pendingEvents = [];
    private NativeWindowEvents? nativeEvents;
    private int eventScheduled;
    private WindowSnapshot[] active = [];
    private EmergencyShortcut? shortcut;
    private StripShortcuts? stripShortcuts;
    private HwndSource? source;
    private bool exitKeepingRecovery;
    private bool busy;
    private int stripOperations;
    private bool StripBusy => stripOperations > 0;
    private bool closingAfterRestore;
    private readonly Dictionary<WindowIdentity, WindowOption> windowNames = [];
    private readonly Dictionary<string, int> windowNumbers = [];
    private string? stripMessage;
    private bool stripMessageIsError;
    private bool updatingMonitors;
    private sealed record MonitorOption(string Id, string Label, string Details)
    {
        public override string ToString() => Label;
    }
    private sealed record WindowOption(WindowIdentity Identity, string Name, string Details)
    {
        public override string ToString() => Name;
    }
    private sealed record StripRow(string Name, string WidthLabel, bool Focused, string Details)
    {
        public override string ToString() => (Focused ? "Focused: " : "") + Name + " · " + WidthLabel;
    }

    public MainWindow(string dataDirectory)
    {
        InitializeComponent();
        store = new(dataDirectory);
        recovery = new(store, native);
        stripSession = new(native, store);
        PositionBox.ItemsSource = Enum.GetValues<ProbePosition>();
        PositionBox.SelectedItem = ProbePosition.Center;
        WidthBox.ItemsSource = Enum.GetValues<ColumnWidth>();
        WidthBox.SelectedItem = ColumnWidth.Half;
        displayTimer = new(TimeSpan.FromSeconds(2), DispatcherPriority.Background,
            (_, _) => CheckDisplay(), Dispatcher);
        eventTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
        eventTimer.Tick += FlushWindowEvents;
        SourceInitialized += InitializeShortcut;
        Loaded += (_, _) => Run(RenderStrip);
        Closing += RestoreOnClosing;
        Closed += (_, _) =>
        {
            displayTimer.Stop();
            eventTimer.Stop();
            nativeEvents?.Dispose();
            shortcut?.Dispose();
            stripShortcuts?.Dispose();
            source?.RemoveHook(WindowMessage);
        };
        Run(() =>
        {
            Refresh();
            RefreshStripCandidates();
            RenderStrip();
            StatusText.Text = recovery.Pending is { Windows.Length: > 0 }
                ? "A previous experiment has pending recovery. Choose Restore all before capturing new windows."
                : native.GetDisplays().Count != 1
                    ? "Capture requires exactly one active display. Restore all remains available."
                    : "Ready. Select windows and start a strip, or use position probes.";
        });
    }

    private void InitializeShortcut(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source.AddHook(WindowMessage);
        Run(() =>
        {
            shortcut = new EmergencyShortcut(source.Handle);
            nativeEvents = new NativeWindowEvents(OnWindowEvent);
            Refresh();
            RenderStrip();
        });
    }

    private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == EmergencyShortcut.Message && wParam.ToInt32() == EmergencyShortcut.Id)
        {
            Dispatcher.BeginInvoke(async () => await RunStripAsync(async () =>
            {
                await stripSession.PauseAsync();
                active = [];
                stripShortcuts?.Dispose();
                stripShortcuts = null;
                Refresh();
                ShowRestoreResult("Strip shortcuts are disabled.");
            }));
            handled = true;
        }

        else if (message is 0x02E0 or 0x007E or 0x0219 or 0x001A && stripSession.Running)
        {
            _ = Dispatcher.BeginInvoke(async () => await RunStripAsync(() => ReconcileWindowEvents(null), background: true));
        }

        else if (message == EmergencyShortcut.Message && Enum.IsDefined(typeof(StripShortcut), wParam.ToInt32()))
        {
            var command = (StripShortcut)wParam.ToInt32();
            Dispatcher.BeginInvoke(async () => await RunStripAsync(() => HandleShortcutAsync(command)));
            handled = true;
        }

        return 0;
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => Run(Refresh);

    private void Refresh()
    {
        var displays = native.GetDisplays();
        DisplayText.Text = $"{displays.Count} active display(s) · Work area {displays.WorkArea.Width} × {displays.WorkArea.Height} px";
        if (active.Length == 0)
        {
            WindowList.ItemsSource = native.EnumerateCandidates();
        }

        CaptureButton.IsEnabled = !stripSession.Running && !StripBusy && displays.Count == 1 && active.Length == 0 && shortcut is not null;
        ProbeButton.IsEnabled = active.Length > 0;
        RevealButton.IsEnabled = active.Length > 0;
    }

    private void CaptureClick(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (stripSession.Running || StripBusy)
        {
            throw new InvalidOperationException("Pause the scrolling strip before using position probes.");
        }
        if (shortcut is null)
        {
            throw new InvalidOperationException("The emergency shortcut is unavailable. Close the conflicting app and restart the experiment.");
        }

        native.RequireSingleDisplay();
        var selected = WindowList.SelectedItems.Cast<WindowCandidate>().ToArray();
        active = recovery.Begin(selected.Select(item => item.Identity));
        WindowList.ItemsSource = selected;
        CaptureButton.IsEnabled = false;
        ProbeButton.IsEnabled = true;
        RevealButton.IsEnabled = true;
        StatusText.Text = $"Captured {active.Length} window(s). Original placements were saved before any movement. Select one window and run a probe.";
    });

    private async void ProbeClick(object sender, RoutedEventArgs e)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        ProbeButton.IsEnabled = false;
        try
        {
            var snapshot = SelectedSnapshot();
            var area = native.GetDisplays().WorkArea;
            var width = ColumnWidths.ResolvePixels((ColumnWidth)WidthBox.SelectedItem, area.Width);
            var target = ProbeLayout.Target(area, width, (ProbePosition)PositionBox.SelectedItem);
            native.Move(snapshot, target);
            await Task.Delay(200);
            if (IsLoaded && active.Contains(snapshot))
            {
                var actual = native.ReadFrame(snapshot.Identity.Handle);
                StatusText.Text = $"Requested: {target}\nObserved: {actual}\nWindows or the app may constrain the requested bounds. Restore all remains available.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally
        {
            busy = false;
            ProbeButton.IsEnabled = active.Length > 0;
        }
    }

    private void RevealClick(object sender, RoutedEventArgs e) => Run(() =>
    {
        var snapshot = SelectedSnapshot();
        var area = native.GetDisplays().WorkArea;
        var current = native.ReadFrame(snapshot.Identity.Handle);
        native.Move(snapshot, ProbeLayout.Target(area, current.Width, ProbePosition.Center));
        StatusText.Text = native.Focus(snapshot)
            ? "Window revealed; Windows accepted the focus request."
            : "Window revealed. Windows declined the focus request; use Alt+Tab to focus it.";
    });

    private WindowSnapshot SelectedSnapshot()
    {
        if (WindowList.SelectedItems.Count != 1 || WindowList.SelectedItem is not WindowCandidate candidate)
        {
            throw new InvalidOperationException("Select exactly one captured window for this probe.");
        }

        return active.SingleOrDefault(item => item.Identity == candidate.Identity)
            ?? throw new InvalidOperationException("Capture the selected windows before running a probe.");
    }

    private async void RestoreClick(object sender, RoutedEventArgs e) => await RunStripAsync(async () =>
    {
        await stripSession.PauseAsync();
        active = [];
        Refresh();
        ShowRestoreResult();
        StatusText.Text = stripSession.Status;
    });

    private void Restore()
    {
        var result = recovery.RestoreAll();
        active = [];
        Refresh();
        StatusText.Text = $"Restored {result.Restored}; closed/replaced {result.ClosedOrReused}; pending {result.Remaining}.";
        if (result.Errors.Length > 0)
        {
            StatusText.Text += "\n" + string.Join("\n", result.Errors);
        }
    }

    private async void CheckDisplay()
    {
        if (stripSession.Running && !StripBusy)
        {
            await RunStripAsync(() => ReconcileWindowEvents(null), background: true);
            return;
        }
        if (busy || active.Length == 0)
        {
            if (!StripBusy) { Run(UpdateStripReadiness); }
            return;
        }

        Run(() =>
        {
            if (native.GetDisplays().Count != 1)
            {
                Restore();
                StatusText.Text = "Display topology changed. The experiment stopped.\n" + StatusText.Text;
            }
        });
    }

    private async void RestoreOnClosing(object? sender, CancelEventArgs e)
    {
        if (exitKeepingRecovery || closingAfterRestore)
        {
            return;
        }

        e.Cancel = true;
        if (StripBusy)
        {
            SetStripMessage("A strip command is finishing. Close again after it completes.");
            return;
        }

        try
        {
            await stripSession.PauseAsync();
            active = [];
            RenderStrip();
            if (recovery.Pending is { Windows.Length: > 0 })
            {
                e.Cancel = true;
                StatusText.Text += "\nRetry Restore all, or use Exit keeping recovery to leave the journal for the next launch.";
                SetStripMessage("Recovery is pending. Select Restore windows to retry, or use Exit keeping recovery in Position probes.", error: true);
            }
            else
            {
                closingAfterRestore = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }
        catch (Exception exception)
        {
            e.Cancel = true;
            StatusText.Text = exception.Message + "\nUse Exit keeping recovery to preserve the journal and exit.";
            SetStripMessage(StatusText.Text, error: true);
        }
    }

    private void ExitKeepingRecoveryClick(object sender, RoutedEventArgs e)
    {
        exitKeepingRecovery = true;
        Close();
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            SetStripMessage(exception.Message, error: true);
        }
    }

    private WindowCandidate[] AvailableStripCandidates()
    {
        var managed = stripSession.Running
            ? stripSession.AllColumns.Select(column => column.Identity).ToHashSet()
            : [];
        return native.EnumerateCandidates().Where(candidate => !managed.Contains(candidate.Identity)).ToArray();
    }

    private void RefreshStripCandidates()
    {
        StripCandidates.ItemsSource = AvailableStripCandidates().Select(candidate => DescribeWindow(candidate.Identity, candidate.ProcessName)).ToArray();
        StripCandidates.SelectAll();
        UpdateStripReadiness();
    }

    private void StripSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // WPF may raise selection events while the visual tree is initializing.
        if (stripSession is not null) { Run(UpdateStripReadiness); }
    }
    private WindowIdentity[] SelectedStripWindows()
    {
        var selected = StripCandidates.SelectedItems.Cast<WindowOption>().Select(item => item.Identity).ToHashSet();
        return StripCandidates.Items.Cast<WindowOption>().Where(item => selected.Contains(item.Identity))
            .Select(item => item.Identity).ToArray();
    }

    private void RenderStrip()
    {
        if (stripSession.Running && stripSession.Strip is { } strip)
        {
            var managed = stripSession.AllColumns.Select(column => column.Identity).ToHashSet();
            var listed = StripCandidates.Items.Cast<WindowOption>().ToArray();
            if (listed.Any(item => managed.Contains(item.Identity)))
            {
                var selected = StripCandidates.SelectedItems.Cast<WindowOption>().Select(item => item.Identity).ToHashSet();
                StripCandidates.ItemsSource = listed.Where(item => !managed.Contains(item.Identity)).ToArray();
                foreach (var item in StripCandidates.Items.Cast<WindowOption>().Where(item => selected.Contains(item.Identity)))
                { StripCandidates.SelectedItems.Add(item); }
            }
        }
        UpdateStripReadiness();
        AutoEnrollCheck.IsEnabled = !StripBusy && !stripSession.Running;
        var columns = stripSession.Strip?.Columns;
        var rows = columns?.Select(column =>
        {
            var window = DescribeWindow(column.Identity);
            var preset = column.Preset switch
            {
                ColumnWidth.Third => "Third", ColumnWidth.Half => "Half",
                ColumnWidth.TwoThirds => "Two thirds", ColumnWidth.Full => "Full", _ => "Custom",
            };
            return new StripRow(window.Name, $"{column.Width} px · {preset}",
                stripSession.Running && column.Identity == stripSession.Strip!.Focused, window.Details);
        }).ToArray() ?? [];
        if (StripOrder.ItemsSource is not StripRow[] previous || !previous.SequenceEqual(rows)) { StripOrder.ItemsSource = rows; }
        StripHeading.Text = $"Strip · {rows.Length}";
        StripEmpty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        StripEmpty.Text = stripSession.Running ? "No windows on this monitor.\nMove a window here to add it."
            : stripSession.AllColumns.Count > 0 ? "No saved windows on this monitor.\nChoose another monitor to view its strip."
            : "No windows in the strip.\nStart the strip to arrange selected windows.";
        var focused = rows.FirstOrDefault(row => row.Focused);
        FocusHint.Text = !stripSession.Running && rows.Length > 0 ? "Saved widths and order"
            : focused is null ? "No focused window" : $"Focused: {focused.Name}";
        var index = columns?.ToList().FindIndex(column => column.Identity == stripSession.Strip!.Focused) ?? -1;
        var canNavigate = stripSession.Running && !StripBusy && index >= 0;
        PreviousStripButton.IsEnabled = MoveLeftStripButton.IsEnabled = canNavigate && index > 0;
        NextStripButton.IsEnabled = MoveRightStripButton.IsEnabled = canNavigate && index < rows.Length - 1;
        WidthStripButton.IsEnabled = canNavigate;
        TransferMonitorButton.IsEnabled = canNavigate && stripSession.Displays.Count > 1;
        RenderStripMessage();
    }

    private WindowOption DescribeWindow(WindowIdentity identity, string? processName = null)
    {
        if (windowNames.TryGetValue(identity, out var known)) { return known; }
        if (processName is null)
        {
            try { using var process = Process.GetProcessById(identity.ProcessId); processName = process.ProcessName; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { processName = "Window"; }
        }
        var app = processName.ToLowerInvariant() switch
        {
            "chrome" => "Chrome", "msedge" => "Edge", "code" => "Visual Studio Code",
            "notepad" => "Notepad", "explorer" => "File Explorer", "windowsterminal" => "Windows Terminal",
            "scrollwm.app" => "Test window", _ => processName,
        };
        var number = windowNumbers.GetValueOrDefault(app) + 1;
        windowNumbers[app] = number;
        var result = new WindowOption(identity, $"{app} {number}",
            $"{processName} · PID {identity.ProcessId} · HWND {identity.Handle}");
        windowNames.Add(identity, result);
        return result;
    }

    private void SetStripMessage(string? message, bool error = false)
    {
        stripMessage = message;
        stripMessageIsError = error;
        RenderStripMessage();
    }

    private void RenderStripMessage()
    {
        var message = stripMessage ?? (stripSession.Running && stripSession.Status.Contains("Windows declined focus", StringComparison.Ordinal)
            ? "Window revealed. Windows declined focus. Use Alt+Tab to focus it." : null);
        StripStatus.Text = message ?? "";
        StripMessagePanel.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        StripMessagePanel.Background = stripMessageIsError ? new SolidColorBrush(Color.FromRgb(254, 242, 242)) : new SolidColorBrush(Color.FromRgb(241, 245, 249));
        StripStatus.Foreground = stripMessageIsError ? new SolidColorBrush(Color.FromRgb(153, 27, 27)) : new SolidColorBrush(Color.FromRgb(51, 65, 85));
    }

    private void ShowRestoreResult(string? next = null)
    {
        if (stripSession.LastRestore is not { } result) { return; }
        SetStripMessage(result.Remaining > 0
            ? $"Restored {WindowCount(result.Restored)}. {result.Remaining} still need recovery. Select Restore windows to retry."
            : $"Restored {WindowCount(result.Restored)}." + (next is null ? "" : " " + next), result.Remaining > 0);
    }

    private static string WindowCount(int count) => $"{count} {(count == 1 ? "window" : "windows")}";

    private void UpdateStripReadiness()
    {
        var displays = native.GetDisplayConfiguration();
        string? blocked = displays.Count == 0
            ? "No monitor is available. Connect a monitor before starting."
            : active.Length > 0
                    ? "Restore the position probes before starting a strip."
                    : !stripSession.Running && recovery.Pending is { Windows.Length: > 0 }
                        ? "Recovery is pending. Select Restore windows to retry."
                        : shortcut is null || nativeEvents is null || nativeEvents.Failure is not null
                            ? "Shortcuts or window events are unavailable. Restart ScrollWM after resolving the error below."
                            : null;

        var ready = !StripBusy && !stripSession.Running && blocked is null;
        var selected = StripCandidates.SelectedItems.Count;
        var hasPausedStrip = !stripSession.Running && stripSession.AllColumns.Count > 0;
        var topologyMatches = displays.SequenceEqual(stripSession.Displays);
        var pendingRecovery = !stripSession.Running && recovery.Pending is { Windows.Length: > 0 };
        StartStripButton.IsEnabled = ready && selected > 0;
        AddStripButton.IsEnabled = !StripBusy && stripSession.Running && selected > 0;
        AddOpenStripButton.IsEnabled = !StripBusy && stripSession.Running;
        ResumeStripButton.IsEnabled = ready && hasPausedStrip && topologyMatches;
        UpdateMonitorChoices(displays);
        StartStripButton.Visibility = !stripSession.Running && !pendingRecovery ? Visibility.Visible : Visibility.Collapsed;
        ResumeStripButton.Visibility = hasPausedStrip && !pendingRecovery ? Visibility.Visible : Visibility.Collapsed;
        StartStripButton.Style = (Style)FindResource(hasPausedStrip ? typeof(Button) : "PrimaryButton");
        PauseStripButton.Visibility = stripSession.Running || pendingRecovery ? Visibility.Visible : Visibility.Collapsed;
        PauseStripButton.Content = pendingRecovery ? "Restore windows" : "Pause & restore";
        PauseStripButton.IsEnabled = !StripBusy && (stripSession.Running || pendingRecovery);
        var state = stripSession.Running ? "Running" : StripBusy ? "Working" : blocked is not null ? "Blocked" : hasPausedStrip ? "Paused" : "Off";
        StripStateText.Text = state;
        StripStateBadge.Background = state switch
        {
            "Running" => new SolidColorBrush(Color.FromRgb(220, 252, 231)),
            "Blocked" => new SolidColorBrush(Color.FromRgb(254, 243, 199)),
            _ => new SolidColorBrush(Color.FromRgb(241, 245, 249)),
        };
        StripStateText.Foreground = state switch
        {
            "Running" => new SolidColorBrush(Color.FromRgb(22, 101, 52)),
            "Blocked" => new SolidColorBrush(Color.FromRgb(146, 64, 14)),
            _ => new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        };
        ReadinessPanel.Background = !stripSession.Running && blocked is not null
            ? new SolidColorBrush(Color.FromRgb(255, 251, 235)) : new SolidColorBrush(Color.FromRgb(241, 245, 249));
        AvailableHeading.Text = $"Available windows · {StripCandidates.Items.Count}";
        SelectionHint.Text = $"{selected} selected · Ctrl+click to change";
        AvailableEmpty.Visibility = StripCandidates.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AvailableEmpty.Text = stripSession.Running
            ? "No windows to add.\nOpen or restore a window, then refresh."
            : "No eligible windows.\nOpen or restore a normal window, then refresh.";
        StripReadiness.Text = stripSession.Running
            ? stripSession.AutomaticEnrollment ? "New eligible windows are added automatically." : "Add open windows to extend the strip."
            : StripBusy
                ? "A strip command is finishing."
                : blocked is not null
                    ? blocked
                    : hasPausedStrip
                        ? topologyMatches ? "Original positions restored. Select Resume to arrange the strip." : "Monitors changed. Select Start strip to create new strips."
                        : selected > 0
                            ? $"{WindowCount(selected)} selected. Select Start strip to arrange {(selected == 1 ? "it" : "them")}."
                            : StripCandidates.Items.Count == 0
                                ? "Open or restore a normal window, then select Refresh."
                                : "Select windows to arrange, then select Start strip.";
    }

    private void UpdateMonitorChoices(IReadOnlyList<DisplayInfo> current)
    {
        var displays = stripSession.Displays.Count > 0 ? stripSession.Displays : current;
        var options = displays.OrderByDescending(item => item.Primary).ThenBy(item => item.Bounds.Left).ThenBy(item => item.Bounds.Top)
            .Select((item, index) => new MonitorOption(item.Id, $"Monitor {index + 1} · {item.Dpi * 100 / 96}%",
                $"{(item.Primary ? "Primary monitor" : "Secondary monitor")} · {item.WorkArea.Width} × {item.WorkArea.Height} usable pixels")).ToArray();
        updatingMonitors = true;
        try
        {
            if (MonitorBox.ItemsSource is not MonitorOption[] previous || !previous.SequenceEqual(options)) { MonitorBox.ItemsSource = options; }
            MonitorBox.SelectedValue = stripSession.ActiveDisplayId ?? options.FirstOrDefault()?.Id;
            MonitorBox.Visibility = options.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            MonitorBox.IsEnabled = !StripBusy && stripSession.Displays.Count > 1;
            TransferMonitorButton.Visibility = options.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { updatingMonitors = false; }
    }

    private async void MonitorSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updatingMonitors || stripSession is null || MonitorBox.SelectedItem is not MonitorOption option || stripSession.Displays.Count == 0) { return; }
        await RunStripAsync(() => stripSession.SetActiveDisplayAsync(option.Id));
    }

    private async void TransferMonitorClick(object sender, RoutedEventArgs args) =>
        await RunStripAsync(stripSession.TransferFocusedToNextDisplayAsync);

    private async Task RunStripAsync(Func<Task> action, bool background = false)
    {
        if (!background) { SetStripMessage(null); }
        stripOperations++;
        RenderStrip();
        string? failure = null;
        try { await action(); }
        catch (Exception exception) { failure = exception.Message; }
        finally
        {
            stripOperations--;
            if (failure is not null) { SetStripMessage(failure, error: true); StatusText.Text = failure; }
            RenderStrip();
        }
    }

    private async Task HandleShortcutAsync(StripShortcut command)
    {
        if (command == StripShortcut.PauseResume)
        {
            if (stripSession.Running) { await stripSession.PauseAsync(); RefreshStripCandidates(); ShowRestoreResult(); }
            else { await ResumeStripSessionAsync(); }
            return;
        }

        if (!stripSession.Running) { return; }
        switch (command)
        {
            case StripShortcut.Previous: await stripSession.NavigateAsync(-1); break;
            case StripShortcut.Next: await stripSession.NavigateAsync(1); break;
            case StripShortcut.MovePrevious: await stripSession.ReorderAsync(-1); break;
            case StripShortcut.MoveNext: await stripSession.ReorderAsync(1); break;
            case StripShortcut.CycleWidth: await stripSession.CycleWidthAsync(); break;
            case StripShortcut.TransferNext: await stripSession.TransferFocusedToNextDisplayAsync(); break;
        }
    }

    private void StripRefreshClick(object sender, RoutedEventArgs e) => Run(() => { SetStripMessage(null); RefreshStripCandidates(); });
    private async void StartStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(async () =>
    {
        if (active.Length > 0 || shortcut is null || source is null || nativeEvents is null || nativeEvents.Failure is not null)
        {
            throw new InvalidOperationException("Restore position probes first and ensure shortcuts and the window event listener are available.");
        }
        stripShortcuts ??= new StripShortcuts(source.Handle);
        try { await stripSession.StartAsync(SelectedStripWindows(), AutoEnrollCheck.IsChecked == true); }
        catch { stripShortcuts.Dispose(); stripShortcuts = null; throw; }
        RefreshStripCandidates();
    });
    private async void AddStripClick(object sender, RoutedEventArgs e) => await AddToStripAsync(SelectedStripWindows);
    private async void AddOpenStripClick(object sender, RoutedEventArgs e) =>
        await AddToStripAsync(() => AvailableStripCandidates().Select(candidate => candidate.Identity).ToArray());

    private async Task AddToStripAsync(Func<WindowIdentity[]> chooseWindows)
    {
        var noAdditions = false;
        await RunStripAsync(async () =>
        {
            if (!stripSession.Running) { throw new InvalidOperationException("Start or resume the strip before adding windows."); }
            var managed = stripSession.AllColumns.Select(column => column.Identity.Handle).ToHashSet();
            var additions = chooseWindows().Where(identity => !managed.Contains(identity.Handle)).Distinct().ToArray();
            if (additions.Length > 0) { await stripSession.AddAsync(additions); }
            RefreshStripCandidates();
            noAdditions = additions.Length == 0;
        });
        if (noAdditions)
        {
            SetStripMessage("No eligible windows to add. Open or restore a window, then select Add open windows.");
        }
    }
    private async void PauseStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(async () =>
    {
        await stripSession.PauseAsync(); active = []; Refresh();
        RefreshStripCandidates();
        ShowRestoreResult();
    });
    private async void ResumeStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(ResumeStripSessionAsync);
    private async Task ResumeStripSessionAsync()
    {
        if (source is null || shortcut is null || active.Length > 0 || nativeEvents is null || nativeEvents.Failure is not null)
        { throw new InvalidOperationException("Restore probes and ensure shortcuts and the window event listener are available before resuming."); }
        stripShortcuts ??= new StripShortcuts(source.Handle);
        await stripSession.ResumeAsync(AutoEnrollCheck.IsChecked == true);
        RefreshStripCandidates();
    }
    private async void PreviousStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(() => HandleShortcutAsync(StripShortcut.Previous));
    private async void NextStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(() => HandleShortcutAsync(StripShortcut.Next));
    private async void MoveLeftStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(() => HandleShortcutAsync(StripShortcut.MovePrevious));
    private async void MoveRightStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(() => HandleShortcutAsync(StripShortcut.MoveNext));
    private async void WidthStripClick(object sender, RoutedEventArgs e) => await RunStripAsync(() => HandleShortcutAsync(StripShortcut.CycleWidth));

    private void OnWindowEvent(DesktopWindowEvent notification)
    {
        if (!stripSession.Running || closingAfterRestore || exitKeepingRecovery) { return; }
        if (notification.Kind == DesktopWindowEventKind.LocationChanged && stripSession.IsApplying) { return; }
        stripSession.Observe(notification);
        if (pendingEvents.Count > 512 && notification.Kind is not
            (DesktopWindowEventKind.MoveSizeStarted or DesktopWindowEventKind.MoveSizeEnded)) { return; }
        pendingEvents.Enqueue(notification);
        if (Interlocked.Exchange(ref eventScheduled, 1) == 0)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref eventScheduled, 0);
                if (!eventTimer.IsEnabled) { eventTimer.Start(); }
            });
        }
    }

    private async void FlushWindowEvents(object? sender, EventArgs args)
    {
        eventTimer.Stop();
        if (StripBusy) { eventTimer.Start(); return; }
        var events = new List<DesktopWindowEvent>();
        while (pendingEvents.TryDequeue(out var notification)) { events.Add(notification); }
        if (stripSession.Running) { await RunStripAsync(() => ReconcileWindowEvents(events), background: true); }
    }

    private async Task ReconcileWindowEvents(IEnumerable<DesktopWindowEvent>? events)
    {
        if (nativeEvents?.Failure is { } error)
        {
            await stripSession.PauseAsync();
            throw new InvalidOperationException("The window event listener failed. " + error.Message, error);
        }
        await stripSession.ReconcileAsync(events);
    }
}
