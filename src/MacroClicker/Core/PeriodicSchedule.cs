namespace MacroClicker;

// One schedule per main node, created afresh for each run. No clocks, waits or Windows input.
internal sealed class PeriodicSchedule(IReadOnlyList<PeriodicAction> rules)
{
    public long CompletedIterations { get; private set; }
    public TimeSpan MainTime { get; private set; }

    public IReadOnlyList<PeriodicAction> Complete(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var previous = MainTime.Ticks;
        MainTime += duration;
        CompletedIterations++;
        // Snapshot all due rules before any additional action is executed. Crossing multiple
        // time buckets produces one execution; the next future multiple remains the next threshold.
        return rules.Where(r => r.Enabled && (r.Trigger == PeriodicTrigger.Cycles
            ? CompletedIterations % r.EveryCycles == 0
            : MainTime.Ticks / (r.EveryMs * TimeSpan.TicksPerMillisecond) >
              previous / (r.EveryMs * TimeSpan.TicksPerMillisecond))).ToArray();
    }
}
