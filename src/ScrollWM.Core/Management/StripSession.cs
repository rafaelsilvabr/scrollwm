using System.Collections.Concurrent;
using ScrollWM.Core.Layout;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;

namespace ScrollWM.Core.Management;

public interface IStripDesktop : IRestorableWindows
{
    PixelRect GetSingleDisplayWorkArea();
    long FocusedHandle { get; }
    bool IsEligible(WindowSnapshot snapshot);
    uint ReadDpi(WindowSnapshot snapshot);
    void Move(WindowSnapshot snapshot, PixelRect target);
    PixelRect ReadFrame(long handle);
    PixelRect ReadManagedFrame(WindowSnapshot snapshot) => ReadFrame(snapshot.Identity.Handle);
    PixelRect ReadPositionedFrame(WindowSnapshot snapshot) => ReadFrame(snapshot.Identity.Handle);
    bool Focus(WindowSnapshot snapshot);
    IReadOnlyList<WindowIdentity> EnumerateKnownWindows();
    IReadOnlyList<WindowIdentity> EnumerateEligibleWindows();
    bool IsUserMoving(WindowSnapshot snapshot);
    IReadOnlyList<DisplayInfo> GetDisplayConfiguration() =>
        [new("default", GetSingleDisplayWorkArea(), GetSingleDisplayWorkArea(), 96, true)];
    string GetWindowDisplay(WindowIdentity identity) => GetDisplayConfiguration()[0].Id;
    void MoveOnDisplay(WindowSnapshot snapshot, PixelRect target, DisplayInfo display, PixelRect viewport) => Move(snapshot, target);
    void EnsurePresentation(WindowSnapshot snapshot, DisplayInfo display, PixelRect viewport) { }
    void SuspendPresentation(WindowSnapshot snapshot) => RestorePresentation(snapshot);
    void PlaceBehind(IReadOnlyList<WindowSnapshot> edges, IReadOnlyList<WindowSnapshot> visible) { }
}

// One command gate and durable recovery journal cover every display. Only dirty
// strips are applied; monitor ownership never follows an off-viewport rectangle.
public sealed class StripSession(IStripDesktop desktop, IRecoveryStore store, Func<Task>? settle = null)
{
    private readonly RecoverySession recovery = new(store, desktop);
    private readonly SemaphoreSlim commands = new(1, 1);
    private readonly Dictionary<WindowIdentity, WindowSnapshot> snapshots = [];
    private readonly Dictionary<string, ScrollingStrip> strips = [];
    private readonly Dictionary<WindowIdentity, string> owners = [];
    private readonly HashSet<string> dirty = [];
    private readonly HashSet<WindowIdentity> baseline = [];
    private sealed record SleepingColumn(StripColumn Column, int Index, string DisplayId);
    private readonly Dictionary<WindowIdentity, SleepingColumn> suspended = [];
    private readonly Dictionary<WindowIdentity, SleepingColumn> detached = [];
    private readonly Dictionary<long, PixelRect> gestureOrigins = [];
    private readonly ConcurrentDictionary<long, byte> interactions = [];
    private readonly ConcurrentDictionary<long, byte> managedHandles = [];
    private readonly Func<Task> settleFrames = settle ?? (() => Task.Delay(150));
    private bool ownsCapture;
    private bool autoEnroll;
    private bool focusPending;
    private int retiredClosed;
    private int applying;
    private long lastForegroundHandle;

    public IReadOnlyList<DisplayInfo> Displays { get; private set; } = [];
    public IReadOnlyDictionary<string, ScrollingStrip> Strips => strips;
    public string? ActiveDisplayId { get; private set; }
    public ScrollingStrip? Strip => ActiveDisplayId is { } id ? strips.GetValueOrDefault(id) : null;
    public IReadOnlyList<StripColumn> AllColumns => strips.Values.SelectMany(strip => strip.Columns).ToArray();
    public bool Running { get; private set; }
    public string Status { get; private set; } = "Select windows to start the strip.";
    public RestoreResult? LastRestore { get; private set; }
    public bool IsApplying => Volatile.Read(ref applying) != 0;
    public bool AutomaticEnrollment => autoEnroll;

    public void Observe(DesktopWindowEvent notification)
    {
        if (notification.Kind == DesktopWindowEventKind.MoveSizeStarted && Running && managedHandles.ContainsKey(notification.Handle))
        { interactions[notification.Handle] = 0; }
    }

