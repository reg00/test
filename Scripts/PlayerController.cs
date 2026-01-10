using Godot;
using test2.Scripts;

// ReSharper disable once CheckNamespace
public partial class PlayerController : CharacterBody2D, IDamageable
{
	// ========== EXPORTED ==========
	[Export] private PlayerData _playerData;

	// ========== NODES / STATE ==========
	private Node2D _visual;
	private AnimationPlayer _animationPlayer;
	private Area2D _hitBox;

	private RayCast2D _ledgeWallRay;
	private RayCast2D _ledgeTopRay;

	private CollisionShape2D _mainCollider;

	private bool _isFacingRight = true;
	private float _axisX;

	// Dash
	private bool _isDashing;
	private float _dashTimer;
	private bool _canDash = true;

	// Jump
	private int _airJumpsLeft;
	private float _coyoteTimer;

	// Attack / Combo
	private bool _isAttacking;
	private int _comboIndex;
	private float _comboResetTimer;
	private bool _attackQueued;

	// Wall slide / wall jump
	private bool _isWallSliding;
	private Vector2 _wallNormal;
	private float _wallJumpLock;

	// Drop-through one-way
	private float _dropThroughTimer;
	private bool _dropRequestedThisFrame;

	// Ledge
	private bool _isLedgeHanging;
	private bool _isLedgeClimbing;
	private Vector2 _ledgeHangPos;
	private Vector2 _ledgeClimbPos;

	// Автоклайм: задержка, чтобы не стартовать climb в тот же тик хвата
	private float _ledgeAutoClimbDelay;

	// NEW: пауза (игнор X-ввода) после взбирания
	private float _moveXIgnoreTimer;

	private PlayerState _state = PlayerState.Idle;

	public override void _Ready()
	{
		_visual = GetNode<Node2D>("Visual");
		_animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");
		_hitBox = GetNode<Area2D>("Visual/AnimatedSprite2D/HitBox");

		_ledgeWallRay = GetNode<RayCast2D>("Visual/LedgeWallRay");
		_ledgeTopRay = GetNode<RayCast2D>("Visual/LedgeTopRay");

		_mainCollider = GetNode<CollisionShape2D>("CollisionShape2D");

		_airJumpsLeft = _playerData.ExtraAirJumps;
		_coyoteTimer = 0f;

		_animationPlayer.AnimationFinished += OnAnimationFinished;
		_hitBox.AreaEntered += OnHitBoxAreaEntered;
	}

	public override void _ExitTree()
	{
		_animationPlayer.AnimationFinished -= OnAnimationFinished;
		_hitBox.AreaEntered -= OnHitBoxAreaEntered;
	}

	public override void _PhysicsProcess(double delta)
	{
		var dt = (float)delta;

		// NEW: тикаем паузу горизонтального ввода
		if (_moveXIgnoreTimer > 0f)
			_moveXIgnoreTimer = Mathf.Max(0f, _moveXIgnoreTimer - dt);

		TickWallJumpLock(dt);

		// ВАЖНО: ввод читаем ВСЕГДА, даже если сейчас hang/climb.
		// Тогда после окончания climb удержанная кнопка сразу продолжит движение. [web:12]
		ReadMoveInput();
		ReadAttackInput();
		ReadDashInput();

		ReadDropThroughInput();
		TickDropThroughTimer(dt);

		// ===== ledge has priority =====
		if (_isLedgeClimbing)
		{
			// Полный лок движения на время climb
			Velocity = Vector2.Zero;
			return;
		}

		if (_isLedgeHanging)
		{
			TickLedgeHang(dt);
			return;
		}

		UpdateComboTimer(dt);
		UpdateCoyote(dt);

		ApplyShapedGravity(dt);
		ApplyJumpAndWallJump();

		UpdateDash(dt);
		ApplyMoveXInstant();

		MoveAndSlide();

		UpdateWallSlide();

		TryGrabLedge();

		UpdateState();
		UpdateAnimation();

		_dropRequestedThisFrame = false;
	}

