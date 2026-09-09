using System.Diagnostics;

namespace MacroClicker;

// Pure recording state machine: the hook adapter supplies physical events and
// foreground/hit-test results. No Windows calls, playback or UI mutations here.
internal sealed class RecordingSession
{
    private sealed class Held(IntPtr window, long time)
    {
        public IntPtr Window = window;
        public long Time = time;
        public bool Recorded, Suppressed, Foreign;
        public MacroStep? Step;
    }
    private sealed record Capture(WindowTarget Target, MacroStep Step, long Time, int Segment);
    private readonly Dictionary<IntPtr, WindowTarget> _targets;
    private readonly ushort[][] _controls;
    private readonly Dictionary<int, Held> _keys = [];
    private readonly Dictionary<string, Held> _buttons = [];
    private readonly List<Capture> _events = [];
    private IntPtr _foreground;
    private int _segment;
    private bool _navigation;
    public int Count => _events.Count;
    public bool Full { get; private set; }

    public RecordingSession(IEnumerable<WindowTarget> targets, IEnumerable<ushort[]>? controls = null)
    {
        _targets = targets.Where(t => t.ProcessId != Environment.ProcessId).DistinctBy(t => t.Handle).ToDictionary(t => t.Handle);
        _controls = (controls ?? []).Select(c => c.ToArray()).ToArray();
    }
    private static int Normalize(int key) => key switch
    { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, 0x5C => 0x5B, _ => key };
    private static bool Modifier(int key) => Normalize(key) is 0x10 or 0x11 or 0x12 or 0x5B;
    private HashSet<int> Modifiers() => _keys.Keys.Where(Modifier).Select(Normalize).ToHashSet();
    private bool ControlGesture(int key, HashSet<int> modifiers) => key == 0x79 ||
        _controls.Any(c => c.Length > 0 && c[^1] == key && modifiers.SetEquals(c[..^1].Select(k => Normalize(k))));
    public void Initialize(IntPtr foreground, IEnumerable<int> heldKeys, IEnumerable<string> heldButtons)
    {
        _foreground = foreground;
        foreach (var key in heldKeys) _keys[key] = new Held(IntPtr.Zero, 0) { Foreign = true, Suppressed = true };
        foreach (var button in heldButtons) _buttons[button] = new Held(IntPtr.Zero, 0) { Foreign = true, Suppressed = true };
    }
    private bool Add(IntPtr window, MacroStep step, long time, bool cleanup = false)
    {
        // Leave room for matching releases at the recording limit.
        if (!cleanup && _events.Count >= 49744) { Full = true; return false; }
        if (!_targets.TryGetValue(window, out var target)) return false;
        step.DelayAfterMs = 0; step.MoveDelayMs = 0;
        _events.Add(new Capture(target, step, time, _segment)); return true;
    }
    private static MacroStep Key(int key, ButtonAction action) => new()
    { Kind = InputKind.Key, Input = InputSender.KeyName(key), Action = action };
    private void Release(int key, Held held, long time)
    {
        if (held.Recorded) Add(held.Window, Key(key, ButtonAction.Up), time, true);
        held.Recorded = false;
    }
    private void Release(Held held, long time)
    {
        if (held.Recorded && held.Step is not null)
        {
            var up = held.Step.Copy(); up.Action = ButtonAction.Up; up.UseCoordinates = false;
            Add(held.Window, up, time, true);
        }
        held.Recorded = false;
    }
    private void Focus(IntPtr window, long time)
    {
        if (_foreground == window) return;
        FlushModifiers(time, reviveSuppressed: false);
        foreach (var held in _buttons.Values) { Release(held, time); held.Foreign = true; }
        foreach (var (key, held) in _keys.Reverse()) { Release(key, held, time); held.Foreign = true; }
        _foreground = window; _segment++;
    }
    private void FlushModifiers(long time, bool reviveSuppressed = true)
    {
        foreach (var (key, held) in _keys.Where(p => Modifier(p.Key)).OrderBy(p => p.Value.Time))
        {
            if (held.Foreign || held.Recorded || held.Window != _foreground || (!reviveSuppressed && held.Suppressed)) continue;
            held.Recorded = Add(held.Window, Key(key, ButtonAction.Down), held.Suppressed ? time : held.Time);
            held.Suppressed = false;
        }
    }
    private void SuppressGesture(int mainKey, long time)
    {
        foreach (var (key, held) in _keys.Reverse().Where(p => Modifier(p.Key) || p.Key == mainKey))
        { Release(key, held, time); held.Suppressed = true; }
    }
    public void Keyboard(IntPtr foreground, int key, bool up, long time)
    {
        // Recognize the gesture before a focus change can commit pending
        // modifiers. Focus may already have changed when its main key arrives.
        if (!up)
        {
            var pressed = Modifiers();
            if (ControlGesture(key, pressed) || key == 9 && pressed.Overlaps([0x12, 0x5B])) SuppressGesture(key, time);
        }
        Focus(foreground, time);
        if (up)
        {
            if (_keys.TryGetValue(key, out var held))
            {
                if (Modifier(key) && !held.Foreign && !held.Suppressed && !held.Recorded && !_navigation) FlushModifiers(time);
                Release(key, held, time); _keys.Remove(key);
            }
            if (!Modifiers().Overlaps([0x12, 0x5B])) _navigation = false;
            return;
        }
        if (!_keys.TryGetValue(key, out var state))
        {
            state = new Held(foreground, time) { Foreign = !_targets.ContainsKey(foreground) };
            _keys[key] = state;
        }
        var modifiers = Modifiers();
        if (key == 9 && modifiers.Overlaps([0x12, 0x5B]))
        {
            SuppressGesture(key, time); _navigation = true;
            // The task switcher can leave GetForegroundWindow pointing at the old app.
            Focus(IntPtr.Zero, time); return;
        }
        if (ControlGesture(key, modifiers))
        { SuppressGesture(key, time); return; }
        if (_navigation) { state.Suppressed = true; return; }
        if (state.Foreign || state.Suppressed || Modifier(key)) return;
        if (_keys.Any(p => Modifier(p.Key) && p.Value.Foreign)) { state.Foreign = true; return; }
        FlushModifiers(time);
        state.Recorded |= Add(foreground, Key(key, ButtonAction.Down), time);
    }
    public void Mouse(IntPtr foreground, IntPtr root, MacroStep step, long time)
    {
        Focus(foreground, time);
        var eligible = !_navigation && root == foreground && _targets.ContainsKey(root) &&
            !_keys.Any(p => Modifier(p.Key) && p.Value.Foreign);
        if (step.Kind == InputKind.Mouse)
        {
            if (step.Action == ButtonAction.Up)
            {
                if (_buttons.Remove(step.Input, out var held)) Release(held, time);
                return;
            }
            // A click activating a window (or clicking MacroClicker) is navigation,
            // not a macro action. Suppress its full down/up/drag sequence.
            if (!eligible)
            {
                _buttons[step.Input] = new Held(IntPtr.Zero, time) { Foreign = true };
                if (root != foreground) Focus(IntPtr.Zero, time);
                return;
            }
            FlushModifiers(time);
            var button = new Held(root, time) { Step = step.Copy() };
            button.Recorded = Add(root, step.Copy(), time); _buttons[step.Input] = button;
        }
        else if (eligible && !_buttons.Values.Any(h => h.Foreign))
        {
            if (step.Kind != InputKind.Move) FlushModifiers(time);
            Add(root, step.Copy(), time);
        }
    }
    public List<MacroNode> Finish(long time)
    {
        FlushModifiers(time, reviveSuppressed: false);
        foreach (var held in _buttons.Values) Release(held, time);
        foreach (var (key, held) in _keys.Reverse()) Release(key, held, time);
        var nodes = new List<MacroNode>(); MacroNode? current = null;
        long? previous = null; int? segment = null;
        foreach (var capture in _events.OrderBy(e => e.Time))
        {
            if (current?.SessionTarget?.Handle != capture.Target.Handle)
            {
                current = new MacroNode { Name = capture.Target.Title, ProcessName = capture.Target.ProcessName,
                    WindowTitle = capture.Target.Title, SessionTarget = capture.Target };
                nodes.Add(current); previous = null;
            }
            if (segment != capture.Segment) previous = null;
            segment = capture.Segment;
            var step = capture.Step.Copy();
            var elapsed = previous.HasValue ? (long)Stopwatch.GetElapsedTime(previous.Value, capture.Time).TotalMilliseconds : 0;
            while (elapsed > 600000) { current.Steps.Add(new MacroStep { Kind = InputKind.Pause, HoldMs = 600000, DelayAfterMs = 0 }); elapsed -= 600000; }
            step.DelayBeforeMs = (int)Math.Max(0, elapsed); current.Steps.Add(step); previous = capture.Time;
        }
        return nodes;
    }
}