    public Task SetActiveDisplayAsync(string id) => Serialize(() =>
    {
        if (!strips.ContainsKey(id)) { throw new ArgumentException("Select an available monitor.", nameof(id)); }
        ActiveDisplayId = id;
        return Task.CompletedTask;
    });

    public Task StartAsync(IEnumerable<WindowIdentity> selected, bool automaticEnrollment = false) => Serialize(async () =>
    {
        if (Running) { throw new InvalidOperationException("Pause the current strip before starting another."); }
        var foreground = desktop.FocusedHandle;
        var identities = selected.OrderByDescending(item => item.Handle == foreground).ToArray();
        var configuration = ReadDisplays();
        baseline.Clear();
        baseline.UnionWith(desktop.EnumerateKnownWindows());
        var captured = recovery.Begin(identities);
        ownsCapture = true;
        Displays = configuration;
        snapshots.Clear(); strips.Clear(); owners.Clear(); dirty.Clear(); suspended.Clear(); detached.Clear();
        gestureOrigins.Clear(); interactions.Clear(); managedHandles.Clear();
        retiredClosed = 0; focusPending = false; autoEnroll = automaticEnrollment;
        foreach (var display in Displays)
        { strips.Add(display.Id, new(display.WorkArea, display.Scale(12), MonitorPlacement.Margin(display, Displays.Count))); }
        ActiveDisplayId = Displays.FirstOrDefault(item => item.Primary)?.Id ?? Displays[0].Id;
        foreach (var snapshot in captured)
        {
            snapshots.Add(snapshot.Identity, snapshot);
            var owner = CapturedOwner(snapshot);
            owners.Add(snapshot.Identity, owner);
            strips[owner].InsertAfterFocus(snapshot.Identity);
            managedHandles[snapshot.Identity.Handle] = 0;
            dirty.Add(owner);
        }
        foreach (var group in captured.GroupBy(item => owners[item.Identity]))
        {
            var first = group.FirstOrDefault(item => item.Identity.Handle == foreground) ?? group.First();
            strips[group.Key].Focus(first.Identity);
        }
        ActiveDisplayId = captured.FirstOrDefault(item => item.Identity.Handle == foreground) is { } focused
            ? owners[focused.Identity] : owners[captured[0].Identity];
        Running = true;
        lastForegroundHandle = foreground;
        await ApplyAsync(focus: false);
    });

    public Task AddAsync(IEnumerable<WindowIdentity> selected) => Serialize(async () =>
    {
        RequireRunning(); ValidateDesktop(); Prune();
        var additions = selected.ToArray();
        if (additions.Length == 0 || additions.Select(item => item.Handle).Distinct().Count() != additions.Length ||
            additions.Any(item => AllColumns.Any(column => column.Identity.Handle == item.Handle)))
        { throw new ArgumentException("Select distinct windows outside the strip.", nameof(selected)); }
        var targetOwners = additions.ToDictionary(identity => identity, identity => owners.GetValueOrDefault(identity) ?? OwnerAtCapture(identity));
        var fresh = additions.Where(item => !snapshots.ContainsKey(item)).ToArray();
        if (fresh.Length > 0)
        {
            foreach (var snapshot in recovery.Append(fresh))
            {
                snapshots.Add(snapshot.Identity, snapshot);
                if (snapshot.OriginalDisplay is not null) { targetOwners[snapshot.Identity] = CapturedOwner(snapshot); }
            }
        }
        foreach (var identity in additions)
        {
            var owner = targetOwners[identity];
            var destination = strips[owner];
            owners[identity] = owner;
            if (suspended.Remove(identity, out var sleeping) || detached.Remove(identity, out sleeping))
            { destination.Reinsert(ScaleColumn(sleeping.Column, sleeping.DisplayId, owner), AfterFocus(destination), focus: true); }
            else { destination.InsertAfterFocus(identity); }
            managedHandles[identity.Handle] = 0;
            dirty.Add(owner);
            ActiveDisplayId = owner;
        }
        await ApplyAsync(focus: true);
    });

