using Godot;

public partial class PauseMenu : CanvasLayer
{
	[Export(PropertyHint.File, "*.tscn")]
	public string MainMenuScene = "res://Scenes/Menu.tscn";

	private Control _root;
	private Button _resumeButton;
	private Button _mainMenuButton;

	public override void _Ready()
	{
		_root = GetNode<Control>("Root");
		_root.Hide();

		// Чтобы работало даже когда дерево на паузе:
		ProcessMode = ProcessModeEnum.Always; // Process Mode влияет на поведение при paused [web:45]
		
		_resumeButton = GetNode<Button>("Root/Center/Panel/Layout/ResumeButton");
		_mainMenuButton = GetNode<Button>("Root/Center/Panel/Layout/MainMenuButton");

		_resumeButton.Pressed += _OnResumePressed;
		_mainMenuButton.Pressed += _OnMainMenuPressed;
	}

	public override void _ExitTree()
	{
		if (_resumeButton != null)
			_resumeButton.Pressed -= _OnResumePressed;
		if (_mainMenuButton != null)
			_mainMenuButton.Pressed -= _OnMainMenuPressed;
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
