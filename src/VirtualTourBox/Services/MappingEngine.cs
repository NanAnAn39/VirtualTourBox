using VirtualTourBox.Models;
namespace VirtualTourBox.Services;

public sealed class MappingEngine
{
    readonly Dictionary<string,long> lastTriggered = new();
    readonly Dictionary<string,long> pressedSince = new();
    readonly HashSet<string> previousPressed = new();
    // Injectable monotonic clock keeps gesture/repeat timing immune to wall-clock corrections.
    public Func<long> ClockMs { get; set; } = () => Environment.TickCount64;
    public int HoldDelayMs { get; set; } = 400;
    sealed class HoldGesture
    {
        public long DownAt;
        public bool Down;
        public bool Held;
        public long? PendingClickAt;
    }
    readonly Dictionary<string,HoldGesture> holdGestures = new();
    public int RepeatDelayMs { get; set; } = 500;
    public int RepeatIntervalMs { get; set; } = 150;
    public bool Enabled { get; set; }
    public Func<string,bool>? SendIpadKey { get; set; }
    public Func<string,bool>? SendIpadCombo { get; set; }
    public Func<bool, bool, bool>? SetIpadSmudge { get; set; }
    bool smudgeHeld;
    bool heldLegacy;
    bool temporaryEraserHeld;
    string? temporaryEraserTarget;
    public MappingProfile? Profile { get; set; }
    public string Status { get; private set; } = "未开始";
    readonly HashSet<string> rawPrevious = new();
    readonly HashSet<string> comboConsumed = new();
    readonly Dictionary<string, long> pendingClicks = new();

    bool waitForRelease = true;
    public void Reset() { holdGestures.Clear(); pressedSince.Clear(); comboConsumed.Clear(); rawPrevious.Clear(); pendingClicks.Clear(); ReleaseTemporaryEraser(); ReleaseSmudge(); waitForRelease=true; lastTriggered.Clear(); previousPressed.Clear(); Status="已重置"; }
    bool SendTool(string key, string? target)
    {
        if (target == "iPad") return SendIpadKey?.Invoke(key.ToLowerInvariant()) == true;
        if (target == "Windows") return KeyboardOutput.SendShortcut(key).StartsWith("已注入");
        return false;
    }

    void ReleaseTemporaryEraser()
    {
        if (!temporaryEraserHeld) return;
        SendTool("B", temporaryEraserTarget);
        temporaryEraserHeld = false;
        temporaryEraserTarget = null;
    }

