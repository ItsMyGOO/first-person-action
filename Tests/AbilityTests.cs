using System.Collections.Generic;
using FirstPersonAction.Combat;
using FirstPersonAction.Core;
using Godot;
using Xunit;

namespace FirstPersonAction.Tests;

public class ForcedMovementTests
{
    [Fact]
    public void Linear_CoversTotalDistance_OverDuration()
    {
        var fm = ForcedMovement.Linear(new Vector3(0, 0, -1), 6f, 0.5f, ForcedMovementEase.Linear);
        Vector3 total = Vector3.Zero;
        for (int i = 0; i < 30; i++)
        {
            total += fm.Tick(1f / 60f);
        }

        Assert.True(fm.IsFinished);
        Assert.Equal(6f, total.Length(), 2);
        Assert.Equal(Vector3.Zero, fm.Tick(1f / 60f)); // 结束后不再位移
    }

    [Fact]
    public void SmoothEase_AlsoCoversTotalDistance()
    {
        var fm = ForcedMovement.Linear(new Vector3(0, 0, -1), 6f, 0.5f, ForcedMovementEase.Smooth);
        Vector3 total = Vector3.Zero;
        for (int i = 0; i < 30; i++)
        {
            total += fm.Tick(1f / 60f);
        }

        Assert.Equal(6f, total.Length(), 2);
    }

    [Fact]
    public void SmoothEase_StartsSlow()
    {
        var fm = ForcedMovement.Linear(new Vector3(0, 0, -1), 6f, 0.5f, ForcedMovementEase.Smooth);
        Vector3 firstFrame = fm.Tick(1f / 60f);
        var fmLinear = ForcedMovement.Linear(
            new Vector3(0, 0, -1),
            6f,
            0.5f,
            ForcedMovementEase.Linear
        );
        Vector3 linearFirst = fmLinear.Tick(1f / 60f);
        Assert.True(firstFrame.Length() < linearFirst.Length());
    }
}

public class AbilityTests
{
    private sealed class FakeTarget : ICombatTarget
    {
        public Vector3 Center;
        public int HitCount;

        public Vector3 CenterProperty => Center;
        public bool CanBeHit => true;

        Vector3 ICombatTarget.Center => Center;

        void ICombatTarget.ApplyHit(in HitData hit) => HitCount++;
    }

    /// <summary>记录调用的假上下文（纯逻辑测试用，不触碰 Godot 对象）。</summary>
    private sealed class FakeContext : IAbilityContext
    {
        public Vector3 Position = Vector3.Zero;
        public Vector3 Forward = new(0, 0, -1);
        public bool OnFloor = true;
        public float MovementForce = 100f;
        public List<ICombatTarget> Targets = new();
        public ForcedMovement? Requested;
        public float LaunchedY;
        public bool SuperArmor;
        public bool SpatialExempt;
        public int HitLandedNotifications;
        public int LeapLandedNotifications;

        Vector3 IAbilityContext.BodyPosition => Position;
        Vector3 IAbilityContext.ForwardFlat => Forward;
        bool IAbilityContext.IsOnFloor => OnFloor;
        float IAbilityContext.CurrentMovementForce
        {
            get => MovementForce;
            set => MovementForce = value;
        }

        void IAbilityContext.RequestForcedMovement(ForcedMovement movement) => Requested = movement;

        void IAbilityContext.CancelForcedMovement() => Requested = null;

        void IAbilityContext.LaunchUp(float velocityY) => LaunchedY = velocityY;

        void IAbilityContext.SetSuperArmor(bool enabled) => SuperArmor = enabled;

        void IAbilityContext.SetSpatialExempt(bool enabled) => SpatialExempt = enabled;

        List<ICombatTarget> IAbilityContext.QueryTargets() => Targets;

        void IAbilityContext.NotifyHitLanded(int hitCount) => HitLandedNotifications++;

        void IAbilityContext.NotifyLeapLanded() => LeapLandedNotifications++;
    }

    private static SkillDefinition Def(SkillKind kind) => AbilityFactory.CreateDefinition(kind);

    [Fact]
    public void Cooldown_GatesRecast()
    {
        var ability = AbilityFactory.Create(SkillKind.Whirlwind);
        var ctx = new FakeContext();
        Assert.True(ability.TryCast(ctx));
        Assert.False(ability.CanCast);
        ability.Update(1f, ctx); // CD 5s 只走 1s
        Assert.False(ability.CanCast);
        ability.Update(4.1f, ctx);
        Assert.True(ability.CanCast);
    }

