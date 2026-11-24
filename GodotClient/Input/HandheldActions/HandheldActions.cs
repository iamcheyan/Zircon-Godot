#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZirconClient.HandheldActions;

public enum HandheldButton { X, Y, Up, Down, Left, Right, A, B, L1, R1, L2, R2 }
public enum ActionKind { Empty, Skill, Item, Menu }
public enum SkillActionMode { Active, Toggle }
public enum MenuAction { Interact, Cancel, OpenInventory, OpenCharacter, OpenSkills, OpenQuests, OpenSocial, OpenSystem, Target, OpenMap, FirstPage }
public enum HandheldTrigger { Tap, Hold, L1R1, L2R2 }

public sealed class ActionBinding
{
    [JsonRequired] public ActionKind Kind { get; set; }
    public string? StableId { get; set; }
    public SkillActionMode? SkillMode { get; set; }
    public MenuAction? MenuAction { get; set; }

    public static ActionBinding Empty() => new();
    public static ActionBinding Skill(string stableId, SkillActionMode mode) => new() { Kind = ActionKind.Skill, StableId = stableId, SkillMode = mode };
    public static ActionBinding Item(string stableId) => new() { Kind = ActionKind.Item, StableId = stableId };
    public static ActionBinding Menu(MenuAction action) => new() { Kind = ActionKind.Menu, MenuAction = action };
}

public sealed class HandheldActionPage
{
    [JsonRequired] public Dictionary<HandheldButton, ActionBinding> Bindings { get; set; } = DirectButtons().ToDictionary(x => x, _ => ActionBinding.Empty());
    internal static HandheldButton[] DirectButtons() => [HandheldButton.X, HandheldButton.Y, HandheldButton.Up, HandheldButton.Down, HandheldButton.Left, HandheldButton.Right];
}

public sealed class HandheldActionConfiguration
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public long LongPressThresholdMs { get; set; } = 600;
    [JsonRequired] public long ChordWindowMs { get; set; } = 80;
    [JsonRequired] public List<HandheldActionPage> Pages { get; set; } = [new(), new()];
    [JsonRequired] public Dictionary<string, ActionBinding> FixedSlots { get; set; } = new()
    {
        ["L2tap"] = ActionBinding.Empty(), ["L2hold"] = ActionBinding.Empty(),
        ["R2tap"] = ActionBinding.Empty(), ["R2hold"] = ActionBinding.Empty(),
        ["L1+R1"] = ActionBinding.Empty(), ["L2+R2"] = ActionBinding.Empty()
    };

    public static HandheldActionConfiguration CreateDefault() => new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != 1) errors.Add("Unsupported schema version.");
        if (LongPressThresholdMs is < HandheldActionEngine.MinThresholdMs or > HandheldActionEngine.MaxThresholdMs) errors.Add("Long-press threshold is outside the supported range.");
        if (ChordWindowMs < 0 || ChordWindowMs > LongPressThresholdMs) errors.Add("Chord window must be nonnegative and no longer than the long-press threshold.");
        if (Pages is not { Count: 2 }) errors.Add("Exactly two pages are required.");
        else for (int i = 0; i < Pages.Count; i++)
        {
            var page = Pages[i];
            if (page?.Bindings == null || page.Bindings.Count != 6 || HandheldActionPage.DirectButtons().Any(k => !page.Bindings.ContainsKey(k)) || page.Bindings.Keys.Any(k => !HandheldActionPage.DirectButtons().Contains(k)))
                errors.Add($"Page {i + 1} must contain exactly X, Y, Up, Down, Left and Right.");
            else foreach (var binding in page.Bindings.Values) ValidateBinding(binding, errors);
        }
        string[] slots = ["L2tap", "L2hold", "R2tap", "R2hold", "L1+R1", "L2+R2"];
        if (FixedSlots == null || FixedSlots.Count != slots.Length || slots.Any(k => !FixedSlots.ContainsKey(k)) || FixedSlots.Keys.Any(k => !slots.Contains(k)))
            errors.Add("Fixed slots must contain the six supported triggers.");
        else foreach (var binding in FixedSlots.Values) ValidateBinding(binding, errors);
        return errors;
    }

    private static void ValidateBinding(ActionBinding? binding, List<string> errors)
    {
        if (binding == null) { errors.Add("Null action binding."); return; }
        if (!Enum.IsDefined(binding.Kind)) { errors.Add("Unknown action kind."); return; }
        switch (binding.Kind)
        {
            case ActionKind.Empty:
                if (binding.StableId != null || binding.MenuAction != null || binding.SkillMode != null) errors.Add("Empty binding has payload.");
                break;
            case ActionKind.Skill:
                if (string.IsNullOrWhiteSpace(binding.StableId) || binding.StableId.Length > 160 || binding.SkillMode is not (SkillActionMode.Active or SkillActionMode.Toggle) || binding.MenuAction != null) errors.Add("Invalid skill binding; passive skills are not executable bindings.");
                break;
            case ActionKind.Item:
                if (string.IsNullOrWhiteSpace(binding.StableId) || binding.StableId.Length > 160 || binding.MenuAction != null || binding.SkillMode != null) errors.Add("Invalid stable item binding.");
                break;
            case ActionKind.Menu:
                if (binding.MenuAction is not { } action || !Enum.IsDefined(action) || binding.StableId != null || binding.SkillMode != null) errors.Add("Invalid menu action.");
                break;
        }
    }
}