	// ========== INPUT ==========
	private void ReadMoveInput() => _axisX = Input.GetAxis("move_left", "move_right");

	private void ReadDashInput()
	{
		if (Input.IsActionJustPressed("dash") && _canDash)
			StartDash();
	}

	private void ReadAttackInput()
	{
		if (!Input.IsActionJustPressed("attack"))
			return;

		_comboResetTimer = _playerData.ComboResetTime;

		if (_isAttacking)
		{
			_attackQueued = true;
			return;
		}

		StartAttack();
	}

	// ========== DROP THROUGH ONE-WAY ==========
	private void ReadDropThroughInput()
	{
		_dropRequestedThisFrame = false;

		if (!IsOnFloor())
			return;

		if (!Input.IsActionPressed("move_down"))
			return;

		if (!Input.IsActionJustPressed("jump"))
			return;

		StartDropThrough();
		_dropRequestedThisFrame = true;
	}

	private void StartDropThrough()
	{
		_dropThroughTimer = _playerData.DropThroughTime;
		SetCollisionMaskValue(_playerData.OneWayPlatformLayer, false);

		GlobalPosition += new Vector2(0f, 2f);

		if (Velocity.Y < 0f) Velocity = Velocity with { Y = 0f };
		Velocity = Velocity with { Y = Mathf.Max(Velocity.Y, 60f) };
	}

	private void TickDropThroughTimer(float dt)
	{
		if (_dropThroughTimer <= 0f)
			return;

		_dropThroughTimer = Mathf.Max(0f, _dropThroughTimer - dt);
		if (_dropThroughTimer <= 0f)
			SetCollisionMaskValue(_playerData.OneWayPlatformLayer, true);
	}

	// ========== COMBO ==========
	private void UpdateComboTimer(float dt)
	{
		if (_comboResetTimer <= 0f)
			return;

		_comboResetTimer = Mathf.Max(0f, _comboResetTimer - dt);
		if (_comboResetTimer <= 0f)
		{
			_comboIndex = 0;
			_attackQueued = false;
		}
	}

	private void StartAttack()
	{
		var hits = Mathf.Max(1, _playerData.ComboHits);
		_comboIndex = Mathf.Clamp(_comboIndex, 0, hits - 1);

		_isAttacking = true;
		_attackQueued = false;

		var anim = $"{_playerData.AttackAnimPrefix}{_comboIndex + 1}";
		_animationPlayer.Play(anim);
	}

	private void AdvanceCombo()
	{
		var hits = Mathf.Max(1, _playerData.ComboHits);
		_comboIndex++;
		if (_comboIndex >= hits) _comboIndex = 0;
	}

	private void OnAnimationFinished(StringName animName)
	{
		var a = animName.ToString();

		if (_isAttacking)
		{
			_isAttacking = false;
			AdvanceCombo();

			if (_attackQueued && _comboResetTimer > 0f)
				StartAttack();

			return;
		}

		if (_isLedgeClimbing && a == "ledge_climb")
		{
			_isLedgeClimbing = false;

			// Переставляем тело в финальную точку и отпускаем лок.
			GlobalPosition = _ledgeClimbPos;
			Velocity = Vector2.Zero;

			// NEW: короткая пауза для X-ввода, чтобы не "уезжал" от края при удержании
			_moveXIgnoreTimer = 0.075f;

			_ledgeWallRay.ForceRaycastUpdate();
			_ledgeTopRay.ForceRaycastUpdate();
		}
	}

	private void OnHitBoxAreaEntered(Area2D otherArea)
	{
		if (otherArea.Owner is IDamageable dmg)
			dmg.TakeDamage(_playerData.AttackDamage[_comboIndex]);
	}

	// ========== COYOTE ==========
	private void UpdateCoyote(float dt)
	{
		if (IsOnFloor())
		{
			_coyoteTimer = _playerData.CoyoteTime;
			return;
		}

		_coyoteTimer = Mathf.Max(0f, _coyoteTimer - dt);
	}

