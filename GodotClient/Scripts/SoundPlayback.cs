using System.Collections.Generic;
using System.IO;
using Godot;
using Library;

namespace ZirconClient.Scripts;

/// <summary>共享场景音效播放器，供登录/选角等没有 GameScene 的阶段使用。</summary>
public static class SoundPlayback
{
    private static readonly Dictionary<SoundIndex, AudioStream> Cache = new();
    private static readonly Dictionary<SoundIndex, AudioStreamPlayer> Loops = new();

    // ---- 相位 BGM（EI 选角/建角屏的 SelChr.mp3 / CreateChr.mp3）----
    // 原版 PlayBGMEx（SoundUtil.pas）在播新曲前先 ClearBGM()，即**任意时刻只有一首 BGM**；
    // 用 BASS_SAMPLE_LOOP 建流，所以是循环播放。这里用单一实例镜像该语义。
    private static SoundIndex _bgmSound = SoundIndex.None;
    private static AudioStreamPlayer _bgmPlayer;

    public static void Play(Node owner, SoundIndex sound)
    {
        if (owner == null || sound == SoundIndex.None || !SoundCatalog.TryGet(sound, out var entry)) return;
        if (entry.Loop && Loops.TryGetValue(sound, out var existing) && GodotObject.IsInstanceValid(existing)) return;

        if (!TryLoad(sound, entry, out var stream)) return;

        if (entry.Loop && stream is AudioStreamWav wav)
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        // 按音效分类走对应总线（设置页 5 类音量/静音的消费端）
        var player = new AudioStreamPlayer { Stream = stream, Bus = ClientSettings.BusFor(entry.Category) };
        owner.AddChild(player);
        if (entry.Loop)
        {
            Loops[sound] = player;
            player.Finished += () =>
            {
                Loops.Remove(sound);
                if (GodotObject.IsInstanceValid(player)) player.QueueFree();
            };
        }
        else player.Finished += player.QueueFree;
        player.Play();
        GD.Print($"[Sound] 播放 {sound} ({entry.FileName}, loop={entry.Loop})");
    }

    /// <summary>
    /// 播放**唯一实例**的相位 BGM：先停掉上一首，再播新曲（镜像原版 PlayBGMEx 的
    /// ClearBGM → BASS_StreamCreateFile(BASS_SAMPLE_LOOP)）。同一首已在播则不动，
    /// 避免相位刷新时反复重头开始。
    /// </summary>
    public static void PlayBgm(Node owner, SoundIndex sound)
    {
        if (owner == null || sound == SoundIndex.None || !SoundCatalog.TryGet(sound, out var entry)) return;
        if (_bgmSound == sound && GodotObject.IsInstanceValid(_bgmPlayer) && _bgmPlayer.Playing) return;

        StopBgm();
        if (!TryLoad(sound, entry, out var stream)) return;

        if (stream is AudioStreamWav wav)
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        _bgmPlayer = new AudioStreamPlayer { Stream = stream, Bus = ClientSettings.BusFor(entry.Category) };
        _bgmSound = sound;
        owner.AddChild(_bgmPlayer);
        _bgmPlayer.Play();
        GD.Print($"[Sound] BGM 播放 {sound} ({entry.FileName}, 单实例循环)");
    }

    /// <summary>停止当前相位 BGM（原版 ClearBGM）。</summary>
    public static void StopBgm()
    {
        if (GodotObject.IsInstanceValid(_bgmPlayer)) _bgmPlayer.QueueFree();
        _bgmPlayer = null;
        _bgmSound = SoundIndex.None;
    }

    public static void Stop(SoundIndex sound)
    {
        if (!Loops.Remove(sound, out var player)) return;
        if (GodotObject.IsInstanceValid(player)) player.QueueFree();
    }

    private static bool TryLoad(SoundIndex sound, SoundEntry entry, out AudioStream stream)
    {
        if (Cache.TryGetValue(sound, out stream)) return true;
        var path = ProjectSettings.GlobalizePath("res://../Debug/Client/Sound/" + entry.FileName);
        stream = File.Exists(path) ? AudioStreamWav.LoadFromFile(path) : null;
        if (stream == null)
        {
            GD.PrintErr($"[Sound] 场景音效缺失: {sound} -> {path}");
            return false;
        }
        Cache[sound] = stream;
        return true;
    }
}
