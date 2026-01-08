using Godot;

namespace test2.Scripts;

[GlobalClass]
public partial class PlayerData : Resource
{
    [Export] public float Speed = 300.0f;
    [Export] public float JumpForce = 500.0f;

    [Export] public float Gravity = 800.0f;
    [Export] public float MaxFallSpeed = 500.0f;

    [Export] public float DashForce = 600.0f;
    [Export] public float DashDuration = 0.3f;

    [Export] public int ExtraAirJumps = 1;
    [Export] public float CoyoteTime = 0.10f;

    // Jump shaping (0..1 по прогрессу подъёма)
    [Export] public float AscentGravityMultiplierMin = 1.0f;  // сразу после отрыва
    [Export] public float AscentGravityMultiplierMax = 2.2f;  // ближе к вершине (сильнее “гасит”)
    [Export] public float FallGravityMultiplier = 2.4f;       // падение “тяжелее”, быстрее вниз
    [Export] public float AirJumpMultiplier = 0.85f;           // второй прыжок в 2 раза меньше
    
    [Export] public int ComboHits = 3;        // сколько ударов в серии (может меняться)
    [Export] public int[] AttackDamage = new int[] { 20, 30, 40 };
    [Export] public float ComboResetTime = 1f;    // через сколько секунд без атак сбрасывать на 1-й удар
    [Export] public string AttackAnimPrefix = "attack_"; // attack_1, attack_2, attack_3...

    [Export] public float WallSlideFallMultiplier = 0.3f;
    
    [Export] public float WallJumpForce  = 500f;
    [Export] public float WallJumpPush  = 300f;
    [Export] public float WallJumpLockTime  = 0.15f;
    
    // ========== DROP THROUGH ONE-WAY ==========
    [Export] public int OneWayPlatformLayer = 7;  // слой (1..32), где стоят твои one-way StaticBody2D
    [Export] public float DropThroughTime = 0.20f;
}