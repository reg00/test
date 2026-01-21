using Godot;
using System;

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
		
		LoadLevel(CurrentLevel);
		
		var level = _world.GetChild<Node2D>(_world.GetChildCount() - 1);
		var spawn = level.FindChild(LastSpawn, recursive: true, owned: false) as Marker2D;

		if (spawn != null)
			_player.GlobalPosition = spawn.GlobalPosition;
	}

	private void LoadLevel(string path)
	{
		foreach (var child in _world.GetChildren())
		{
			if (child == _player) continue;
			child.QueueFree();
		}

		var packed = GD.Load<PackedScene>(path);
		var level = packed.Instantiate<Node2D>();
		_world.AddChild(level);
	}
	
	public void ChangeLevel(string levelPath, string spawnName)
	{
		CallDeferred(nameof(ChangeLevelDeferred), levelPath, spawnName);
	}
	
	private void ChangeLevelDeferred(string levelPath, string spawnName)
	{
		LoadLevel(levelPath);

		// Находим точку спавна внутри загруженного уровня (он теперь ребёнок World)
		var level = _world.GetChild<Node2D>(_world.GetChildCount() - 1);

		if (level.FindChild(spawnName, recursive: true, owned: false) is Marker2D spawn)
			_player.GlobalPosition = spawn.GlobalPosition;

		CurrentLevel = levelPath;
	}
}
