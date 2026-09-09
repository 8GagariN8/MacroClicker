using System.Text.Json;
using System.Text.Json.Nodes;
using MacroClicker;

internal static class PeriodicChecks
{
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(int ms) => _ticks += ms * TimeSpan.TicksPerMillisecond;
    }
    private static MacroStep Key(string input = "1", ButtonAction action = ButtonAction.Tap) =>
        new() { Kind = InputKind.Key, Input = input, Action = action, HoldMs = 1, DelayAfterMs = 0 };
    private static PeriodicAction Rule(int every = 1, string input = "Enter") => new()
    {
        EveryCycles = every, Node = new() { Name = "Extra", UseActiveWindow = true, Steps = [Key(input)] }
    };
    private static MacroDocument Doc(int count = 1, params PeriodicAction[] rules) => new()
    {
        RepeatCount = count, SafeMousePauses = false,
        Nodes = [new() { Name = "Parent", UseActiveWindow = true, Steps = [Key()], PeriodicActions = rules.ToList() }]
    };
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Fails<T>(Func<Task> body) where T : Exception
    { try { await body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static Task Run(MacroDocument doc, Action<string>? status = null, CancellationToken token = default, TimeProvider? clock = null) =>
        new MacroRunner(clock).RunAsync(doc, status ?? (_ => { }), token);
    private static string[] Downs() => Native.Events.Where(e => e.StartsWith("key:") && e.EndsWith(":down")).ToArray();

    public static async Task RunAll()
    {
        var tests = new (string Name, Func<Task> Body)[]
        {
            ("N=6: 5/6/7/11/12 passes preserve 1..5 and insert Enter after parent", async () =>
            {
                foreach (var count in new[] { 5, 6, 7, 11, 12 })
                {
                    Native.Reset(); var doc = Doc(count, Rule(6));
                    doc.Nodes[0].Steps = new[] { "1", "2", "3", "4", "5" }.Select(x => Key(x)).ToList();
                    doc.Nodes.Add(new() { Name = "Next", UseActiveWindow = true, Steps = [Key("C")] });
                    await Run(doc);
                    var expected = Enumerable.Range(1, count).SelectMany(i => new[] { 49, 50, 51, 52, 53 }
                        .Concat(i % 6 == 0 ? new[] { 13 } : Array.Empty<int>()).Concat(new[] { 67 }))
                        .Select(k => $"key:{k}:down");
                    Assert(Downs().SequenceEqual(expected), "Wrong order at " + count);
                    Assert(!Native.Events.Any(e => e.StartsWith("window:")), "Same-window rule switched application");
                }
            }),
            ("N=1, simultaneous rules, disabled rule and final iteration", async () =>
            {
                var disabled = Rule(1, "5"); disabled.Enabled = false; disabled.Node.ProcessName = "missing"; disabled.Node.UseActiveWindow = false;
                await Run(Doc(2, Rule(1, "2"), disabled, Rule(1, "3")));
                Assert(Downs().SequenceEqual(new[] { 49, 50, 51, 49, 50, 51 }.Select(k => $"key:{k}:down")), "Rules or final iteration lost");
            }),
            ("Each parent has its own attachments; no recursive execution", async () =>
            {
                var doc = Doc(3, Rule(2, "2"));
                doc.Nodes.Add(new() { Name = "Second", UseActiveWindow = true, Steps = [Key("3")], PeriodicActions = [Rule(3, "4")] });
                await Run(doc);
                Assert(Downs().SequenceEqual(new[] { 49, 51, 49, 50, 51, 49, 51, 52 }.Select(k => $"key:{k}:down")), "Schedules shared or wrong boundary");
            }),
            ("Time thresholds: below/exact/above, skipped buckets and ordered snapshot", () =>
            {
                var a = Rule(); a.Trigger = PeriodicTrigger.Time; a.EveryMs = 30000;
                var b = Rule(); b.Trigger = PeriodicTrigger.Time; b.EveryMs = 30000;
                var schedule = new PeriodicSchedule(new[] { a, b });
                Assert(schedule.Complete(TimeSpan.FromMilliseconds(29999)).Count == 0, "Early");
                Assert(schedule.Complete(TimeSpan.FromMilliseconds(1)).SequenceEqual(new[] { a, b }), "Exact or order");
                Assert(schedule.Complete(TimeSpan.FromMilliseconds(1)).Count == 0, "Double execution");
                schedule = new(new[] { a });
                Assert(schedule.Complete(TimeSpan.FromSeconds(95)).Count == 1, "Catch-up burst");
                Assert(schedule.Complete(TimeSpan.FromMilliseconds(24999)).Count == 0, "Future threshold early");
                Assert(schedule.Complete(TimeSpan.FromMilliseconds(1)).Count == 1, "120s threshold lost");
                Assert(schedule.CompletedIterations == 3, "Additional blocks counted");
                return Task.CompletedTask;
            }),
            ("Runner time excludes other nodes, additional work, return and transition delays", async () =>
            {
                var clock = new Clock(); clock.Advance(5000);
                var timed = Rule(); timed.Trigger = PeriodicTrigger.Time; timed.EveryMs = 1000;
                timed.Node.UseActiveWindow = false; timed.Node.Name = "B"; timed.Node.TransitionDelayMs = 1;
                var doc = Doc(6, timed); doc.Nodes[0].TransitionDelayMs = 1;
                doc.Nodes.Add(new() { Name = "Other", UseActiveWindow = true, Steps = [Key("C")] });
                Native.OnEvent = e => { if (e == "key:49:up") clock.Advance(400); else if (e == "key:13:up" || e == "key:67:up" || e.StartsWith("window:")) clock.Advance(100000); };
                await Run(doc, s => { if (s.Contains("пауза перед")) clock.Advance(100000); }, clock: clock);
                Assert(Downs().Count(e => e == "key:13:down") == 2, "Non-parent work advanced schedule");
                Assert(Downs().SequenceEqual(new[] {49,67,49,67,49,13,67,49,67,49,13,67,49,67}.Select(k => $"key:{k}:down")), "Wrong time boundary");
            }),
            ("Parent activation and intervals inside main actions count as main work", async () =>
            {
                var clock = new Clock(); var timed = Rule(); timed.Trigger = PeriodicTrigger.Time; timed.EveryMs = 1000;
                var doc = Doc(1, timed); doc.Nodes[0].UseActiveWindow = false;
                doc.Nodes[0].Steps.Add(new() { Kind = InputKind.Pause, HoldMs = 1 });
                Native.OnEvent = e => { if (e == "window:Parent") clock.Advance(600); };
                await Run(doc, s => { if (s.Contains("действие 2/2")) clock.Advance(400); }, clock: clock);
                Assert(Downs().Last() == "key:13:down", "Parent activation/pause interval excluded");
            }),
            ("A -> B -> A; next rule and active main receive input in A", async () =>
            {
                var b = Rule(); b.Node.Name = "B"; b.Node.UseActiveWindow = false;
                Native.Active = Native.Target("A");
                await Run(Doc(2, b, Rule(1, "2")));
                Assert(Native.Events.Where(e => e.StartsWith("window:")).SequenceEqual(new[] { "window:B", "window:A", "window:B", "window:A" }), "No safe return");
                Assert(Native.WindowEvents.Where(e => e.Contains(":key:") && e.EndsWith(":down")).SequenceEqual(
                    new[] { "A:key:49:down", "B:key:13:down", "A:key:50:down", "A:key:49:down", "B:key:13:down", "A:key:50:down" }), "Input sent to wrong app");
            }),
            ("Manual focus loss in extra prevents restore and next rule; release held keys", async () =>
            {
                var b = Rule(1, "Ctrl+C"); b.Node.UseActiveWindow = false; b.Node.Name = "B";
                Native.OnEvent = e => { if (e == "key:67:down") Native.Active = Native.Target("User"); };
                await Fails<TargetWindowException>(() => Run(Doc(2, b, Rule(1, "5"))));
                Assert(Native.Events.Contains("key:67:up") && Native.Events.Contains("key:17:up"), "Keys held");
                Assert(!Native.Events.Contains("window:active") && !Native.Events.Contains("key:53:down"), "Manual switch hidden");
            }),
            ("Closed parent or extra stops rather than continuing into another application", async () =>
            {
                foreach (var closeParent in new[] { true, false })
                {
                    Native.Reset(); var b = Rule(); b.Node.UseActiveWindow = false; b.Node.Name = "B";
                    Native.OnEvent = e => { if (e == "key:13:up") Native.Closed.Add(Native.Target(closeParent ? "active" : "B").Handle); };
                    await Fails<TargetWindowException>(() => Run(Doc(2, b, Rule(1, "5"))));
                    Assert(!Native.Events.Contains("key:53:down"), "Continued after closed window");
                }
            }),
            ("Cancel incomplete parent: no attached rule or completed iteration", async () =>
            {
                using var stop = new CancellationTokenSource(); var doc = Doc(2, Rule());
                Native.OnEvent = e => { if (e == "key:49:down") stop.Cancel(); };
                await Fails<OperationCanceledException>(() => Run(doc, token: stop.Token));
                Assert(Downs().SequenceEqual(new[] { "key:49:down" }) && Native.Events.Contains("key:49:up"), "Incomplete parent triggered rule or remained held");
            }),
            ("Cancel additional action stops the entire run and resets schedule on next start", async () =>
            {
                var runner = new MacroRunner(); var doc = Doc(3, Rule(2)); using var stop = new CancellationTokenSource();
                Native.OnEvent = e => { if (e == "key:13:down") stop.Cancel(); };
                await Fails<OperationCanceledException>(() => runner.RunAsync(doc, _ => { }, stop.Token));
                Assert(Native.Events.Last() == "key:13:up", "Extra remained held");
                Native.Reset(); doc.RepeatCount = 1; await runner.RunAsync(doc, _ => { }, default);
                Assert(Downs().SequenceEqual(new[] { "key:49:down" }), "Schedule survived restart");
            }),
            ("Main -> extra -> extra pause -> main pause -> next; no final main pause", async () =>
            {
                var rule = Rule(); rule.Node.TransitionDelayMs = 1; var doc = Doc(2, rule); doc.Nodes[0].TransitionDelayMs = 1;
                var sequence = new List<string>(); Native.OnEvent = e => { if (e.EndsWith(":down")) sequence.Add(e); };
                await Run(doc, s => { if (s.Contains("пауза перед")) sequence.Add(s.Contains("«Extra»") ? "extra pause" : "main pause"); });
                Assert(sequence.SequenceEqual(new[] { "key:49:down", "key:13:down", "extra pause", "main pause", "key:49:down", "key:13:down", "extra pause" }), "Transition ordering");
            }),
            ("Stop/focus guard applies to both transition pauses", async () =>
            {
                foreach (var extra in new[] { true, false }) foreach (var cancel in new[] { true, false })
                {
                    Native.Reset(); using var stop = new CancellationTokenSource(); var r = Rule();
                    var doc = Doc(2, r); if (extra) r.Node.TransitionDelayMs = 600000; else doc.Nodes[0].TransitionDelayMs = 600000;
                    Action<string> status = s => { if (s.Contains("пауза перед")) { if (cancel) stop.Cancel(); else Native.Focus = false; } };
                    if (cancel) await Fails<OperationCanceledException>(() => Run(doc, status, stop.Token));
                    else await Fails<TargetWindowException>(() => Run(doc, status));
                    Assert(Downs().Count(x => x == "key:49:down") == 1, "Next parent started during failed pause");
                }
            }),
            ("Enabled destinations are preflighted even when threshold is distant", async () =>
            {
                var r = Rule(100000); r.Node.ProcessName = "missing"; r.Node.UseActiveWindow = false;
                await Fails<TargetWindowException>(() => Run(Doc(1, r))); Assert(Native.Events.Count == 0, "Input before preflight");
            }),
            ("Mouse release history spans main and additional blocks", async () =>
            {
                var mouse = new MacroStep { Kind = InputKind.Mouse, Input = "Правая", HoldMs = 1, DelayAfterMs = 0 };
                var r = Rule(); r.Node.Steps = [mouse.Copy()]; var doc = Doc(1, r); doc.Nodes[0].Steps = [mouse]; doc.SafeMousePauses = true;
                await Run(doc);
                var gap = System.Diagnostics.Stopwatch.GetElapsedTime(Native.MouseTimes[1], Native.MouseTimes[2]).TotalMilliseconds;
                Assert(gap >= InputSender.SafeMousePauseMs - 1, "System mouse separation lost across blocks");
            }),
            ("JSON v1/v2 migration, v3 deep copy/save/load/trash restore and retention", () =>
            {
                var directory = Directory.CreateTempSubdirectory("periodic-roundtrip-");
                try
                {
                    var file = Path.Combine(directory.FullName, "macro.json");
                    foreach (var version in new[] { 1, 2 })
                    {
                        File.WriteAllText(file, $$"""{"Version":{{version}},"Steps":[{"Kind":"Key","Input":"1"}]}""");
                        var old = MacroStorage.Read(file); Assert(old.Version == 3 && old.Nodes.Count == 1 && old.Nodes[0].PeriodicActions.Count == 0, "Migration");
                        Assert(File.ReadAllText(file).Contains($"\"Version\":{version}"), "Migration rewrote library");
                    }
                    var doc = Doc(12, Rule(6)); doc.Nodes[0].TransitionDelayMs = 2000;
                    doc.Nodes[0].PeriodicActions[0].Node.SessionTarget = Native.Target("A");
                    var copy = doc.Copy(); copy.Nodes[0].PeriodicActions[0].Node.Steps[0].Input = "C";
                    Assert(doc.Nodes[0].PeriodicActions[0].Node.Steps[0].Input == "Enter" && copy.Nodes[0].PeriodicActions[0].Node.SessionTarget == Native.Target("A"), "Copy aliases or drops binding");
                    MacroStorage.Write(file, doc); var json = File.ReadAllText(file);
                    Assert(!json.Contains("SessionTarget") && !json.Contains("TargetId"), "Transient handle leaked");
                    var read = MacroStorage.Read(file); Assert(read.Version == 3 && read.Nodes[0].PeriodicActions[0].EveryCycles == 6 && read.Nodes[0].TransitionDelayMs == 2000, "Roundtrip");
                    var date = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
                    var archived = MacroStorage.Archive(directory.FullName, "macro.json", date);
                    Assert(MacroTrash.List(directory.FullName).Single().ExpiresAt == date.AddDays(30), "Retention changed");
                    var restored = MacroTrash.Restore(directory.FullName, Path.GetFileName(archived));
                    Assert(File.ReadAllText(restored) == json, "Trash lost fields");
                }
                finally { directory.Delete(true); }
                return Task.CompletedTask;
            }),
            ("Editor bridge roundtrip: main/extra bindings, JSON changes and foreign handle rejection", () =>
            {
                var doc = Doc(2, Rule());
                var a = Native.Target("A"); var b = Native.Target("B");
                doc.Nodes[0].ProcessName = doc.Nodes[0].WindowTitle = a.Name; doc.Nodes[0].SessionTarget = a;
                var extra = doc.Nodes[0].PeriodicActions[0].Node;
                extra.ProcessName = extra.WindowTitle = b.Name; extra.SessionTarget = b;
                var json = MacroEditorDocument.Export(doc);
                var handles = new Dictionary<string, WindowTarget> { [a.Handle.ToString()] = a, [b.Handle.ToString()] = b };
                var imported = MacroEditorDocument.Import(JsonSerializer.SerializeToElement(json), doc, handles);
                Assert(imported.Nodes[0].SessionTarget == a && imported.Nodes[0].PeriodicActions[0].Node.SessionTarget == b, "Editor lost attachment binding");
                json["Nodes"]![0]!["PeriodicActions"]![0]!.AsObject()["EveryCycles"] = 7;
                json["Nodes"]![0]!["PeriodicActions"]![0]!["Node"]!.AsObject().Remove("TargetId");
                imported = MacroEditorDocument.Import(JsonSerializer.SerializeToElement(json), doc, handles);
                Assert(imported.Nodes[0].PeriodicActions[0].EveryCycles == 7 && imported.Nodes[0].PeriodicActions[0].Node.SessionTarget == b, "JSON edit lost unchanged target");
                json["Nodes"]![0]!["PeriodicActions"]![0]!["Node"]!["TargetId"] = a.Handle.ToString();
                imported = MacroEditorDocument.Import(JsonSerializer.SerializeToElement(json), new(), handles);
                Assert(imported.Nodes[0].PeriodicActions[0].Node.SessionTarget is null, "Foreign binding trusted");
                return Task.CompletedTask;
            }),
            ("Validation boundaries, nulls, nesting, limits and missing JSON fields", async () =>
            {
                foreach (var n in new[] { 0, -1, 100001 })
                { var doc = Doc(1, Rule(n)); await Fails<InvalidDataException>(() => { MacroStorage.Validate(doc); return Task.CompletedTask; }); }
                foreach (var t in new[] { 999, 0, -1, 86400001 })
                { var doc = Doc(1, Rule()); doc.Nodes[0].PeriodicActions[0].EveryMs = t; await Fails<InvalidDataException>(() => { MacroStorage.Validate(doc); return Task.CompletedTask; }); }
                foreach (var t in new[] { 1000, 86400000 }) foreach (var n in new[] { 1, 100000 })
                { var doc = Doc(1, Rule(n)); doc.Nodes[0].PeriodicActions[0].EveryMs = t; MacroStorage.Validate(doc); }
                foreach (var change in new Action<MacroDocument>[] {
                    d => d.Nodes[0].PeriodicActions = null!, d => d.Nodes[0].PeriodicActions[0] = null!,
                    d => d.Nodes[0].PeriodicActions[0].Node = null!, d => d.Nodes[0].PeriodicActions[0].Node.Steps = [],
                    d => d.Nodes[0].Steps = [], d => d.Nodes[0].PeriodicActions[0].Node.PeriodicActions.Add(Rule()),
                    d => d.Nodes[0].PeriodicActions[0].Trigger = (PeriodicTrigger)55,
                    d => d.Nodes[0].TransitionDelayMs = -1, d => d.Nodes[0].PeriodicActions[0].Node.TransitionDelayMs = 600001,
                    d => d.Nodes[0].PeriodicActions = Enumerable.Range(0,101).Select(_ => Rule()).ToList(),
                    d => d.Nodes.AddRange(Enumerable.Range(0,999).Select(_ => new MacroNode())) })
                { var doc = Doc(1, Rule()); change(doc); await Fails<InvalidDataException>(() => { MacroStorage.Validate(doc); return Task.CompletedTask; }); }
                var max = Doc(1, Rule()); max.Nodes[0].TransitionDelayMs = 600000;
                max.Nodes.AddRange(Enumerable.Range(0,998).Select(_ => new MacroNode())); MacroStorage.Validate(max);
                var source = JsonSerializer.SerializeToNode(Doc(1, Rule()), MacroStorage.Options)!;
                foreach (var property in new[] { "EveryCycles", "EveryMs", "Enabled", "Trigger", "Node" })
                {
                    var missing = source.DeepClone(); missing["Nodes"]![0]!["PeriodicActions"]![0]!.AsObject().Remove(property);
                    await Fails<JsonException>(() => { missing.Deserialize<MacroDocument>(MacroStorage.Options); return Task.CompletedTask; });
                }
                foreach (var raw in new[] { "null", "1.5", "\"6\"", "2147483648" })
                {
                    var bad = source.DeepClone(); bad["Nodes"]![0]!["PeriodicActions"]![0]!["EveryCycles"] = JsonNode.Parse(raw);
                    await Fails<JsonException>(() => { bad.Deserialize<MacroDocument>(MacroStorage.Options); return Task.CompletedTask; });
                }
                var numeric = source.DeepClone(); numeric["Nodes"]![0]!["PeriodicActions"]![0]!["Trigger"] = 0;
                await Fails<JsonException>(() => { numeric.Deserialize<MacroDocument>(MacroStorage.Options); return Task.CompletedTask; });
            })
        };
        foreach (var (name, body) in tests) { Native.Reset(); await body(); Console.WriteLine("PASS Periodic: " + name); }
        Console.WriteLine($"PASS periodic {tests.Length}/{tests.Length}. Deterministic scheduling and fake Windows input; real Windows smoke still required.");
    }
}