    public Task NavigateAsync(int direction) => ChangeAsync(strip => strip.FocusAdjacent(direction));
    public Task ReorderAsync(int direction) => ChangeAsync(strip => strip.ReorderFocused(direction));
    public Task CycleWidthAsync() => ChangeAsync(strip => strip.CycleWidth());
    private Task ChangeAsync(Action<ScrollingStrip> change) => Serialize(async () =>
    {
        RequireRunning(); ValidateDesktop(); Prune(); ActivateForeground();
        change(Strip!);
        dirty.Add(ActiveDisplayId!);
        await ApplyAsync(focus: true);
    });

    public Task TransferFocusedAsync(string destinationId) => Serialize(() => TransferFocusedCoreAsync(destinationId));
    public Task TransferFocusedToNextDisplayAsync() => Serialize(() => TransferFocusedCoreAsync(null));
    private async Task TransferFocusedCoreAsync(string? destinationId)
    {
        RequireRunning(); ValidateDesktop(); Prune(); ActivateForeground();
        if (!interactions.IsEmpty) { throw new ArgumentException("Finish dragging or resizing before transferring a window."); }
        if (destinationId is null)
        {
            if (Displays.Count < 2) { return; }
            var ordered = Displays.OrderByDescending(item => item.Primary).ThenBy(item => item.Bounds.Left).ThenBy(item => item.Bounds.Top).ToArray();
            var index = ordered.ToList().FindIndex(item => item.Id == ActiveDisplayId);
            destinationId = ordered[(index + 1) % ordered.Length].Id;
        }
        if (!strips.ContainsKey(destinationId)) { throw new ArgumentException("Select an available destination monitor."); }
        if (Strip!.Focused is not { } identity || owners[identity] == destinationId) { return; }
        Transfer(identity, destinationId);
        ActiveDisplayId = destinationId;
        await ApplyAsync(focus: true);
    }

    public Task ReconcileAsync(IEnumerable<DesktopWindowEvent>? notifications = null) => Serialize(async () =>
    {
        if (!Running) { return; }
        ValidateDesktop(); Prune();
        var batch = notifications?.ToArray();
        var inventoryNeeded = batch is null || batch.Any(item => item.Kind is DesktopWindowEventKind.Created or
            DesktopWindowEventKind.Shown or DesktopWindowEventKind.Restored or DesktopWindowEventKind.StateChanged);
        var eligible = (suspended.Count > 0 || autoEnroll) && inventoryNeeded ? desktop.EnumerateEligibleWindows() : [];
        foreach (var pair in suspended.OrderBy(item => item.Value.Index).ToArray())
        {
            if (!eligible.Contains(pair.Key)) { continue; }
            suspended.Remove(pair.Key);
            var sleeping = pair.Value;
            var index = sleeping.Index - suspended.Values.Count(item => item.DisplayId == sleeping.DisplayId && item.Index < sleeping.Index);
            strips[sleeping.DisplayId].Reinsert(sleeping.Column, index, focus: false);
            managedHandles[pair.Key.Handle] = 0;
            dirty.Add(sleeping.DisplayId);
        }
        foreach (var notification in batch ?? []) { HandleGesture(notification); }
        if (batch is null)
        {
            foreach (var handle in interactions.Keys)
            {
                var snapshot = snapshots.Values.FirstOrDefault(item => item.Identity.Handle == handle);
                if (snapshot is not null && !desktop.IsUserMoving(snapshot))
                { HandleGesture(new(handle, DesktopWindowEventKind.MoveSizeEnded)); }
            }
        }
        if (!interactions.IsEmpty) { return; }
        if (autoEnroll)
        {
            foreach (var identity in eligible)
            {
                if (baseline.Contains(identity) || snapshots.ContainsKey(identity) || detached.ContainsKey(identity)) { continue; }
                try
                {
                    var owner = OwnerAtCapture(identity);
                    var captured = recovery.Append([identity]);
                    if (captured[0].OriginalDisplay is not null) { owner = CapturedOwner(captured[0]); }
                    snapshots.Add(identity, captured[0]);
                    owners[identity] = owner;
                    var destination = strips[owner];
                    var oldFocus = destination.Focused;
                    destination.InsertAfterFocus(identity);
                    managedHandles[identity.Handle] = 0;
                    if (oldFocus is not null && desktop.FocusedHandle != identity.Handle) { destination.Focus(oldFocus); }
                    dirty.Add(owner);
                }
                catch (WindowUnavailableException) { }
            }
        }
        if (!focusPending) { ActivateForeground(); }
        if (dirty.Count > 0) { await ApplyAsync(focus: false); }
        else
        {
            Interlocked.Exchange(ref applying, 1);
            try
            {
                foreach (var pair in strips)
                {
                    foreach (var column in pair.Value.Columns) { desktop.EnsurePresentation(snapshots[column.Identity], Display(pair.Key), pair.Value.Viewport); }
                    ArrangeEdges(pair.Key);
                }
            }
            finally { Interlocked.Exchange(ref applying, 0); }
        }
    });

