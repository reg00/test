using Godot;

public partial class Door : Area2D
{
	[Export(PropertyHint.File, "*.tscn")]
	public string TargetLevel;

	private Main _main;

	[Export]
	public string TargetSpawnName = "";

	public override void _Ready()
	{
		_main = GetTree().CurrentScene as Main;

		BodyEntered += OnBodyEntered;
	}

	public override void _ExitTree()
	{
		BodyEntered -= OnBodyEntered;
	}

	private void OnBodyEntered(Node body)
	{
		if (!body.IsInGroup("player")) return;

		if (_main == null)
		{
			GD.PushError($"Door '{Name}': main scene is not Main, level transition skipped.");
			return;
		}

		_main.ChangeLevel(TargetLevel, TargetSpawnName);
	}
}
