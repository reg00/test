using Godot;
using System;

public partial class Main : Node
{
	[Export(PropertyHint.File, "*.tscn")]
	public string CurrentLevel = "res://Scenes/level_1.tscn";

	private Node2D _world;

	public override void _Ready()
	{
		_world = GetNode<Node2D>("World");
		LoadLevel(CurrentLevel);
	}

	private void LoadLevel(string path)
	{
		// Удаляем старый уровень
		foreach (var child in _world.GetChildren())
			child.QueueFree();

		// Загружаем новый
		var packed = GD.Load<PackedScene>(path);
		var level = packed.Instantiate<Node2D>();
		_world.AddChild(level);
	}
}