    public Task PauseAsync() => Serialize(() => { Restore(); return Task.CompletedTask; });
    public Task ResumeAsync(bool? automaticEnrollment = null) => Serialize(async () =>
    {
        if (Running || AllColumns.Count == 0) { throw new InvalidOperationException("There is no paused strip to resume."); }
        ValidateDesktop();
        baseline.UnionWith(desktop.EnumerateKnownWindows());
        autoEnroll = automaticEnrollment ?? autoEnroll;
        var captured = recovery.Begin(AllColumns.Select(item => item.Identity));
        ownsCapture = true;
        snapshots.Clear(); dirty.Clear();
        foreach (var snapshot in captured)
        {
            snapshots.Add(snapshot.Identity, snapshot);
            var currentOwner = CapturedOwner(snapshot);
            if (owners[snapshot.Identity] != currentOwner) { Transfer(snapshot.Identity, currentOwner); }
            managedHandles[snapshot.Identity.Handle] = 0;
            dirty.Add(currentOwner);
        }
        Running = true;
        ActivateForeground();
        await ApplyAsync(focus: false);
    });

    private IReadOnlyList<DisplayInfo> ReadDisplays()
    {
        var displays = desktop.GetDisplayConfiguration();
        if (displays.Count == 0 || displays.Select(item => item.Id).Distinct().Count() != displays.Count ||
            displays.Any(item => string.IsNullOrWhiteSpace(item.Id) || item.Dpi is < 48 or > 768 ||
                item.Bounds.Width <= 0 || item.Bounds.Height <= 0 || item.WorkArea.Width <= item.Scale(12) || item.WorkArea.Height <= item.Scale(12)))
        { throw new InvalidOperationException("The display configuration is unavailable or invalid."); }
        return displays.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }
    private void ValidateDesktop()
    {
        if (!ReadDisplays().SequenceEqual(Displays))
        { throw new InvalidOperationException("The monitor configuration changed. Original placements will be restored. Start a new strip after restoration."); }
    }
    private void RequireRunning()
    { if (!Running || Strip is null) { throw new InvalidOperationException("Start the strip first."); } }
    private DisplayInfo Display(string id) => Displays.Single(item => item.Id == id);
    private string OwnerAtCapture(WindowIdentity identity)
    {
        var id = desktop.GetWindowDisplay(identity);
        if (!strips.ContainsKey(id)) { throw new InvalidOperationException("The window's monitor changed during capture."); }
        return id;
    }
    private string CapturedOwner(WindowSnapshot snapshot)
    {
        var id = snapshot.OriginalDisplay?.Id ?? OwnerAtCapture(snapshot.Identity);
        if (!strips.ContainsKey(id)) { throw new InvalidOperationException("The monitor configuration changed during capture."); }
        return id;
    }
    private static int AfterFocus(ScrollingStrip strip) => strip.Columns.ToList().FindIndex(item => item.Identity == strip.Focused) + 1;
    private void ActivateForeground()
    {
        var foreground = desktop.FocusedHandle;
        if (foreground == lastForegroundHandle) { return; }
        lastForegroundHandle = foreground;
        var identity = owners.Keys.FirstOrDefault(item => item.Handle == foreground);
        if (identity is null || !strips[owners[identity]].Columns.Any(item => item.Identity == identity)) { return; }
        var owner = owners[identity];
        ActiveDisplayId = owner;
        if (strips[owner].Focused != identity) { strips[owner].Focus(identity); dirty.Add(owner); }
    }
    private StripColumn ScaleColumn(StripColumn column, string source, string destination)
    {
        if (source == destination) { return column; }
        var ratio = Display(destination).Dpi / (double)Display(source).Dpi;
        var minimum = Math.Max(1, (int)Math.Round(column.MinimumWidth * ratio));
        var width = column.Preset is { } preset ? ColumnWidths.ResolvePixels(preset, strips[destination].Viewport.Width, strips[destination].Gap)
            : Math.Max(1, (int)Math.Round(column.Width * ratio));
        return column with { Width = Math.Max(width, minimum), MinimumWidth = minimum };
    }
    private void Transfer(WindowIdentity identity, string destination)
    {
        var source = owners[identity];
        var column = strips[source].Columns.Single(item => item.Identity == identity);
        strips[source].Remove(identity);
        strips[destination].Reinsert(ScaleColumn(column, source, destination), AfterFocus(strips[destination]), focus: true);
        owners[identity] = destination;
        dirty.Add(source); dirty.Add(destination);
    }

