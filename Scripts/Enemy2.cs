using Godot;
using test2.Scripts;

public partial class Enemy2 : CharacterBody2D, IDamageable
{
	[Export] public int MaxHp = 100;

	[Export] public float PatrolSpeed = 60f;
	[Export] public float ChaseSpeed = 110f;

	[Export] public float Gravity = 1200f;
	[Export] public float MaxFallSpeed = 900f;

	[Export] public float TurnPauseTime = 1.0f;
	[Export] public float TurnCooldownTime = 0.2f; // анти-зацикливание

	[Export] public float AttackCooldown = 1.0f;
	[Export] public int AttackDamage = 10;

	[Export] public float HurtLockTime = 0.5f;

	// Если спрайт "смотрит влево" при Visual.Scale.X = +1 -> -1, иначе +1
	[Export] public int AssetForward = -1;

	private int _hp;

	private Node2D _visual;
	private AnimationPlayer _anim;

	private Area2D _hitBox;

	private Area2D _playerDetector;
	private Area2D _attackRange;

	private RayCast2D _wallRay;
	private RayCast2D _floorRay;

	private Node2D _player;

	private int _dir = -1;

	private float _turnPauseTimer;
	private bool _turnQueued;
	private float _turnCooldownTimer;

	private float _attackTimer;
	private float _hurtLockTimer;

	private bool _isDead;
	private bool _isAttacking;
	private bool _playerInAttackRange;

	public override void _Ready()
	{
		_hp = MaxHp;

		_visual = GetNode<Node2D>("Visual");
		_anim = GetNode<AnimationPlayer>("AnimationPlayer");

		_hitBox = GetNode<Area2D>("Visual/HitBox");

		_playerDetector = GetNode<Area2D>("Visual/PlayerDetector");
		_attackRange = GetNode<Area2D>("Visual/AttackRange");

		_wallRay = GetNode<RayCast2D>("Visual/WallRay");
		_floorRay = GetNode<RayCast2D>("Visual/FloorRay");

		_playerDetector.BodyEntered += OnPlayerDetectorBodyEntered;
		_playerDetector.BodyExited += OnPlayerDetectorBodyExited;

		_attackRange.BodyEntered += OnAttackRangeBodyEntered;
		_attackRange.BodyExited += OnAttackRangeBodyExited;

		_hitBox.AreaEntered += OnHitBoxAreaEntered;
		_anim.AnimationFinished += OnAnimationFinished;

		ApplyVisualFlip();
		PlayIfNotPlaying("idle");
	}

	public override void _ExitTree()
	{
		_playerDetector.BodyEntered -= OnPlayerDetectorBodyEntered;
		_playerDetector.BodyExited -= OnPlayerDetectorBodyExited;

		_attackRange.BodyEntered -= OnAttackRangeBodyEntered;
		_attackRange.BodyExited -= OnAttackRangeBodyExited;

		_hitBox.AreaEntered -= OnHitBoxAreaEntered;
		_anim.AnimationFinished -= OnAnimationFinished;
	}

	public override void _PhysicsProcess(double delta)
	{
		var dt = (float)delta;

		if (_isDead)
			return;

		TickTimers(dt);
		ApplyGravity(dt);

		if (_isAttacking || _hurtLockTimer > 0f)
		{
			Velocity = Velocity with { X = 0f };
			MoveAndSlide();
			return;
		}

		// Пауза перед разворотом
		if (_turnPauseTimer > 0f)
		{
			Velocity = Velocity with { X = 0f };
			MoveAndSlide();
			PlayIfNotPlaying("idle");
			return;
		}

		// Выполнить отложенный разворот (после паузы)
		if (_turnQueued)
		{
			_turnQueued = false;
			TurnAround();
			_turnCooldownTimer = TurnCooldownTime;
		}

		var hasPlayer = GodotObject.IsInstanceValid(_player);

		if (hasPlayer)
		{
			FacePlayer();

			if (_playerInAttackRange)
			{
				TryAttack();
				Velocity = Velocity with { X = 0f };
			}
			else
			{
				Velocity = Velocity with { X = _dir * ChaseSpeed };
				PlayIfNotPlaying("move");
			}
		}
		else
		{
			Patrol();
		}

		MoveAndSlide();
	}

