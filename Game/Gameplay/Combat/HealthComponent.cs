using Godot;

namespace FirstPersonAction.Combat;

/// <summary>血量组件：受击方通用。死亡只发信号，表现由宿主决定。</summary>
public partial class HealthComponent : Node
{
    [Signal]
    public delegate void DamagedEventHandler(float amount, float remaining);

    [Signal]
    public delegate void DiedEventHandler();

    [Export]
    public float MaxHealth = 100f;

    public float CurrentHealth { get; private set; }

    public bool IsDead => CurrentHealth <= 0f;

    public override void _Ready() => CurrentHealth = MaxHealth;

    /// <summary>运行时按角色定义设置上限并回满（须在宿主 _Ready 之前或紧后调用）。</summary>
    public void Init(float maxHealth)
    {
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
    }

    public void ApplyDamage(float amount)
    {
        if (IsDead)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        EmitSignal(SignalName.Damaged, amount, CurrentHealth);
        if (IsDead)
        {
            EmitSignal(SignalName.Died);
        }
    }
}
