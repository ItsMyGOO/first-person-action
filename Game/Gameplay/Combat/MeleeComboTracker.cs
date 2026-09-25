using System.Collections.Generic;

namespace FirstPersonAction.Combat;

public enum ComboStagePhase
{
    Inactive,
    Startup,
    Active,
    Recovery,
}

/// <summary>
/// 普攻连段跟踪器（纯逻辑，可单元测试；规格第 3 节行动层的连段核心）。
/// 状态推进：Startup → Active（命中窗口）→ Recovery（过 CancelAfter 可接下一段）→ Inactive。
/// 本类不做任何 Godot 调用；控制器读取其状态驱动位移与表现。
/// </summary>
public sealed class MeleeComboTracker
{
    private readonly IReadOnlyList<ComboStageData> _stages;
    private bool _hitApplied;

    public MeleeComboTracker(IReadOnlyList<ComboStageData> stages) => _stages = stages;

    public ComboStagePhase Phase { get; private set; } = ComboStagePhase.Inactive;
    public int StageIndex { get; private set; } = -1;
    public float PhaseElapsed { get; private set; }

    public bool IsActive => Phase != ComboStagePhase.Inactive;
    public bool CanApplyHit => Phase == ComboStagePhase.Active && !_hitApplied;
    public bool IsInCancelWindow =>
        Phase == ComboStagePhase.Recovery && PhaseElapsed >= _stages[StageIndex].CancelAfter;
    public bool CanStartAction => !IsActive || IsInCancelWindow;

    private ComboStageData Current => _stages[StageIndex];

    /// <summary>
    /// 接受一次攻击输入：空闲则从第 1 段开始；取消窗口内则链下一段（末段后回第 1 段）。
    /// 条件不满足返回 false（输入留在缓冲里继续等待）。
    /// </summary>
    public bool TryAdvance()
    {
        if (!CanStartAction)
        {
            return false;
        }

        int next = IsActive ? StageIndex + 1 : 0;
        if (next >= _stages.Count)
        {
            next = 0;
        }

        StageIndex = next;
        Phase = ComboStagePhase.Startup;
        PhaseElapsed = 0f;
        _hitApplied = false;
        return true;
    }

    /// <summary>结算本段命中：返回 true 表示本次生效（每段最多一次）。</summary>
    public bool ConsumeHit()
    {
        if (!CanApplyHit)
        {
            return false;
        }

        _hitApplied = true;
        return true;
    }

    /// <summary>外部打断（闪避/受击/将来的处决）→ 回到空闲。</summary>
    public void Reset()
    {
        Phase = ComboStagePhase.Inactive;
        StageIndex = -1;
        PhaseElapsed = 0f;
        _hitApplied = false;
    }

    public void Tick(float dt)
    {
        if (!IsActive)
        {
            return;
        }

        // 循环推进：单帧的 dt 可能跨越多个阶段，剩余时间必须结转
        PhaseElapsed += dt;
        while (IsActive)
        {
            float duration = Phase switch
            {
                ComboStagePhase.Startup => Current.Startup,
                ComboStagePhase.Active => Current.Active,
                ComboStagePhase.Recovery => Current.Recovery,
                _ => 0f,
            };
            if (PhaseElapsed < duration)
            {
                return;
            }

            PhaseElapsed -= duration;
            Phase = Phase switch
            {
                ComboStagePhase.Startup => ComboStagePhase.Active,
                ComboStagePhase.Active => ComboStagePhase.Recovery,
                _ => ComboStagePhase.Inactive,
            };
            if (Phase == ComboStagePhase.Inactive)
            {
                StageIndex = -1;
                PhaseElapsed = 0f;
            }
        }
    }
}
