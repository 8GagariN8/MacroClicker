using System.Diagnostics;
using MacroClicker;

internal static class RecordingChecks
{
    private static readonly IntPtr A = (IntPtr)101, B = (IntPtr)102, UI = (IntPtr)103;
    private const int Ctrl = 0xA2, Alt = 0xA4, Shift = 0xA0, Win = 0x5B, Tab = 9, F8 = 0x77, F9 = 0x78, F10 = 0x79, C = 67;
    private sealed class Scene
    {
        public RecordingSession Recorder;
        public long Milliseconds;
        public long Now => Milliseconds * Stopwatch.Frequency / 1000;
        public Scene(params ushort[][] controls)
        {
            Recorder = new RecordingSession([new("A", A), new("B", B), new("Own app", UI, Environment.ProcessId)], controls);
            Recorder.Initialize(A, [], []);
        }
        public void Key(int key, bool up = false, IntPtr? foreground = null)
        { Milliseconds += 10; Recorder.Keyboard(foreground ?? A, key, up, Now); }
        public void Tap(int key, IntPtr? foreground = null) { Key(key, false, foreground); Key(key, true, foreground); }
        public void Mouse(ButtonAction action, IntPtr? foreground = null, IntPtr? root = null)
        { Milliseconds += 10; Recorder.Mouse(foreground ?? A, root ?? A, new MacroStep { Kind = InputKind.Mouse, Input = "Левая", Action = action, UseCoordinates = true, X = 10, Y = 20 }, Now); }
        public void Move(IntPtr? foreground = null, IntPtr? root = null)
        { Milliseconds += 10; Recorder.Mouse(foreground ?? A, root ?? A, new MacroStep { Kind = InputKind.Move, UseCoordinates = true, X = 30, Y = 40 }, Now); }
        public List<MacroNode> Finish() { Milliseconds += 10; return Recorder.Finish(Now); }
        public MacroStep[] Steps() => Finish().SelectMany(n => n.Steps).ToArray();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string[] Keys(IEnumerable<MacroStep> steps) => steps.Where(s => s.Kind == InputKind.Key).Select(s => s.Input + ":" + s.Action).ToArray();
    private static string D(int key) => key + ":Down";
    private static string U(int key) => key + ":Up";
    public static void Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Own window input is ignored even if supplied as target", () => {
                var s = new Scene(); s.Tap(C, UI); s.Mouse(ButtonAction.Down, UI, UI); s.Mouse(ButtonAction.Up, UI, UI);
                Check(s.Finish().Count == 0, "Own input/empty node escaped");
            }),
            ("Activation click drops both halves; next real click survives", () => {
                var s = new Scene(); s.Mouse(ButtonAction.Down, UI, A); s.Mouse(ButtonAction.Up, A, A);
                Check(s.Recorder.Count == 0, "Activation click recorded");
                s.Mouse(ButtonAction.Down); s.Mouse(ButtonAction.Up);
                var steps = s.Steps(); Check(steps.Length == 2 && steps[0].Action == ButtonAction.Down && steps[1].Action == ButtonAction.Up, "Real click lost");
            }),
            ("Keys/buttons held before start do not leak or repeat", () => {
                var s = new Scene(); s.Recorder.Initialize(A, [Ctrl, C], ["Левая"]); s.Key(C); s.Key(C,true); s.Key(Ctrl,true); s.Mouse(ButtonAction.Up);
                Check(s.Recorder.Count == 0, "Startup input escaped"); s.Tap(C); Check(Keys(s.Steps()).SequenceEqual([D(C),U(C)]), "Fresh input lost");
            }),
            ("Orphan releases create no actions and no empty nodes", () => {
                var s = new Scene(); s.Key(C,true); s.Mouse(ButtonAction.Up); Check(s.Finish().Count == 0, "Orphan produced node");
            }),
            ("F10 and auto-repeat never recorded", () => {
                var s = new Scene(); s.Key(F10); s.Key(F10); s.Key(F10,true); Check(s.Finish().Count == 0, "F10 escaped");
            }),
            ("Start chord and all modifier release orders removed", () => {
                foreach(var reverse in new[]{false,true}) { var s = new Scene([17,18,F8]); s.Key(Ctrl);s.Key(Alt);s.Key(F8);s.Key(F8);s.Key(F8,true);
                    s.Key(reverse?Ctrl:Alt,true);s.Key(reverse?Alt:Ctrl,true);Check(s.Finish().Count==0,"Control chord leaked"); }
            }),
            ("Separate stop and custom Win shortcut removed", () => {
                var s = new Scene([17,18,F8],[16,F9],[91,65]); s.Key(Shift);s.Tap(F9);s.Key(Shift,true);s.Key(Win);s.Tap(65);s.Key(Win,true);
                Check(s.Finish().Count==0,"Custom/stop shortcut leaked");
            }),
            ("Different modifier combination stays ordinary input", () => {
                var s = new Scene([17,18,F8]);s.Key(Ctrl);s.Tap(F8);s.Key(Ctrl,true);
                Check(Keys(s.Steps()).SequenceEqual([D(Ctrl),D(F8),U(F8),U(Ctrl)]),"Non-reserved chord removed");
            }),
            ("Ctrl+C preserves order and hold duration", () => {
                var s = new Scene(); s.Key(Ctrl);s.Key(C);s.Milliseconds+=200;s.Key(C,true);s.Key(Ctrl,true);var steps=s.Steps();
                Check(Keys(steps).SequenceEqual([D(Ctrl),D(C),U(C),U(Ctrl)]) && steps[2].DelayBeforeMs==210,"Normal chord/timing damaged");
            }),
            ("Alt+Shift and standalone modifiers survive", () => {
                var s = new Scene();s.Key(Alt);s.Key(Shift);s.Key(Shift,true);s.Key(Alt,true);
                Check(Keys(s.Steps()).SequenceEqual([D(Alt),D(Shift),U(Shift),U(Alt)]),"Layout shortcut damaged");
            }),
            ("Control gesture never erases an earlier valid chord", () => {
                var s = new Scene([17,18,F8]);s.Key(Ctrl);s.Tap(C);s.Key(Alt);s.Tap(F8);s.Key(Alt,true);s.Key(Ctrl,true);
                Check(Keys(s.Steps()).SequenceEqual([D(Ctrl),D(C),U(C),U(Ctrl)]),"Earlier chord erased/control leaked");
            }),
            ("Valid action after control chord reuses held modifiers", () => {
                var s = new Scene([17,18,F8]);s.Key(Ctrl);s.Key(Alt);s.Tap(F8);s.Tap(C);s.Key(Alt,true);s.Key(Ctrl,true);
                Check(Keys(s.Steps()).SequenceEqual([D(Ctrl),D(Alt),D(C),U(C),U(Alt),U(Ctrl)]),"Following chord damaged");
            }),
            ("Alt+Tab and Alt+Shift+Tab are navigation only", () => {
                foreach(var reverse in new[]{false,true}) {var s=new Scene();s.Key(Alt);if(reverse)s.Key(Shift);s.Tap(Tab);if(reverse)s.Key(Shift,true);s.Key(Alt,true);Check(s.Finish().Count==0,"Alt navigation leaked");}
            }),
            ("Navigation is excluded even if foreground changed before Tab", () => {
                var s=new Scene();s.Key(Alt);s.Key(Tab,false,B);s.Key(Tab,true,B);s.Key(Alt,true,B);
                Check(s.Finish().Count==0,"Modifier committed during focus transition");
            }),
            ("Earlier Alt input is not removed by a later switch", () => {
                var s=new Scene();s.Key(Alt);s.Tap(65);s.Key(Alt,true);s.Key(Alt);s.Tap(Tab);s.Key(Alt,true);
                Check(Keys(s.Steps()).SequenceEqual([D(Alt),D(65),U(65),U(Alt)]),"Earlier Alt gesture removed");
            }),
            ("Repeated task switching and navigation arrows removed", () => {
                var s=new Scene();s.Key(Alt);s.Tap(Tab);s.Key(Shift);s.Tap(Tab);s.Tap(39);s.Key(Shift,true);s.Key(Alt,true);
                s.Tap(C,B);var nodes=s.Finish();Check(nodes.Count==1&&nodes[0].SessionTarget!.Handle==B&&Keys(nodes[0].Steps).SequenceEqual([D(C),U(C)]),"Task switching created input/nodes");
            }),
            ("Win+Tab then target input records only the target input", () => {
                var s=new Scene();s.Key(Win);s.Tap(Tab);s.Key(Win,true);s.Tap(C,B);var nodes=s.Finish();
                Check(nodes.Count==1&&nodes[0].SessionTarget!.Handle==B&&Keys(nodes[0].Steps).SequenceEqual([D(C),U(C)]),"Win navigation leaked");
            }),
            ("Tab and Shift+Tab remain ordinary application input", () => {
                var s=new Scene();s.Tap(Tab);s.Key(Shift);s.Tap(Tab);s.Key(Shift,true);
                Check(Keys(s.Steps()).SequenceEqual([D(Tab),U(Tab),D(Shift),D(Tab),U(Tab),U(Shift)]),"Tab over-filtered");
            }),
            ("Modifier begun outside target cannot leak across focus", () => {
                var s=new Scene();s.Key(Ctrl,false,UI);s.Tap(C);s.Key(Ctrl,true);Check(s.Recorder.Count==0,"Foreign prefix escaped");s.Tap(C);
                Check(Keys(s.Steps()).SequenceEqual([D(C),U(C)]),"Fresh key not restored");
            }),
            ("Ordinary drag keeps down, movement and matching up", () => {
                var s=new Scene();s.Mouse(ButtonAction.Down);s.Move();s.Mouse(ButtonAction.Up);var steps=s.Steps();
                Check(steps.Length==3&&steps[0].Action==ButtonAction.Down&&steps[1].Kind==InputKind.Move&&steps[2].Action==ButtonAction.Up&&!steps[2].UseCoordinates,"Drag damaged");
            }),
            ("Standalone modifier hold is balanced on finish", () => {
                var s=new Scene();s.Key(Shift);s.Milliseconds+=500;var steps=s.Steps();
                Check(Keys(steps).SequenceEqual([D(Shift),U(Shift)])&&steps[1].DelayBeforeMs==510,"Standalone modifier lost");
            }),
            ("Ctrl drag survives while foreign Ctrl click is excluded", () => {
                var s=new Scene();s.Key(Ctrl);s.Mouse(ButtonAction.Down);s.Move();s.Mouse(ButtonAction.Up);s.Key(Ctrl,true);var steps=s.Steps();
                Check(steps.Length==5&&steps[0].Input==Ctrl.ToString()&&steps[1].Kind==InputKind.Mouse&&steps[2].Kind==InputKind.Move&&steps[3].Action==ButtonAction.Up&&steps[4].Input==Ctrl.ToString(),"Ctrl drag lost");
                s=new Scene();s.Key(Ctrl,false,UI);s.Mouse(ButtonAction.Down);s.Mouse(ButtonAction.Up);s.Key(Ctrl,true);Check(s.Finish().Count==0,"Foreign modifier click escaped");
            }),
            ("Win+Space layout shortcut is not mistaken for Win+Tab", () => {
                var s=new Scene();s.Key(Win);s.Tap(32);s.Key(Win,true);
                Check(Keys(s.Steps()).SequenceEqual([D(Win),D(32),U(32),U(Win)]),"Win+Space removed");
            }),
            ("Activation drag and its release are fully excluded", () => {
                var s=new Scene();s.Mouse(ButtonAction.Down,UI,A);s.Move();s.Mouse(ButtonAction.Up);Check(s.Finish().Count==0,"Activation drag escaped");
            }),
            ("A to B activation click becomes node transition only", () => {
                var s=new Scene();s.Tap(C);s.Mouse(ButtonAction.Down,A,B);s.Mouse(ButtonAction.Up,B,B);s.Tap(65,B);var nodes=s.Finish();
                Check(nodes.Count==2&&nodes[0].SessionTarget!.Handle==A&&nodes[1].SessionTarget!.Handle==B&&nodes.All(n=>n.Steps.All(st=>st.Kind==InputKind.Key)),"Activation click retained");
            }),
            ("Stopping in own UI closes held input without recording UI click", () => {
                var s=new Scene();s.Key(Ctrl);s.Key(C);s.Mouse(ButtonAction.Down,A,UI);s.Mouse(ButtonAction.Up,UI,UI);
                Check(Keys(s.Steps()).SequenceEqual([D(Ctrl),D(C),U(C),U(Ctrl)]),"Unbalanced input/UI click");
            }),
            ("Mouse release outside target balances original down", () => {
                var s=new Scene();s.Mouse(ButtonAction.Down);s.Mouse(ButtonAction.Up,A,UI);var nodes=s.Finish();
                Check(nodes.Count==1&&nodes[0].Steps.Count==2&&nodes[0].Steps[1].Action==ButtonAction.Up,"Outside release lost");
            }),
            ("Time in MacroClicker does not become target delay", () => {
                var s=new Scene();s.Tap(C);s.Tap(65,UI);s.Milliseconds+=30000;s.Tap(C);var nodes=s.Finish();
                Check(nodes.Count==1&&nodes[0].Steps.Count==4&&nodes[0].Steps[2].DelayBeforeMs==0&&nodes[0].Steps.All(st=>st.DelayBeforeMs<100),"UI time or redundant node leaked");
            }),
            ("Recording limit leaves room for balanced cleanup", () => {
                var s=new Scene();for(var i=0;i<50000;i++)s.Key(C);var nodes=s.Finish();
                Check(s.Recorder.Full&&nodes.Sum(n=>n.Steps.Count)<=50000&&nodes[^1].Steps[^1].Action==ButtonAction.Up,"Limit lost cleanup");
            })
        };
        foreach(var test in tests) { test.Run(); Console.WriteLine("PASS Recording: "+test.Name); }
        Console.WriteLine($"PASS recording {tests.Length}/{tests.Length}. Real RecordingSession with supplied input/focus events; Windows hooks not executed.");
    }
}