public sealed record ActionIntent(ActionKind Kind, string? ActionId, SkillActionMode? SkillMode, MenuAction? MenuAction, HandheldTrigger Trigger, int Page);
public sealed record HandheldActionSnapshot(int CurrentPage, IReadOnlySet<HandheldButton> Pressed, IReadOnlySet<HandheldButton> Pending, double LongPressProgress, ActionIntent? LastTrigger);

public sealed class HandheldActionEngine
{
    public const long MinThresholdMs = 100;
    public const long MaxThresholdMs = 5000;
    private HandheldActionConfiguration _configuration = null!;
    private long _longPressThresholdMs, _chordWindowMs;
    private readonly Dictionary<HandheldButton, PressState> _down = new();
    private readonly HashSet<HandheldButton> _blockedUntilRelease = [];
    private readonly List<ActionIntent> _intents = [];
    private int _page;
    private bool _menuOwnsInput;
    private double _lastNow = -1;
    private ActionIntent? _lastTrigger;

    private sealed class PressState(double at)
    {
        public double At { get; } = at;
        public bool HoldFired { get; set; }
        public bool Suppressed { get; set; }
    }

    public HandheldActionEngine(HandheldActionConfiguration configuration, long? longPressThresholdMs = null, long? chordWindowMs = null)
        => ApplyConfiguration(configuration, longPressThresholdMs, chordWindowMs);

    public void FirstPage() => _page = 0;

    public void Reload(HandheldActionConfiguration configuration)
    {
        ApplyConfiguration(configuration, null, null);
        CancelPending();
        FirstPage();
        _lastTrigger = null;
    }

