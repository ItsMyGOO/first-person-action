using System.Collections.Generic;
using Godot;

namespace FirstPersonAction.Core;

/// <summary>
/// 音效播放器（autoload）：占位合成音效（Tools/generate_placeholder_sfx.gd 生成，
/// 真实资产到位后同名替换即可）。8 路 AudioStreamPlayer 池复用，全部路由到 SFX 总线。
/// 事件站点（Player 近战/箭矢/处决/冲刺、EnemyAI 死亡、UI 点击）直接 Play(name)。
/// </summary>
public partial class Sfx : Node
{
    private const string SfxDir = "res://Game/Audio";
    private const int PoolSize = 8;
    private const float DefaultVolumeDb = -6f;

    private static readonly string[] SoundNames =
    {
        "swing",
        "dash",
        "hit_melee",
        "hit_arrow",
        "arrow_release",
        "execute",
        "hurt",
        "enemy_die",
        "ui_click",
    };

    public static Sfx? Instance { get; private set; }

    private readonly Dictionary<string, AudioStreamWav> _streams = new();
    private readonly List<AudioStreamPlayer> _pool = new();

    /// <summary>最近一次请求播放的音效名（冒烟断言事件接线用，可重置）。</summary>
    public string LastPlayed { get; set; } = "";

    public override void _Ready()
    {
        Instance = this;
        foreach (string name in SoundNames)
        {
            var stream = ResourceLoader.Load<AudioStreamWav>($"{SfxDir}/{name}.wav");
            if (stream == null)
            {
                GD.PushError(
                    $"[Sfx] 音效加载失败：{SfxDir}/{name}.wav（占位音可用 Tools 生成器重建）"
                );
                continue;
            }

            _streams[name] = stream;
        }

        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer
            {
                Bus = "SFX",
                VolumeDb = DefaultVolumeDb,
                MaxPolyphony = 1,
            };
            AddChild(player);
            _pool.Add(player);
        }
    }

    public bool Has(string name) => _streams.ContainsKey(name);

    /// <summary>播放一个音效（池满则复用最早开始的那路）。</summary>
    public static void Play(string name)
    {
        Sfx? sfx = Instance;
        if (sfx == null || !sfx._streams.TryGetValue(name, out AudioStreamWav? stream))
        {
            return; // 静默缺音：占位管线缺失不该崩玩法
        }

        sfx.LastPlayed = name;
        AudioStreamPlayer? player = null;
        float earliest = float.MaxValue;
        foreach (AudioStreamPlayer candidate in sfx._pool)
        {
            if (!candidate.Playing)
            {
                player = candidate;
                break;
            }

            if (candidate.GetPlaybackPosition() < earliest)
            {
                earliest = candidate.GetPlaybackPosition();
                player = candidate;
            }
        }

        player!.Stream = stream;
        player.Play();
    }
}