    private void Prune()
    {
        foreach (var snapshot in snapshots.Values.ToArray())
        {
            var identity = desktop.CheckIdentity(snapshot);
            if (identity == IdentityStatus.Unavailable) { throw new InvalidOperationException("A managed window's identity is unavailable."); }
            var owner = owners[snapshot.Identity];
            var strip = strips[owner];
            if (identity == IdentityStatus.ClosedOrReused)
            {
                if (recovery.Forget(snapshot.Identity)) { retiredClosed++; }
                snapshots.Remove(snapshot.Identity); owners.Remove(snapshot.Identity); suspended.Remove(snapshot.Identity);
                managedHandles.TryRemove(snapshot.Identity.Handle, out _); interactions.TryRemove(snapshot.Identity.Handle, out _);
                gestureOrigins.Remove(snapshot.Identity.Handle);
                if (strip.Remove(snapshot.Identity)) { dirty.Add(owner); }
            }
            else if (!desktop.IsEligible(snapshot))
            {
                var index = strip.Columns.ToList().FindIndex(item => item.Identity == snapshot.Identity);
                if (index < 0) { continue; }
                desktop.SuspendPresentation(snapshot);
                var originalIndex = index;
                foreach (var slot in suspended.Values.Where(item => item.DisplayId == owner).OrderBy(item => item.Index))
                { if (slot.Index <= originalIndex) { originalIndex++; } }
                suspended[snapshot.Identity] = new(strip.Columns[index], originalIndex, owner);
                strip.Remove(snapshot.Identity); dirty.Add(owner);
            }
        }
    }

    private async Task ApplyAsync(bool focus)
    {
        focusPending |= focus;
        Interlocked.Exchange(ref applying, 1);
        try
        {
            ValidateDesktop(); Prune();
            if (!interactions.IsEmpty) { return; }
            for (var pass = 0; pass < 3; pass++)
            {
                var pending = dirty.ToArray();
                var targets = pending.SelectMany(id => strips[id].Layout().Select(target => (Id: id, Target: target))).ToArray();
                foreach (var entry in targets)
                {
                    var snapshot = snapshots[entry.Target.Identity];
                    if (desktop.ReadDpi(snapshot) == 0) { throw new InvalidOperationException("The window's DPI is unavailable."); }
                    desktop.MoveOnDisplay(snapshot, entry.Target.Bounds, Display(entry.Id), strips[entry.Id].Viewport);
                }
                await settleFrames();
                if (!interactions.IsEmpty) { return; }
                var adjusted = false;
                foreach (var entry in targets)
                {
                    var target = entry.Target;
                    desktop.EnsurePresentation(snapshots[target.Identity], Display(entry.Id), strips[entry.Id].Viewport);
                    var actual = desktop.ReadManagedFrame(snapshots[target.Identity]);
                    if (actual.Width > target.Bounds.Width)
                    { strips[entry.Id].SetMinimumWidth(target.Identity, actual.Width); adjusted = true; }
                    else if (actual != target.Bounds)
                    {
                        if (pass == 2) { throw new InvalidOperationException("An application constrained the strip placement. Original placements will be restored."); }
                        adjusted = true;
                    }
                }
                if (adjusted) { continue; }
                foreach (var id in pending) { ArrangeEdges(id); }
                foreach (var id in pending) { dirty.Remove(id); }
                var accepted = !focusPending || Strip?.Focused is null || desktop.Focus(snapshots[Strip.Focused]);
                if (focusPending) { lastForegroundHandle = desktop.FocusedHandle; }
                focusPending = false;
                Status = $"Strip active · {AllColumns.Count} window(s) · {Displays.Count} monitor(s)" +
                    (accepted ? "" : " · Window revealed; Windows declined focus.");
                return;
            }
            throw new InvalidOperationException("Application sizes did not settle after three layout attempts.");
        }
        finally { Interlocked.Exchange(ref applying, 0); }
    }