    private void ApplyConfiguration(HandheldActionConfiguration configuration, long? longPressThresholdMs, long? chordWindowMs)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = configuration.Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
        long longPress = longPressThresholdMs ?? configuration.LongPressThresholdMs;
        long chordWindow = chordWindowMs ?? configuration.ChordWindowMs;
        if (longPress is < MinThresholdMs or > MaxThresholdMs) throw new ArgumentOutOfRangeException(nameof(longPressThresholdMs));
        if (chordWindow < 0 || chordWindow > longPress) throw new ArgumentOutOfRangeException(nameof(chordWindowMs));
        _configuration = configuration;
        _longPressThresholdMs = longPress;
        _chordWindowMs = chordWindow;
    }

    public void Press(HandheldButton button, double nowMs)
    {
        CheckTime(nowMs);
        if (_blockedUntilRelease.Contains(button) || _down.ContainsKey(button))
        {
            Tick(nowMs);
            return;
        }
        if (_menuOwnsInput)
        {
            Tick(nowMs);
            _blockedUntilRelease.Add(button);
            return;
        }

        var other = Partner(button);
        if (other is { } paired && _down.TryGetValue(paired, out var heldPartner) && heldPartner.Suppressed)
        {
            Tick(nowMs);
            _blockedUntilRelease.Add(button);
            return;
        }
        PressState? partner = other is { } partnerButton && _down.TryGetValue(partnerButton, out var found) ? found : null;
        bool chord = partner != null && !partner.Suppressed && !partner.HoldFired
            && nowMs - partner.At <= _chordWindowMs;
        if (partner != null && chord) partner.Suppressed = true;
        Tick(nowMs);

        _down[button] = new PressState(nowMs) { Suppressed = chord };
        if (HandheldActionPage.DirectButtons().Contains(button))
            Emit(CurrentBinding(button), HandheldTrigger.Tap);
        if (chord)
        {
            if (button is HandheldButton.L1 or HandheldButton.R1) Emit(_configuration.FixedSlots["L1+R1"], HandheldTrigger.L1R1);
            else Emit(_configuration.FixedSlots["L2+R2"], HandheldTrigger.L2R2);
        }
    }

    public void Release(HandheldButton button, double nowMs)
    {
        CheckTime(nowMs);
        Tick(nowMs);
        if (_blockedUntilRelease.Remove(button)) return;
        if (!_down.TryGetValue(button, out var state)) return;
        if (state.Suppressed) { _down.Remove(button); return; }
        if (button is HandheldButton.L1 or HandheldButton.R1)
        {
            _page = (_page + 1) % 2;
            _down.Remove(button);
            return;
        }
        if (button is HandheldButton.L2 or HandheldButton.R2)
        {
            if (nowMs - state.At >= _longPressThresholdMs)
            {
                if (!state.HoldFired) Emit(_configuration.FixedSlots[button == HandheldButton.L2 ? "L2hold" : "R2hold"], HandheldTrigger.Hold);
            }
            else Emit(_configuration.FixedSlots[button == HandheldButton.L2 ? "L2tap" : "R2tap"], HandheldTrigger.Tap);
            _down.Remove(button);
            return;
        }
        if (button is HandheldButton.A or HandheldButton.B)
        {
            Emit(ActionBinding.Menu(button == HandheldButton.A ? MenuAction.Interact : MenuAction.Cancel), HandheldTrigger.Tap);
            _down.Remove(button);
            return;
        }
        _down.Remove(button);
    }

    public void Tick(double nowMs)
    {
        CheckTime(nowMs);
        foreach (var pair in _down.ToArray())
        {
            HandheldButton button = pair.Key;
            PressState state = pair.Value;
            if (state.Suppressed) continue;
            if (button is HandheldButton.L2 or HandheldButton.R2)
            {
                if (!state.HoldFired && nowMs - state.At >= _longPressThresholdMs)
                {
                    state.HoldFired = true;
                    Emit(_configuration.FixedSlots[button == HandheldButton.L2 ? "L2hold" : "R2hold"], HandheldTrigger.Hold);
                }
            }
        }
    }

    public void SetMenuOwnsInput(bool ownsInput, double nowMs)
    {
        CheckTime(nowMs);
        if (ownsInput) CancelPending();
        _menuOwnsInput = ownsInput;
    }
    public void FocusLost(double nowMs) { CheckTime(nowMs); CancelPending(); }
    public void CancelPending()
    {
        foreach (var key in _down.Keys) _blockedUntilRelease.Add(key);
        _down.Clear();
        _intents.Clear();
    }

    public IReadOnlyList<ActionIntent> DrainIntents()
    {
        var result = _intents.ToArray(); _intents.Clear(); return result;
    }

    public HandheldActionSnapshot Snapshot(double nowMs)
    {
        CheckTime(nowMs);
        var pending = _down.Where(kv => !kv.Value.Suppressed && !kv.Value.HoldFired
            && !HandheldActionPage.DirectButtons().Contains(kv.Key));
        double progress = _down.Where(kv => (kv.Key is HandheldButton.L2 or HandheldButton.R2) && !kv.Value.Suppressed)
            .Select(kv => Math.Clamp((nowMs - kv.Value.At) / _longPressThresholdMs, 0, 1)).DefaultIfEmpty(0).Max();
        return new(_page, _down.Keys.ToFrozenSet(), pending.Select(kv => kv.Key).ToFrozenSet(), progress, _lastTrigger);
    }

    private ActionBinding CurrentBinding(HandheldButton button) => _configuration.Pages[_page].Bindings[button];
    private static HandheldButton? Partner(HandheldButton button) => button switch { HandheldButton.L1 => HandheldButton.R1, HandheldButton.R1 => HandheldButton.L1, HandheldButton.L2 => HandheldButton.R2, HandheldButton.R2 => HandheldButton.L2, _ => null };
    private void Emit(ActionBinding binding, HandheldTrigger trigger)
    {
        if (binding.Kind == ActionKind.Empty) return;
        _lastTrigger = new(binding.Kind, binding.StableId, binding.SkillMode, binding.MenuAction, trigger, _page);
        _intents.Add(_lastTrigger);
    }
    private void CheckTime(double nowMs)
    {
        if (!double.IsFinite(nowMs) || nowMs < 0 || nowMs < _lastNow) throw new ArgumentOutOfRangeException(nameof(nowMs), "Time must be finite, nonnegative and monotonic.");
        _lastNow = nowMs;
    }
}

public sealed record SaveResult(bool Success, string? Error = null);

public static class HandheldActionConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static HandheldActionConfiguration Load(string path)
    {
        var text = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<HandheldActionConfiguration>(text, JsonOptions) ?? throw new InvalidDataException("Configuration is empty.");
        var errors = config.Validate();
        if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors));
        return config;
    }
    public static SaveResult Save(string path, HandheldActionConfiguration configuration)
    {
        var errors = configuration.Validate();
        if (errors.Count != 0) return new(false, string.Join(" ", errors));
        string? tempPath = null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath)!;
            tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(directory);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(configuration, JsonOptions));
            if (File.Exists(fullPath)) File.Move(tempPath, fullPath, true); else File.Move(tempPath, fullPath);
            return new(true);
        }
        catch (Exception ex) { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } return new(false, ex.Message); }
    }
}