	// ========== GRAVITY (SHAPED) ==========
	private void ApplyShapedGravity(float dt)
	{
		if (_isDashing)
		{
			Velocity = Velocity with { Y = 0f };
			return;
		}

		var v = Velocity;

		if (IsOnFloor())
		{
			if (v.Y > 0f) v.Y = 0f;

			_airJumpsLeft = _playerData.ExtraAirJumps;
			_canDash = true;

			Velocity = v;
			return;
		}

		var g = _playerData.Gravity;

		if (v.Y < 0f)
		{
			var progress = Mathf.Clamp(
				1f - (Mathf.Abs(v.Y) / Mathf.Max(1f, _playerData.JumpForce)),
				0f, 1f);

			var ascentMult = Mathf.Lerp(
				_playerData.AscentGravityMultiplierMin,
				_playerData.AscentGravityMultiplierMax,
				progress);

			g *= ascentMult;
		}
		else
		{
			g *= _playerData.FallGravityMultiplier;
		}

		v.Y = Mathf.Min(v.Y + g * dt, _playerData.MaxFallSpeed);
		Velocity = v;
	}

	// ========== JUMP (GROUND + DOUBLE + WALL) ==========
	private void ApplyJumpAndWallJump()
	{
		if (_dropRequestedThisFrame)
			return;

		if (!Input.IsActionJustPressed("jump"))
			return;

		var canGroundJump = IsOnFloor() || _coyoteTimer > 0f;
		if (canGroundJump)
		{
			Velocity = Velocity with { Y = -_playerData.JumpForce };
			_state = PlayerState.Jumping;
			_coyoteTimer = 0f;
			return;
		}

		if (TryWallJump())
			return;

		if (_airJumpsLeft <= 0)
			return;

		_airJumpsLeft--;

		var v = Velocity;
		if (v.Y > 0f) v.Y = 0f;

		var airJumpForce = _playerData.JumpForce * _playerData.AirJumpMultiplier;
		v.Y = -airJumpForce;

		Velocity = v;
		_state = PlayerState.Jumping;
	}

	private bool TryWallJump()
	{
		if (IsOnFloor())
			return false;

		if (_wallNormal == Vector2.Zero)
			return false;

		var pressingIntoWall =
			Mathf.Abs(_axisX) > 0.1f &&
			Mathf.Sign(_axisX) == -Mathf.Sign(_wallNormal.X);

		if (!pressingIntoWall)
			return false;

		var pushX = _wallNormal.X * _playerData.WallJumpPush;
		_canDash = true;

		Velocity = new Vector2(pushX, -_playerData.WallJumpForce);

		_isWallSliding = false;
		_coyoteTimer = 0f;

		_wallJumpLock = Mathf.Max(0f, _playerData.WallJumpLockTime);

		_state = PlayerState.Jumping;
		return true;
	}

	private void TickWallJumpLock(float dt)
	{
		if (_wallJumpLock > 0f)
			_wallJumpLock = Mathf.Max(0f, _wallJumpLock - dt);
	}

	// ========== MOVE X (INSTANT) ==========
	private void ApplyMoveXInstant()
	{
		if (_isDashing)
			return;

		if (_wallJumpLock > 0f)
			return;

		// Висим/взбираемся — не применяем X
		if (_isLedgeHanging || _isLedgeClimbing)
			return;

		// NEW: после взбирания игнорируем X на короткое время
		if (_moveXIgnoreTimer > 0f)
		{
			Velocity = Velocity with { X = 0f };
			return;
		}

		var x = _axisX * _playerData.Speed;
		Velocity = Velocity with { X = x };

		if (x > 0.1f && !_isFacingRight) FlipSprite(true);
		else if (x < -0.1f && _isFacingRight) FlipSprite(false);
	}

