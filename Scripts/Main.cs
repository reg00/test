using Godot;

public partial class Main : Node
{
	[Export(PropertyHint.File, "*.tscn")]
	public string CurrentLevel = "res://Scenes/level_1.tscn";

	[Export] public string LastSpawn = "";
	
	private Node2D _world;
	private CharacterBody2D _player;

	public override void _Ready()
	{
		_world = GetNode<Node2D>("World");
		_player = GetNode<CharacterBody2D>("Player");

		if (!TryLoadLevel(CurrentLevel, out var level))
			return;

		TryMovePlayerToSpawn(level, LastSpawn);
	}

	private bool TryLoadLevel(string path, out Node2D level)
	{
		level = null;

		if (string.IsNullOrWhiteSpace(path))
		{
			GD.PushError("Main: level path is empty.");
			return false;
		}

		var packed = GD.Load<PackedScene>(path);
		if (packed == null)
		{
			GD.PushError($"Main: failed to load level scene '{path}'.");
			return false;
		}

		foreach (var child in _world.GetChildren())
		{
			if (child == _player) continue;
			child.QueueFree();
		}

		level = packed.Instantiate<Node2D>();
		if (level == null)
		{
			GD.PushError($"Main: failed to instantiate level scene '{path}'.");
			return false;
		}

		_world.AddChild(level);
		return true;
	}

	private void TryMovePlayerToSpawn(Node2D level, string spawnName)
	{
		if (level == null || _player == null)
			return;

		if (string.IsNullOrWhiteSpace(spawnName))
			return;

		if (level.FindChild(spawnName, recursive: true, owned: false) is Marker2D spawn)
		{
			_player.GlobalPosition = spawn.GlobalPosition;
			return;
		}

		GD.PushWarning($"Main: spawn '{spawnName}' was not found in level '{level.Name}'.");
	}
	
	public void ChangeLevel(string levelPath, string spawnName)
	{
		CallDeferred(nameof(ChangeLevelDeferred), levelPath, spawnName);
	}
	
	private void ChangeLevelDeferred(string levelPath, string spawnName)
	{
		if (!TryLoadLevel(levelPath, out var level))
			return;

		TryMovePlayerToSpawn(level, spawnName);
		CurrentLevel = levelPath;
		LastSpawn = spawnName;
	}
}
