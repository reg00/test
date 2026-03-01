using Godot;

public partial class MainMenu : Control
{
	[Export(PropertyHint.File, "*.tscn")]
	public string MainGameScene = "res://Scenes/main.tscn";

	private Button _startButton;
	private Button _quitButton;

	public override void _Ready()
	{
		_startButton = GetNode<Button>("Center/Panel/Layout/StartButton");
		_quitButton = GetNode<Button>("Center/Panel/Layout/QuitButton");

		_startButton.Pressed += _OnStartPressed;
		_quitButton.Pressed += _OnQuitPressed;
	}

	public override void _ExitTree()
	{
		if (_startButton != null)
			_startButton.Pressed -= _OnStartPressed;
		if (_quitButton != null)
			_quitButton.Pressed -= _OnQuitPressed;
	}

	private void _OnStartPressed()
	{
		GetTree().ChangeSceneToFile(MainGameScene);
	}

	private void _OnQuitPressed()
	{
		GetTree().Quit();
	}
}