    void ReleaseSmudge()
    {
        if (!smudgeHeld) return;
        SetIpadSmudge?.Invoke(false, heldLegacy);
        smudgeHeld = false;
    }
    public void Update(IReadOnlySet<string> rawPressed)
    {
        if (!Enabled || Profile is null || Profile.Target is not ("Windows" or "iPad"))
        {
            Reset();
            return;
        }
        if (waitForRelease)
        {
            rawPrevious.UnionWith(rawPressed);
            if (rawPressed.Count != 0) return;
            rawPrevious.Clear();
        }
        var effective = rawPressed.ToHashSet();
        comboConsumed.RemoveWhere(button => !rawPressed.Contains(button));
        foreach (var combo in Profile.Bindings.Where(pair => pair.Key.Contains("+") && pair.Value.Action != "None")
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var parts = combo.Key.Split('+');
            if (parts.Length != 2 || !parts.All(rawPressed.Contains)) continue;
            effective.Add(combo.Key);
            foreach (var button in parts)
            {
                comboConsumed.Add(button);
                pendingClicks.Remove(button);
            }
        }
        effective.ExceptWith(comboConsumed);
        long now = ClockMs();
        var holdButtons = Profile.Bindings.Where(pair => pair.Key.EndsWith(".Hold") && pair.Value.Action != "None")
            .Select(pair => pair.Key[..^5]).ToHashSet();
        foreach (var button in holdGestures.Keys.Except(holdButtons).ToList()) holdGestures.Remove(button);
        foreach (var button in holdButtons)
        {
            effective.Remove(button);
            pendingClicks.Remove(button);
            if (comboConsumed.Contains(button))
            {
                holdGestures.Remove(button);
                continue;
            }
            // Matched history pairs preserve immediate taps and the configured repeat delay.
            // They are one continuous action, unlike brush -> temporary-tool gestures.
            bool hasAssignedDouble=Profile.Bindings.TryGetValue(button+".Double",out var doubleBinding)&&doubleBinding.Action!="None";
            bool historyPair=Profile.Bindings.TryGetValue(button,out var tapBinding) &&
                ((tapBinding.Action=="Undo"&&Profile.Bindings[button+".Hold"].Action=="UndoRepeat")||
                 (tapBinding.Action=="Redo"&&Profile.Bindings[button+".Hold"].Action=="RedoRepeat"));
            if(historyPair&&!hasAssignedDouble)
            {
                if(rawPressed.Contains(button))effective.Add(button+".Hold");
                holdGestures.Remove(button);
                continue;
            }
            if (!holdGestures.TryGetValue(button, out var gesture))
                holdGestures[button] = gesture = new HoldGesture();
            bool down = rawPressed.Contains(button);
            bool hasDouble = Profile.Bindings.TryGetValue(button+".Double", out var twice) && twice.Action != "None";
            if (gesture.PendingClickAt is long at && now-at > 300 && !down)
            {
                effective.Add(button);
                gesture.PendingClickAt = null;
            }
            if (down && !gesture.Down)
            {
                gesture.DownAt=now;
                gesture.Held=false;
            }
            if (down && now-gesture.DownAt >= HoldDelayMs)
            {
                gesture.Held=true;
                gesture.PendingClickAt=null;
                effective.Add(button+".Hold");
            }
            if (!down && gesture.Down)
            {
                if (!gesture.Held && now-gesture.DownAt < HoldDelayMs)
                {
                    if (!hasDouble) effective.Add(button);
                    else if (gesture.PendingClickAt is long first && now-first <= 300)
                    {
                        gesture.PendingClickAt=null;
                        effective.Add(button+".Double");
                    }
                    else gesture.PendingClickAt=now;
                }
                gesture.Held=false;
            }
            gesture.Down=down;
        }
        foreach (var input in Profile.Bindings.Keys.Where(key => key.EndsWith(".Double")).ToList())
        {
            string button = input[..^7];
            if (holdButtons.Contains(button)) continue;
            if (Profile.Bindings[input].Action == "None") continue;
            if (Profile.Bindings.TryGetValue(button, out var single) &&
                single.Action is "Smudge" or "SmudgeLegacy" or "TemporaryEraser")
                continue;
            effective.Remove(button);
            if (comboConsumed.Contains(button))
            {
                pendingClicks.Remove(button);
                continue;
            }
            if (rawPressed.Contains(button) && !rawPrevious.Contains(button))
            {
                if (pendingClicks.TryGetValue(button, out var first) && now-first <= 300)
                {
                    pendingClicks.Remove(button);

                    effective.Add(input);
                }
                else pendingClicks[button] = now;
            }

            if (pendingClicks.TryGetValue(button, out var started) && now-started > 300)
            {
                effective.Add(button);
                pendingClicks.Remove(button);
            }
        }
        rawPrevious.Clear();
        rawPrevious.UnionWith(rawPressed);
        UpdateCore(effective);
    }