	private void TickTimers(float dt)
	{
		if (_turnPauseTimer > 0f) _turnPauseTimer = Mathf.Max(0f, _turnPauseTimer - dt);
		if (_turnCooldownTimer > 0f) _turnCooldownTimer = Mathf.Max(0f, _turnCooldownTimer - dt);

		if (_attackTimer > 0f) _attackTimer = Mathf.Max(0f, _attackTimer - dt);
		if (_hurtLockTimer > 0f) _hurtLockTimer = Mathf.Max(0f, _hurtLockTimer - dt);
	}

	private void ApplyGravity(float dt)
	{
		if (IsOnFloor())
		{
			if (Velocity.Y > 0f)
				Velocity = Velocity with { Y = 0f };
			return;
		}

		var y = Mathf.Min(Velocity.Y + Gravity * dt, MaxFallSpeed);
		Velocity = Velocity with { Y = y };
	}

	private void Patrol()
	{
		// Антизацикливание: сразу после разворота даём врагу “шанс” отойти
		if (_turnCooldownTimer > 0f)
		{
			Velocity = Velocity with { X = _dir * PatrolSpeed };
			PlayIfNotPlaying("move");
			return;
		}

		var hitWall = _wallRay.IsColliding();
		var hasFloorAhead = _floorRay.IsColliding();

		// Поворачиваемся только когда стоим на полу, иначе в воздухе у края может спамить
		if (IsOnFloor() && (hitWall || !hasFloorAhead))
		{
			StartTurnPause();
			return;
		}

		Velocity = Velocity with { X = _dir * PatrolSpeed };
		PlayIfNotPlaying("move");
	}

	private void StartTurnPause()
	{
		if (_turnPauseTimer > 0f || _turnQueued)
			return;

		Velocity = Velocity with { X = 0f };
		_turnPauseTimer = TurnPauseTime;
		_turnQueued = true;

		PlayIfNotPlaying("idle");
	}

	private void TurnAround()
	{
		_dir *= -1;
		ApplyVisualFlip();
	}

	private void FacePlayer()
	{
		if (!GodotObject.IsInstanceValid(_player))
			return;

		var dx = _player.GlobalPosition.X - GlobalPosition.X;
		var desiredDir = dx >= 0 ? 1 : -1;

		if (desiredDir != _dir)
		{
			_dir = desiredDir;
			ApplyVisualFlip();
		}
	}

	private void ApplyVisualFlip()
	{
		// scale влияет на детей (Visual флипает хитбоксы/детекторы вместе) [page:1]
		_visual.Scale = new Vector2(_dir * AssetForward, 1f);
	}

	private void TryAttack()
	{
		if (_attackTimer > 0f) return;
		if (_hurtLockTimer > 0f) return;

		_attackTimer = AttackCooldown;
		_isAttacking = true;

		PlayIfNotPlaying("attack");
	}

	private void OnAnimationFinished(StringName animName)
	{
		var a = animName.ToString();

		if (a == "attack")
		{
			_isAttacking = false;
			return;
		}

		if (a == "die")
			QueueFree();
	}

	public void TakeDamage(int amount)
	{
		if (_isDead) return;

		_hp -= Mathf.Max(0, amount);

		if (_hp <= 0)
		{
			Die();
			return;
		}

		_hurtLockTimer = Mathf.Max(_hurtLockTimer, HurtLockTime);
		_isAttacking = false;

		PlayIfNotPlaying("hurt");
	}

	private void Die()
	{
		_isDead = true;
		Velocity = Vector2.Zero;

		PlayIfNotPlaying("die");
	}

	private void OnPlayerDetectorBodyEntered(Node2D body)
	{
		if (body.IsInGroup("player"))
			_player = body;
	}

	private void OnPlayerDetectorBodyExited(Node2D body)
	{
		if (_player == body)
			_player = null;
	}

	private void OnAttackRangeBodyEntered(Node2D body)
	{
		if (body.IsInGroup("player"))
			_playerInAttackRange = true;
	}

	private void OnAttackRangeBodyExited(Node2D body)
	{
		if (body.IsInGroup("player"))
			_playerInAttackRange = false;
	}

	private void OnHitBoxAreaEntered(Area2D otherArea)
	{
		if (_isDead) return;

		var owner = otherArea.Owner;
		if (owner is IDamageable dmg)
			dmg.TakeDamage(AttackDamage);
	}

	private void PlayIfNotPlaying(string anim)
	{
		if (_isDead && anim != "die")
			return;

		if (_anim.CurrentAnimation == anim)
			return;

		_anim.Play(anim);
	}
}