    private void HandleGesture(DesktopWindowEvent notification)
    {
        var identity = owners.Keys.FirstOrDefault(item => item.Handle == notification.Handle);
        var owner = identity is not null ? owners[identity] : null;
        var column = owner is not null ? strips[owner].Columns.FirstOrDefault(item => item.Identity == identity) : null;
        if (notification.Kind == DesktopWindowEventKind.MoveSizeStarted && column is not null)
        { gestureOrigins[notification.Handle] = desktop.ReadPositionedFrame(snapshots[column.Identity]); }
        else if (notification.Kind == DesktopWindowEventKind.MoveSizeEnded)
        {
            interactions.TryRemove(notification.Handle, out _);
            var hasOrigin = gestureOrigins.Remove(notification.Handle, out var before);
            if (column is null || !hasOrigin) { return; }
            var after = desktop.ReadPositionedFrame(snapshots[column.Identity]);
            var currentOwner = OwnerAtCapture(column.Identity);
            if (currentOwner != owner)
            { Transfer(column.Identity, currentOwner); ActiveDisplayId = currentOwner; }
            else if (after.Width != before.Width || after.Height != before.Height)
            {
                if (after.Width < column.MinimumWidth) { strips[owner!].SetMinimumWidth(column.Identity, after.Width); }
                strips[owner!].SetCustomWidth(column.Identity, after.Width); dirty.Add(owner!);
            }
            else if (after.Left != before.Left || after.Top != before.Top)
            {
                recovery.Forget(column.Identity, restorePresentation: true);
                snapshots.Remove(column.Identity); owners.Remove(column.Identity);
                managedHandles.TryRemove(column.Identity.Handle, out _);
                detached[column.Identity] = new(column, 0, owner!);
                strips[owner!].Remove(column.Identity); dirty.Add(owner!);
            }
        }
    }

    private void ArrangeEdges(string displayId)
    {
        var strip = strips[displayId];
        var layout = strip.Layout();
        var edges = layout.Where(item => MonitorPlacement.Intersects(item.Bounds, strip.Viewport) &&
            !MonitorPlacement.Contains(strip.Viewport, item.Bounds)).Select(item => snapshots[item.Identity]).ToArray();
        var visible = layout.Where(item => MonitorPlacement.Contains(strip.Viewport, item.Bounds))
            .Select(item => snapshots[item.Identity]).ToArray();
        if (edges.Length > 0) { desktop.PlaceBehind(edges, visible); }
    }

    private void Restore()
    {
        Running = false; focusPending = false; dirty.Clear();
        var result = recovery.RestoreAll();
        LastRestore = result with { ClosedOrReused = result.ClosedOrReused + retiredClosed };
        retiredClosed = 0;
        if (LastRestore.Remaining == 0)
        {
            foreach (var item in suspended.Values.OrderBy(item => item.Index))
            { strips[item.DisplayId].Reinsert(item.Column, item.Index, focus: false); }
            suspended.Clear(); ownsCapture = false; snapshots.Clear();
        }
        interactions.Clear(); managedHandles.Clear(); gestureOrigins.Clear();
        Status = $"Paused · restored {LastRestore.Restored}, closed/replaced {LastRestore.ClosedOrReused}, pending {LastRestore.Remaining}.";
        if (LastRestore.Errors.Length > 0) { Status += " " + string.Join(" ", LastRestore.Errors); }
    }

    private async Task Serialize(Func<Task> action)
    {
        await commands.WaitAsync();
        try { await action(); }
        catch (Exception exception)
        {
            if (exception is ArgumentException or WindowUnavailableException) { throw; }
            if (ownsCapture)
            {
                try { Restore(); }
                catch (Exception restoreFailure) { Running = false; Status = $"Recovery remains pending: {restoreFailure.Message}"; }
                throw new InvalidOperationException(exception.Message + " " + Status, exception);
            }
            throw;
        }
        finally { commands.Release(); }
    }
}
