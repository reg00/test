using Godot;
using test2.Scripts;

public partial class Enemy : CharacterBody2D, IDamageable
{
    [Export] public int Hp = 100;
    
    [Export] public float PatrolSpeed = 100f;
    [Export] public float ChaseSpeed = 150f;

    [Export] public float Gravity = 1200f;
    [Export] public float MaxFallSpeed = 900f;

    [Export] public bool CanChase = true;

    // Узлы (пути можно не экспортить, если названия фиксированные)
    [Export] public NodePath DetectAreaPath = "DetectArea";
    [Export] public NodePath WallRayPath = "WallRay";
    [Export] public NodePath FloorRayPath = "FloorRay";

    private Area2D _detectArea;
    private RayCast2D _wallRay;
    private RayCast2D _floorRay;

    private Node2D _target; // игрок
    private int _dir = -1;  // -1 влево, +1 вправо

    public override void _Ready()
    {
        _detectArea = GetNode<Area2D>(DetectAreaPath);
        _wallRay = GetNode<RayCast2D>(WallRayPath);
        _floorRay = GetNode<RayCast2D>(FloorRayPath);

        // Area2D: сигналы входа/выхода тел [page:2]
        _detectArea.BodyEntered += OnDetectBodyEntered;
        _detectArea.BodyExited += OnDetectBodyExited;

        ApplyDirToRays();
    }

    public override void _ExitTree()
    {
        _detectArea.BodyEntered -= OnDetectBodyEntered;
        _detectArea.BodyExited -= OnDetectBodyExited;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        ApplyGravity(dt);

        if (CanChase && IsTargetValid())
            Chase();
        else
            Patrol();

        // Движение CharacterBody2D [page:1]
        MoveAndSlide();
    }

    private void ApplyGravity(float dt)
    {
        if (IsOnFloor())
        {
            if (Velocity.Y > 0f)
                Velocity = Velocity with { Y = 0f };
            return;
        }

        float y = Mathf.Min(Velocity.Y + Gravity * dt, MaxFallSpeed);
        Velocity = Velocity with { Y = y };
    }

    private void Patrol()
    {
        // Разворот у стены или у края (RayCast2D)
        bool hitWall = _wallRay.IsColliding();
        bool hasFloorAhead = _floorRay.IsColliding();

        if (hitWall || !hasFloorAhead)
            TurnAround();

        Velocity = Velocity with { X = _dir * PatrolSpeed };
    }

    private void Chase()
    {
        float dx = _target.GlobalPosition.X - GlobalPosition.X;
        int desiredDir = dx >= 0 ? 1 : -1;

        if (desiredDir != _dir)
        {
            _dir = desiredDir;
            ApplyDirToRays();
        }

        Velocity = Velocity with { X = _dir * ChaseSpeed };
    }

    private void TurnAround()
    {
        _dir *= -1;
        ApplyDirToRays();
    }

    private void ApplyDirToRays()
    {
        // Повернуть RayCast2D вперёд по направлению движения
        _wallRay.TargetPosition = new Vector2(Mathf.Abs(_wallRay.TargetPosition.X) * _dir, _wallRay.TargetPosition.Y);
        _floorRay.TargetPosition = new Vector2(Mathf.Abs(_floorRay.TargetPosition.X) * _dir, _floorRay.TargetPosition.Y);
    }

    private bool IsTargetValid()
    {
        return GodotObject.IsInstanceValid(_target);
    }

    private void OnDetectBodyEntered(Node body)
    {
        if (!CanChase) return;

        // Самый простой вариант: игрок добавлен в группу "player"
        if (body is Node2D n2d && body.IsInGroup("player"))
            _target = n2d;
    }

    private void OnDetectBodyExited(Node body)
    {
        if (_target == body)
            _target = null;
    }

    public void TakeDamage(int amount)
    {
        Hp -= amount;
        GD.Print($"Current HP: {Hp}");
        if(Hp <= 0)
            QueueFree();
    }
}
