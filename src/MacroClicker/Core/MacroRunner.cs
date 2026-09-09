using System.Diagnostics;

namespace MacroClicker;

internal sealed class MacroRunner(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly List<ushort> _keys = [];
    private readonly List<string> _buttons = [];
    private long? _lastMouseRelease;

    public async Task RunAsync(MacroDocument doc, Action<string> status, CancellationToken token)
    {
        MacroStorage.Validate(doc);
        if (doc.Nodes.Count == 0) throw new InvalidDataException("Добавьте узел приложения.");
        // Проверяем все назначения до отправки первого действия.
        var targets = doc.AllNodes(enabledOnly: true).Distinct().ToDictionary(n => n,
            n => n.UseActiveWindow ? null : WindowTargeting.Resolve(n));
        var schedules = doc.Nodes.Select(n => new PeriodicSchedule(n.PeriodicActions)).ToArray();
        _lastMouseRelease = null;
        try
        {
            long cycle = 0;
            WindowTarget? expected = null;
            while (doc.RepeatForever || cycle < doc.RepeatCount)
            {
                for (var n = 0; n < doc.Nodes.Count; n++)
                {
                    token.ThrowIfCancellationRequested();
                    if (expected is not null) WindowTargeting.EnsureForeground(expected);
                    ReleaseAll();
                    var node = doc.Nodes[n];
                    var started = _time.GetTimestamp();
                    var target = await ExecuteNodeAsync(node, targets[node], doc.SafeMousePauses,
                        $"Цикл {cycle + 1} · узел {n + 1}/{doc.Nodes.Count} «{node.Name}»", status, token);
                    var due = schedules[n].Complete(_time.GetElapsedTime(started));
                    expected = target;
                    foreach (var rule in due)
                    {
                        token.ThrowIfCancellationRequested();
                        WindowTargeting.EnsureForeground(target);
                        var extra = await ExecuteNodeAsync(rule.Node, targets[rule.Node], doc.SafeMousePauses,
                            $"Узел «{node.Name}» · повторение {schedules[n].CompletedIterations} завершено · дополнительно «{rule.Node.Name}»", status, token);
                        await TransitionAsync(rule.Node, extra, status, token);
                        // Check the additional window BEFORE restoring the parent, so a manual
                        // focus change or a closed window cannot be hidden by automatic activation.
                        token.ThrowIfCancellationRequested();
                        WindowTargeting.EnsureForeground(extra);
                        if (extra.Handle != target.Handle || extra.ProcessId != target.ProcessId)
                        {
                            status($"Возврат в окно основного узла «{node.Name}»…");
                            await WindowTargeting.ActivateAsync(target, token);
                        }
                        WindowTargeting.EnsureForeground(target);
                    }
                    if (n + 1 < doc.Nodes.Count || doc.RepeatForever || cycle + 1 < doc.RepeatCount)
                        await TransitionAsync(node, target, status, token);
                }
                ReleaseAll();
                cycle++;
            }
            status($"Готово · выполнено циклов: {cycle}");
        }
        finally { ReleaseAll(); }
    }

    private async Task<WindowTarget> ExecuteNodeAsync(MacroNode node, WindowTarget? resolved, bool safePauses,
        string context, Action<string> status, CancellationToken token)
    {
        var step = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            ReleaseAll();
            var target = resolved ?? WindowTargeting.GetActiveApplication();
            if (!node.UseActiveWindow)
            {
                status(context + ": переключение окна…");
                await WindowTargeting.ActivateAsync(target, token);
            }
            for (; step < node.Steps.Count; step++)
            {
                status($"{context} · действие {step + 1}/{node.Steps.Count}");
                await ExecuteAsync(node.Steps[step], target, safePauses, token);
            }
            ReleaseAll();
            token.ThrowIfCancellationRequested();
            WindowTargeting.EnsureForeground(target);
            return target;
        }
        catch (TargetWindowException e) { throw new TargetWindowException($"{context} · действие {Math.Min(step + 1, node.Steps.Count)}: {e.Message}"); }
        catch (InvalidDataException e) { throw new InvalidDataException($"{context} · действие {step + 1}: {e.Message}", e); }
        catch (InvalidOperationException e) { throw new InvalidOperationException($"{context} · действие {step + 1}: {e.Message}", e); }
        catch (System.ComponentModel.Win32Exception e) { throw new System.ComponentModel.Win32Exception(e.NativeErrorCode, $"{context} · действие {step + 1}: {e.Message}"); }
    }

    private async Task TransitionAsync(MacroNode node, WindowTarget target, Action<string> status, CancellationToken token)
    {
        if (node.TransitionDelayMs == 0) return;
        status($"Узел «{node.Name}» · пауза перед следующим узлом: {node.TransitionDelayMs} мс");
        try { await WaitAsync(node.TransitionDelayMs, target, token); }
        catch (TargetWindowException e) { throw new TargetWindowException($"Узел «{node.Name}» · пауза перехода: {e.Message}"); }
    }

    private async Task WaitAsync(int milliseconds, WindowTarget target, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        do
        {
            token.ThrowIfCancellationRequested();
            WindowTargeting.EnsureForeground(target);
            var remaining = milliseconds - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (remaining <= 0) break;
            await Task.Delay((int)Math.Min(50, Math.Ceiling(remaining)), token);
        } while (true);
    }

    private async Task ExecuteAsync(MacroStep s, WindowTarget target, bool systemPause, CancellationToken token)
    {
        await WaitAsync(s.DelayBeforeMs, target, token);
        if (s.Kind == InputKind.Pause) { await WaitAsync(s.HoldMs, target, token); return; }
        if (s.Kind == InputKind.DoubleClick && _buttons.Count > 0)
            throw new InvalidDataException("Перед двойным кликом отпустите удерживаемые кнопки мыши отдельным шагом.");
        if (systemPause && s.Kind is InputKind.Mouse or InputKind.DoubleClick && s.Action != ButtonAction.Up && _lastMouseRelease is long released)
        {
            var remaining = InputSender.SafeMousePauseMs - Stopwatch.GetElapsedTime(released).TotalMilliseconds;
            if (remaining > 0) await WaitAsync((int)Math.Ceiling(remaining), target, token);
        }
        if (s.Kind is InputKind.Mouse or InputKind.DoubleClick or InputKind.Move or InputKind.Wheel)
        {
            if (s.UseCoordinates || s.Kind == InputKind.Move)
            {
                string error;
                var moved = s.CoordinateSpace == CoordinateSpace.Window
                    ? WindowTargeting.TryMoveCursorToClientPosition(target, s.X, s.Y, out error)
                    : WindowTargeting.TryMoveCursorToScreenPosition(s.X, s.Y, out error);
                if (!moved) throw new TargetWindowException(error);
                await WaitAsync(s.MoveDelayMs, target, token);
            }
            if (!WindowTargeting.IsCursorOverApplication(target))
                throw new TargetWindowException("Курсор вне выбранного приложения. Нажатие не отправлено.");
        }
        token.ThrowIfCancellationRequested();
        WindowTargeting.EnsureForeground(target);
        switch (s.Kind)
        {
            case InputKind.DoubleClick:
                // Two short taps form one action. Never insert the single-click
                // separation pause inside this pair; still guard every wait.
                var tapMs = Math.Clamp(InputSender.SystemDoubleClickMs / 4, 1, 40);
                for (var click = 0; click < 2; click++)
                {
                    await WaitAsync(0, target, token);
                    if (!WindowTargeting.IsCursorOverApplication(target))
                        throw new TargetWindowException("Курсор вне выбранного приложения. Двойной клик остановлен.");
                    _buttons.Add(s.Input); InputSender.MouseEvent(s.Input, false);
                    await WaitAsync(tapMs, target, token);
                    MouseUp(s.Input);
                    if (click == 0) await WaitAsync(tapMs, target, token);
                }
                break;
            case InputKind.Key:
                var chord = InputSender.ParseChord(s.Input);
                if (s.Action == ButtonAction.Up) { foreach (var key in chord.Reverse()) KeyUp(key); break; }
                foreach (var key in chord)
                {
                    if (!_keys.Contains(key)) _keys.Add(key);
                    InputSender.KeyEvent(key, false);
                }
                if (s.Action == ButtonAction.Tap)
                {
                    await WaitAsync(s.HoldMs, target, token);
                    foreach (var key in chord.Reverse()) KeyUp(key);
                }
                break;
            case InputKind.Mouse:
                if (s.Action == ButtonAction.Up) { MouseUp(s.Input); break; }
                if (!_buttons.Contains(s.Input)) _buttons.Add(s.Input);
                InputSender.MouseEvent(s.Input, false);
                if (s.Action == ButtonAction.Tap)
                {
                    await WaitAsync(s.HoldMs, target, token);
                    MouseUp(s.Input);
                }
                break;
            case InputKind.Wheel: InputSender.Wheel(s.WheelDelta, s.HorizontalWheel); break;
            case InputKind.Text:
                // UTF-16 surrogate pairs идут подряд; не используем буфер обмена.
                var text = s.Text.Replace("\r\n", "\n").Replace('\r', '\n');
                foreach (var rune in text.EnumerateRunes())
                {
                    await WaitAsync(0, target, token);
                    if (rune.Value is 10 or 9)
                    {
                        var key = (ushort)(rune.Value == 10 ? 13 : 9);
                        _keys.Add(key); InputSender.KeyEvent(key, false); KeyUp(key);
                    }
                    else foreach (var unit in rune.ToString())
                    {
                        InputSender.UnicodeEvent(unit, false);
                        InputSender.UnicodeEvent(unit, true);
                    }
                    await WaitAsync(s.CharacterDelayMs, target, token);
                }
                break;
        }
        await WaitAsync(s.DelayAfterMs, target, token);
    }
    private void KeyUp(ushort key) { InputSender.KeyEvent(key, true); _keys.Remove(key); }
    private void MouseUp(string button)
    {
        InputSender.MouseEvent(button, true); _buttons.Remove(button); _lastMouseRelease = Stopwatch.GetTimestamp();
    }
    private void ReleaseAll()
    {
        // При ошибке одной кнопки всё равно пробуем отпустить остальные.
        Exception? failure = null;
        foreach (var key in _keys.ToArray().Reverse()) try { KeyUp(key); } catch (Exception e) { failure = e; }
        foreach (var button in _buttons.ToArray()) try { MouseUp(button); } catch (Exception e) { failure = e; }
        if (failure is not null) throw new InvalidOperationException("Windows не приняла отпускание кнопки. Отпустите её вручную.", failure);
    }
}