    void UpdateCore(IReadOnlySet<string> currentlyPressed)
    {
        if (!Enabled || Profile is null || Profile.Target is not ("Windows" or "iPad"))
        {
            ReleaseTemporaryEraser();
            ReleaseSmudge();
            previousPressed.Clear();
            return;
        }
        if (waitForRelease)
        {
            if (currentlyPressed.Count != 0) return;
            waitForRelease = false;
        }
        bool wantsEraser = currentlyPressed.Any(input =>
            Profile.Bindings.TryGetValue(input, out var binding) && binding.Action == "TemporaryEraser");
        if (wantsEraser)
        {
            ReleaseSmudge();
            if (!temporaryEraserHeld)
            {
                temporaryEraserTarget = Profile.Target;
                temporaryEraserHeld = SendTool("E", temporaryEraserTarget);
            }
            Status = temporaryEraserHeld ? "按住临时橡皮擦；松开切回画笔" : "临时橡皮擦发送失败，请检查连接";
            previousPressed.UnionWith(currentlyPressed);
            return;
        }
        ReleaseTemporaryEraser();
        var smudgeActions = Profile.Target == "iPad"
            ? currentlyPressed.Where(input => Profile.Bindings.ContainsKey(input))
                .Select(input => Profile.Bindings[input].Action)
                .Where(action => action is "Smudge" or "SmudgeLegacy").Distinct().ToList()
            : new List<string>();
        if (smudgeActions.Count > 1)
        {
            ReleaseSmudge();
            Status = "两种涂抹模式同时按下，请松开后只使用一种";
            previousPressed.UnionWith(currentlyPressed);
            return;
        }
        bool wantsSmudge = smudgeActions.Count == 1;
        if (wantsSmudge)
        {
            bool legacy = smudgeActions[0] == "SmudgeLegacy";
            if (smudgeHeld && heldLegacy != legacy) ReleaseSmudge();
            heldLegacy = legacy;
            if (!smudgeHeld) smudgeHeld = SetIpadSmudge?.Invoke(true, heldLegacy) == true;
            Status = smudgeHeld ? "按住临时涂抹：请用 Apple Pencil 绘制；松开恢复" : "涂抹保持失败：请使用支持工具保持的新 CLI 并检查连接";
            previousPressed.UnionWith(currentlyPressed);
            return;
        }
        ReleaseSmudge();
        var now=ClockMs();
        foreach (var released in pressedSince.Keys.Where(key => !currentlyPressed.Contains(key)).ToList())
            pressedSince.Remove(released);
        foreach (var input in currentlyPressed)
        {
            if (!Profile.Bindings.TryGetValue(input,out var binding) || binding.Action is "None") continue;
            if (binding.Action is "PanCanvas" or "RotateCanvas" or "ZoomIn" or "ZoomOut")
            {
                Status = "该画布动作尚未接入；当前通道仅支持键盘与鼠标，请勿作为已实现功能验收";
                continue;
            }
            bool newlyPressed = !previousPressed.Contains(input);
            if (newlyPressed) pressedSince[input] = now;
            bool historyRepeat = binding.Action is "UndoRepeat" or "RedoRepeat" &&
                pressedSince.TryGetValue(input, out var downAt) && (now-downAt) >= RepeatDelayMs &&
                (!lastTriggered.TryGetValue(input, out var sentAt) || (now-sentAt) >= RepeatIntervalMs);
            // Repeating only for DPad, not for one-shot ABXY buttons.
            bool repeating = (binding.Action is "BrushLarger" or "BrushSmaller" || binding.Action == "CustomKey" && input.StartsWith("DPad")) && (!lastTriggered.TryGetValue(input,out var previous) || (now-previous) >= RepeatIntervalMs);
            if (!newlyPressed && !repeating && !historyRepeat) continue;
            string? shortcut = binding.Action switch
            {
                "Undo" or "UndoRepeat" => "Ctrl+Z", "Redo" or "RedoRepeat" => "Ctrl+Shift+Z", "Brush" => "B", "Eraser" => "E",
                "Selection" => "S", "Transform" => "V", "Layers" => "L", "Colors" => "C",
                "BrushLarger" => "OemCloseBrackets", "BrushSmaller" => "OemOpenBrackets",
                "QuickMenu" => "Space", "Smudge" => null, "CustomKey" => binding.Shortcut, _ => null
            };
            if (binding.Action == "Smudge")
            {
                Status = "涂抹尚未绑定可验证的 Procreate 快捷键；未发送";
                lastTriggered[input] = now;
                continue;
            }
            if (shortcut is null) continue;
            bool succeeded;
            if (Profile.Target == "iPad")
            {
                // The optional CLI supports single keys. Do not silently turn a Cmd shortcut
                // into another gesture or claim that it was sent successfully.
                if (binding.Action is "Undo" or "Redo" or "UndoRepeat" or "RedoRepeat")
                {
                    succeeded = SendIpadCombo?.Invoke(binding.Action is "Undo" or "UndoRepeat" ? "gui+z" : "gui+shift+z") == true;
                    Status = succeeded ? $"{input} → {(binding.Action is "Undo" or "UndoRepeat" ? "Cmd+Z" : "Cmd+Shift+Z")}（已提交）" : "iPad 组合键需要增强版 BleHid CLI：未发送";
                    lastTriggered[input] = now;
                    continue;
                }
                if (shortcut.Contains('+'))
                {
                    Status = "iPad 自定义组合键尚未支持：未发送";
                    lastTriggered[input] = now;
                    continue;
                }
                succeeded = SendIpadKey?.Invoke(shortcut.ToLowerInvariant()) == true;
            }
            else
            {
                Status = $"{DateTime.Now:HH:mm:ss} · {Profile.Name} / Windows · {input} → {shortcut} · {KeyboardOutput.SendShortcut(shortcut)}";
                lastTriggered[input] = now;
                continue;
            }
            Status=succeeded ? $"{input} → {shortcut}" : $"快捷键无效/发送失败: {shortcut}";
            lastTriggered[input]=now;
        }
        previousPressed.Clear();
        previousPressed.UnionWith(currentlyPressed);
    }
}