    [Fact]
    public void Whirlwind_AppliesHitOnceInActiveWindow_ThenEnds()
    {
        var ability = AbilityFactory.Create(SkillKind.Whirlwind);
        var ctx = new FakeContext();
        var target = new FakeTarget { Center = new Vector3(0, 1, -1.5f) };
        ctx.Targets.Add(target);

        Assert.True(ability.TryCast(ctx));
        ability.Update(0.30f, ctx); // 前摇 0.15 + 主动 0.15
        ability.Update(0.30f, ctx); // 推进到后摇中
        Assert.Equal(1, target.HitCount); // 只结算一次
        Assert.Equal(1, ctx.HitLandedNotifications);

        ability.Update(0.40f, ctx); // 后摇结束
        Assert.False(ability.IsCasting);
        Assert.Equal(1, target.HitCount); // 不会再结算
    }

    [Fact]
    public void Charge_RaisesMovementForce_DuringDash_ThenEnds()
    {
        var ability = AbilityFactory.Create(SkillKind.Charge);
        var ctx = new FakeContext();
        SkillDefinition def = Def(SkillKind.Charge);

        Assert.True(ability.TryCast(ctx));
        ability.Update(0.1f, ctx);
        Assert.Equal(def.PushForce, ctx.MovementForce); // 冲刺期间推力拉满
        Assert.True(ctx.SuperArmor);
        Assert.NotNull(ctx.Requested);

        ability.Update(def.MoveDuration, ctx);
        Assert.False(ability.IsCasting); // 冲刺结束（推力由控制器恢复）
    }

    [Fact]
    public void LeapSlam_ImpactsOnceOnLanding()
    {
        var ability = AbilityFactory.Create(SkillKind.LeapSlam);
        var ctx = new FakeContext();
        var target = new FakeTarget { Center = new Vector3(0, 1, -2f) };
        ctx.Targets.Add(target);
        SkillDefinition def = Def(SkillKind.LeapSlam);

        Assert.True(ability.TryCast(ctx));
        Assert.True(ctx.LaunchedY > 0); // 起跳
        ctx.OnFloor = false;
        ability.Update(def.MoveDuration, ctx);
        Assert.Equal(0, target.HitCount); // 空中不结算
        ctx.OnFloor = true;
        ability.Update(0.05f, ctx);
        Assert.Equal(1, target.HitCount);
        Assert.Equal(1, ctx.LeapLandedNotifications); // 落地反馈触发一次
        Assert.False(ctx.SuperArmor); // 落地后霸体解除
        Assert.False(ability.IsCasting);
    }

    [Fact]
    public void Backstep_MovesBackward()
    {
        var ability = AbilityFactory.Create(SkillKind.Backstep);
        var ctx = new FakeContext();
        Assert.True(ability.TryCast(ctx));
        Assert.NotNull(ctx.Requested);
        // 方向 = 前方的反向（-Z 前方 → 请求位移沿 +Z）
        Assert.True(ctx.Requested!.Direction.Z > 0);
    }

    [Fact]
    public void DashThrough_ExemptsSpatial_WithoutRaisingPushForce()
    {
        var ability = AbilityFactory.Create(SkillKind.DashThrough);
        var ctx = new FakeContext();
        SkillDefinition def = Def(SkillKind.DashThrough);

        Assert.True(ability.TryCast(ctx));
        Assert.True(ctx.SpatialExempt); // 穿人：冲刺期间对空间系统隐身
        Assert.NotNull(ctx.Requested); // 直线强制位移
        ability.Update(0.1f, ctx);
        Assert.Equal(100f, ctx.MovementForce); // 疾行不改推力——穿人靠豁免而非推挤

        ability.Update(def.MoveDuration, ctx);
        Assert.False(ability.IsCasting);
    }

    [Fact]
    public void DashThrough_Cancel_ClearsExemptionAndMovement()
    {
        var ability = AbilityFactory.Create(SkillKind.DashThrough);
        var ctx = new FakeContext();
        Assert.True(ability.TryCast(ctx));
        ability.Cancel(ctx);
        Assert.False(ctx.SpatialExempt); // 打断路径不留豁免残渣（否则永久穿人）
        Assert.Null(ctx.Requested);
        Assert.False(ability.IsCasting);
    }
}
