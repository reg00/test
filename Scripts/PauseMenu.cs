using Godot;

public partial class PauseMenu : CanvasLayer
{
	[Export(PropertyHint.File, "*.tscn")]
	public string MainMenuScene = "res://Scenes/Menu.tscn";

	private Control _root;

	public override void _Ready()
	{
		_root = GetNode<Control>("Root");
		_root.Hide();

		// Чтобы работало даже когда дерево на паузе:
		ProcessMode = ProcessModeEnum.Always; // Process Mode влияет на поведение при paused [web:45]
		
		var resume = GetNode<Button>("Root/Center/Panel/Layout/ResumeButton");
		var menu = GetNode<Button>("Root/Center/Panel/Layout/MeinMenuButton");

		resume.Pressed += _OnResumePressed;
		menu.Pressed += _OnMainMenuPressed;
	}
	
	public override void _UnhandledInput(InputEvent e)
	{
		if (e.IsActionPressed("ui_cancel"))
			TogglePause();
	}

	private void TogglePause()
	{
		bool newPaused = !GetTree().Paused;
		GetTree().Paused = newPaused; // пауза через SceneTree.paused [web:45]

		if (newPaused) _root.Show();
		else _root.Hide();
	}

	private void _OnResumePressed()
	{
		GetTree().Paused = false;
		_root.Hide();
	}

	private void _OnMainMenuPressed()
	{
		// Перед сменой сцены важно снять паузу, иначе можно “притащить” paused дальше.
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(MainMenuScene);
	}
}
