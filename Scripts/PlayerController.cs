using Godot;
using test2.Scripts;

// ReSharper disable once CheckNamespace
public partial class PlayerController : CharacterBody2D
{
	// ========== ЭКСПОРТИРУЕМЫЕ ПАРАМЕТРЫ ==========
	[Export] private PlayerData _playerData;

	// ========== ПРИВАТНЫЕ ПЕРЕМЕННЫЕ ==========
	private AnimatedSprite2D _animatedSprite;

	private bool _isFacingRight = true;

	// Dash
	private bool _isDashing;
	private float _dashTimer;
	private bool _canDash = true;

	// Jump
	private int _airJumpsLeft;
	private float _coyoteTimer;

	// Attack / Combo
	private bool _isAttacking;
	private int _comboIndex;          // 0..ComboHits-1
	private float _comboResetTimer;   // таймер до сброса комбо
	private bool _attackQueued;       // если нажали attack во время удара — ставим в очередь следующий

	private PlayerState _state = PlayerState.Idle;

	public override void _Ready()
	{
		_animatedSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");

		_airJumpsLeft = _playerData.ExtraAirJumps;
		_coyoteTimer = 0f;

		_animatedSprite.AnimationFinished += OnAnimationFinished;
	}

	public override void _ExitTree()
	{
		_animatedSprite.AnimationFinished -= OnAnimationFinished;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		UpdateComboTimer(dt);

		UpdateCoyote(dt);
		ReadAttackInput();
		ReadDashInput();

		ApplyShapedGravity(dt);
		ApplyJump();

		UpdateDash(dt);
		ApplyMoveXInstant();

		UpdateState();
		UpdateAnimation();

		MoveAndSlide(); // движение через Velocity + MoveAndSlide() [page:1]
	}

	// ========== COMBO TIMER ==========
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

	// ========== ATTACK INPUT ==========
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

	private void StartAttack()
	{
		int hits = Mathf.Max(1, _playerData.ComboHits);
		_comboIndex = Mathf.Clamp(_comboIndex, 0, hits - 1);

		_isAttacking = true;
		_attackQueued = false;

		string anim = $"{_playerData.AttackAnimPrefix}{_comboIndex + 1}";
		_animatedSprite.Play(anim);
	}

	private void AdvanceCombo()
	{
		int hits = Mathf.Max(1, _playerData.ComboHits);
		_comboIndex++;
		if (_comboIndex >= hits)
			_comboIndex = 0;
	}

	private void OnAnimationFinished()
	{
		if (!_isAttacking)
			return;

		_isAttacking = false;

		AdvanceCombo();

		if (_attackQueued && _comboResetTimer > 0f)
			StartAttack();
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

	// ========== INPUT (DASH) ==========
	private void ReadDashInput()
	{
		if (Input.IsActionJustPressed("dash") && _canDash)
			StartDash();
	}

	// ========== GRAVITY (SHAPED) ==========
	private void ApplyShapedGravity(float dt)
	{
		// Дэш строго горизонтальный: без гравитации, Y фиксируем в 0
		if (_isDashing)
		{
			Velocity = Velocity with { Y = 0f };
			return;
		}

		var v = Velocity;

		if (IsOnFloor())
		{
			if (v.Y > 0)
				v.Y = 0;

			_airJumpsLeft = _playerData.ExtraAirJumps;
			_canDash = true;

			Velocity = v;
			return;
		}

		float g = _playerData.Gravity;

		if (v.Y < 0f)
		{
			// Подъём: ближе к вершине (v.Y -> 0) сильнее “гасим” скорость вверх
			float progress = Mathf.Clamp(
				1f - (Mathf.Abs(v.Y) / Mathf.Max(1f, _playerData.JumpForce)),
				0f, 1f);

			float ascentMult = Mathf.Lerp(
				_playerData.AscentGravityMultiplierMin,
				_playerData.AscentGravityMultiplierMax,
				progress);

			g *= ascentMult;
		}
		else
		{
			// Падение: сильнее тянем вниз
			g *= _playerData.FallGravityMultiplier;
		}

		v.Y = Mathf.Min(v.Y + g * dt, _playerData.MaxFallSpeed);
		Velocity = v;
	}

	// ========== JUMP (COYOTE + DOUBLE) ==========
	private void ApplyJump()
	{
		if (!Input.IsActionJustPressed("jump"))
			return;

		bool canGroundJump = IsOnFloor() || _coyoteTimer > 0f;

		if (canGroundJump)
		{
			Velocity = Velocity with { Y = -_playerData.JumpForce };
			_state = PlayerState.Jumping;
			_coyoteTimer = 0f;
			return;
		}

		if (_airJumpsLeft > 0)
		{
			_airJumpsLeft--;

			// Ключевая правка: если падаем, сначала гасим скорость падения,
			// иначе маленький airJumpForce может почти не ощущаться. [page:1]
			var v = Velocity;
			if (v.Y > 0f)
				v.Y = 0f;

			float airJumpForce = _playerData.JumpForce * _playerData.AirJumpMultiplier; // например 0.5
			v.Y = -airJumpForce;

			Velocity = v;
			_state = PlayerState.Jumping;
		}
	}

	// ========== MOVE X (INSTANT STOP) ==========
	private void ApplyMoveXInstant()
	{
		if (_isDashing)
			return;

		float axis = Input.GetAxis("move_left", "move_right");
		float x = axis * _playerData.Speed;

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

		float dir = _isFacingRight ? 1f : -1f;
		Velocity = new Vector2(dir * _playerData.DashForce, 0f);
	}

	private void UpdateDash(float dt)
	{
		if (!_isDashing)
			return;

		_dashTimer += dt;
		if (_dashTimer >= _playerData.DashDuration)
		{
			_isDashing = false;
			_dashTimer = 0f;

			float axis = Input.GetAxis("move_left", "move_right");
			Velocity = Velocity with { X = axis * _playerData.Speed };
		}
	}

	// ========== STATE ==========
	private void UpdateState()
	{
		if (_isDashing)
		{
			_state = PlayerState.Dashing;
			return;
		}

		if (_isAttacking)
		{
			_state = PlayerState.Attacking;
			return;
		}

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
		// Пока атакуем — анимацию атак задаёт StartAttack(), чтобы движение/прыжок её не перебивали
		if (_isAttacking)
			return;

		string anim = _state switch
		{
			PlayerState.Idle => "idle",
			PlayerState.Running => "move",
			PlayerState.Jumping => "jump_up",
			PlayerState.Falling => "jump_down",
			PlayerState.Dashing => "dash",
			_ => "idle"
		};

		if (_animatedSprite.Animation != anim)
			_animatedSprite.Play(anim);
	}

	// ========== UTILS ==========
	private void FlipSprite(bool facingRight)
	{
		_isFacingRight = facingRight;
		_animatedSprite.FlipH = !facingRight;
	}
}