	// ========== DASH ==========
	private void StartDash()
	{
		_isDashing = true;
		_canDash = false;
		_dashTimer = 0f;

		var dir = _isFacingRight ? 1f : -1f;
		Velocity = new Vector2(dir * _playerData.DashForce, 0f);
	}

	private void UpdateDash(float dt)
	{
		if (!_isDashing)
			return;

		_dashTimer += dt;
		if (_dashTimer < _playerData.DashDuration)
			return;

		_isDashing = false;
		_dashTimer = 0f;

		Velocity = Velocity with { X = _axisX * _playerData.Speed };
	}

	// ========== WALL SLIDE ==========
	private void UpdateWallSlide()
	{
		_isWallSliding = false;
		_wallNormal = Vector2.Zero;

		if (_isDashing || _isAttacking)
			return;

		var inAir = !IsOnFloor();
		var touchingWall = IsOnWall();
		var falling = Velocity.Y > 0f;

		if (!(inAir && touchingWall && falling))
			return;

		var wallNormal = GetWallNormal();
		_wallNormal = wallNormal;

		var pressingIntoWall =
			Mathf.Abs(_axisX) > 0.1f &&
			Mathf.Sign(_axisX) == -Mathf.Sign(wallNormal.X);

		if (!pressingIntoWall)
			return;

		_isWallSliding = true;

		var mult = Mathf.Clamp(_playerData.WallSlideFallMultiplier, 0f, 1f);
		var wallSlideMaxFall = _playerData.MaxFallSpeed * mult;

		if (Velocity.Y > wallSlideMaxFall)
			Velocity = Velocity with { Y = wallSlideMaxFall };
	}

	// ========== LEDGE (FORWARD + DOWN) ==========
	private void TryGrabLedge()
	{
		if (_isLedgeHanging || _isLedgeClimbing)
			return;

		if (IsOnFloor())
			return;

		if (_isDashing || _isAttacking)
			return;

		if (_wallJumpLock > 0f)
			return;

		if (Velocity.Y < _playerData.LedgeGrabMinFallSpeed)
			return;

		var pressingIntoWall =
			Mathf.Abs(_axisX) > _playerData.LedgeInputDeadzone &&
			(_axisX > 0) == _isFacingRight;

		if (!pressingIntoWall)
			return;

		_ledgeWallRay.ForceRaycastUpdate();
		_ledgeTopRay.ForceRaycastUpdate();

		if (!_ledgeWallRay.IsColliding())
			return;

		if (!_ledgeTopRay.IsColliding())
			return;

		var wallPoint = _ledgeWallRay.GetCollisionPoint();
		var topPoint = _ledgeTopRay.GetCollisionPoint();

		GetColliderHalfExtents(out var halfW, out var halfH);

		var sign = _isFacingRight ? 1f : -1f;

		var hangX = wallPoint.X - sign * (halfW + _playerData.LedgeExtraGapX);
		var hangY = topPoint.Y + halfH + _playerData.LedgeExtraGapY;

		_ledgeHangPos = new Vector2(hangX, hangY);
		_ledgeClimbPos = _ledgeHangPos + new Vector2(sign * _playerData.LedgeClimbDelta.X, _playerData.LedgeClimbDelta.Y);

		StartLedgeHang();
	}

	private void GetColliderHalfExtents(out float halfW, out float halfH)
	{
		halfW = 8f;
		halfH = 16f;

		if (_mainCollider?.Shape == null)
			return;

		switch (_mainCollider.Shape)
		{
			case RectangleShape2D rect:
				halfW = rect.Size.X * 0.5f * Mathf.Abs(_mainCollider.Scale.X);
				halfH = rect.Size.Y * 0.5f * Mathf.Abs(_mainCollider.Scale.Y);
				return;

			case CapsuleShape2D cap:
				halfW = cap.Radius * Mathf.Abs(_mainCollider.Scale.X);
				halfH = (cap.Height * 0.5f + cap.Radius) * Mathf.Abs(_mainCollider.Scale.Y);
				return;

			case CircleShape2D cir:
				halfW = cir.Radius * Mathf.Abs(_mainCollider.Scale.X);
				halfH = cir.Radius * Mathf.Abs(_mainCollider.Scale.Y);
				return;
		}
	}

