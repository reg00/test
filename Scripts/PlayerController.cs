using Godot;
using test2.Scripts;

// ReSharper disable once CheckNamespace
public partial class PlayerController : CharacterBody2D
{
	// ========== EXPORTED ==========
	[Export] private PlayerData _playerData;

	// ========== NODES / STATE ==========
	private Node2D _visual;
	private AnimationPlayer _animationPlayer;

	private Area2D _hitBox;

	private bool _isFacingRight = true;

	// Cached input per frame
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
	private Vector2 _wallNormal;     // last wall normal (valid after MoveAndSlide when on wall) [page:1]
	private float _wallJumpLock;     // optional: short lock of X control after wall jump

	private PlayerState _state = PlayerState.Idle;

	public override void _Ready()
	{
		_visual = GetNode<Node2D>("Visual");
		_animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");
		_hitBox = GetNode<Area2D>("Visual/AnimatedSprite2D/HitBox");

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

		TickWallJumpLock(dt);

		ReadMoveInput();
		ReadAttackInput();
		ReadDashInput();

		UpdateComboTimer(dt);
		UpdateCoyote(dt);

		// Vertical / special moves
		ApplyShapedGravity(dt);
		ApplyJumpAndWallJump(); // ground jump / double jump / wall jump

		// Horizontal moves
		UpdateDash(dt);
		ApplyMoveXInstant();

		// Physics step (updates collision flags + may modify Velocity) [page:1]
		MoveAndSlide();

		// Post-collision features depending on wall info (IsOnWall/GetWallNormal) [page:1]
		UpdateWallSlide();

		UpdateState();
		UpdateAnimation();
	}

	// ========== INPUT ==========
	private void ReadMoveInput()
	{
		_axisX = Input.GetAxis("move_left", "move_right");
	}

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
		if (_comboIndex >= hits)
			_comboIndex = 0;
	}

	// Сюда придёт имя завершившейся анимации [web:206][web:202]
	private void OnAnimationFinished(StringName animName)
	{
		// Нас интересуют только удары (по префиксу).
		// Например AttackAnimPrefix = "attack_" -> "attack_1", "attack_2"... 
		// var name = animName.ToString();
		// if (!name.StartsWith(_playerData.AttackAnimPrefix))
		// 	return;

		if (!_isAttacking)
			return;

		_isAttacking = false;

		AdvanceCombo();

		if (_attackQueued && _comboResetTimer > 0f)
			StartAttack();
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
		// Dash is purely horizontal
		if (_isDashing)
		{
			Velocity = Velocity with { Y = 0f };
			return;
		}

		var v = Velocity;

		if (IsOnFloor())
		{
			if (v.Y > 0f)
				v.Y = 0f;

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
		if (!Input.IsActionJustPressed("jump"))
			return;

		// 1) Ground/coyote jump first
		var canGroundJump = IsOnFloor() || _coyoteTimer > 0f;
		if (canGroundJump)
		{
			Velocity = Velocity with { Y = -_playerData.JumpForce };
			_state = PlayerState.Jumping;
			_coyoteTimer = 0f;
			return;
		}

		// 2) Wall jump (requires wall info from LAST MoveAndSlide) [page:1]
		// This means wall jump will work reliably while wall sliding (because wall slide is computed after MoveAndSlide).
		// If you want it to work the instant you touch the wall, we can add raycasts.
		if (TryWallJump())
			return;

		// 3) Air jump
		if (_airJumpsLeft <= 0)
			return;

		_airJumpsLeft--;

		var v = Velocity;
		if (v.Y > 0f)
			v.Y = 0f;

		var airJumpForce = _playerData.JumpForce * _playerData.AirJumpMultiplier;
		v.Y = -airJumpForce;

		Velocity = v;
		_state = PlayerState.Jumping;
	}

	private bool TryWallJump()
	{
		// Only in air + must have wall normal from previous physics step [page:1]
		if (IsOnFloor())
			return false;

		if (_wallNormal == Vector2.Zero)
			return false;

		// Require pressing toward the wall (same rule as wall slide)
		var pressingIntoWall =
			Mathf.Abs(_axisX) > 0.1f &&
			Mathf.Sign(_axisX) == -Mathf.Sign(_wallNormal.X);

		if (!pressingIntoWall)
			return false;

		// Push away from wall: normal points out of wall [page:1]
		var pushX = _wallNormal.X * _playerData.WallJumpPush;
		_canDash = true;

		Velocity = new Vector2(pushX, -_playerData.WallJumpForce);

		_isWallSliding = false;
		_coyoteTimer = 0f;

		// Optional short lock so player doesn't instantly override push with input
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

		// Optional: stabilize wall jump push
		if (_wallJumpLock > 0f)
			return;

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

		// IsOnWall/GetWallNormal are valid after MoveAndSlide() [page:1]
		var inAir = !IsOnFloor();
		var touchingWall = IsOnWall();
		var falling = Velocity.Y > 0f;

		if (!(inAir && touchingWall && falling))
			return;

		var wallNormal = GetWallNormal(); // [page:1]
		_wallNormal = wallNormal;

		var pressingIntoWall =
			Mathf.Abs(_axisX) > 0.1f &&
			Mathf.Sign(_axisX) == -Mathf.Sign(wallNormal.X);

		if (!pressingIntoWall)
			return;

		_isWallSliding = true;

		// Instant clamp fall speed by multiplier
		var mult = Mathf.Clamp(_playerData.WallSlideFallMultiplier, 0f, 1f);
		var wallSlideMaxFall = _playerData.MaxFallSpeed * mult;

		if (Velocity.Y > wallSlideMaxFall)
			Velocity = Velocity with { Y = wallSlideMaxFall };
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
		if (_isAttacking)
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
}
