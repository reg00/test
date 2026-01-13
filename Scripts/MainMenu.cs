using Godot;

public partial class MainMenu : Control
{
	[Export(PropertyHint.File, "*.tscn")]
	public string MainGameScene = "res://Scenes/main.tscn";

	public override void _Ready()
	{
		var start = GetNode<Button>("Center/Panel/Layout/StartButton");
		var quit = GetNode<Button>("Center/Panel/Layout/QuitButton");

		start.Pressed += _OnStartPressed;
		quit.Pressed += _OnQuitPressed;
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