	private void StartLedgeHang()
	{
		GD.Print("Start");
		_isLedgeHanging = true;
		_isWallSliding = false;

		Velocity = Vector2.Zero;
		GlobalPosition = _ledgeHangPos;

		_ledgeAutoClimbDelay = 0.30f;

		_animationPlayer.Play("ledge_hang");
	}

	private void TickLedgeHang(float dt)
	{
		Velocity = Vector2.Zero;
		GlobalPosition = _ledgeHangPos;

		if (_ledgeAutoClimbDelay > 0f)
			_ledgeAutoClimbDelay = Mathf.Max(0f, _ledgeAutoClimbDelay - dt);

		var pressAwayFromLedge =
			Mathf.Abs(_axisX) > _playerData.LedgeInputDeadzone &&
			(_axisX > 0) != _isFacingRight;

		if (Input.IsActionJustPressed("move_down") || pressAwayFromLedge)
		{
			DropFromLedge();
			return;
		}

		if (Input.IsActionJustPressed("jump"))
		{
			LedgeWallJump();
			return;
		}

		// Автоклайм при удержании "в сторону уступа" (после задержки)
		if (_ledgeAutoClimbDelay <= 0f)
		{
			var pressTowardLedge =
				Mathf.Abs(_axisX) > _playerData.LedgeInputDeadzone &&
				(_axisX > 0) == _isFacingRight;

			if (pressTowardLedge)
			{
				StartLedgeClimb();
				return;
			}
		}
	}

	private void DropFromLedge()
	{
		_isLedgeHanging = false;
		Velocity = Velocity with { Y = 60f };
	}

	private void LedgeWallJump()
	{
		_isLedgeHanging = false;
		_isWallSliding = false;

		var dirX = _isFacingRight ? -1f : 1f;
		Velocity = new Vector2(dirX * _playerData.WallJumpPush, -_playerData.WallJumpForce);

		_canDash = true;
		_coyoteTimer = 0f;
		_wallJumpLock = Mathf.Max(0f, _playerData.WallJumpLockTime);

		_state = PlayerState.Jumping;
	}

	private void StartLedgeClimb()
	{
		_isLedgeHanging = false;
		_isLedgeClimbing = true;

		Velocity = Vector2.Zero;
		_animationPlayer.Play("ledge_climb");
	}

	// ========== STATE ==========
	private void UpdateState()
	{
		if (_isDashing) { _state = PlayerState.Dashing; return; }
		if (_isAttacking) { _state = PlayerState.Attacking; return; }
		if (_isWallSliding) { _state = PlayerState.WallSliding; return; }

		if (!IsOnFloor())
		{
			_state = Velocity.Y < 0 ? PlayerState.Jumping : PlayerState.Falling;
			return;
		}

		_state = Mathf.Abs(Velocity.X) > 0.1f ? PlayerState.Running : PlayerState.Idle;
	}

	// ========== ANIMATION ==========
	private void UpdateAnimation()
	{
		if (_isAttacking || _isLedgeHanging || _isLedgeClimbing)
			return;

		var anim = _state switch
		{
			PlayerState.Idle => "idle",
			PlayerState.Running => "move",
			PlayerState.Jumping => "jump_up",
			PlayerState.Falling => "jump_down",
			PlayerState.Dashing => "dash",
			PlayerState.WallSliding => "wall_slide",
			_ => "idle"
		};

		if (_animationPlayer.CurrentAnimation != anim)
			_animationPlayer.Play(anim);
	}

	// ========== UTILS ==========
	private void FlipSprite(bool facingRight)
	{
		_isFacingRight = facingRight;
		_visual.Scale = new Vector2(facingRight ? 1f : -1f, 1f);
	}

	public void TakeDamage(int amount)
	{
		GD.Print($"Take damage: {amount}");
	}
}
