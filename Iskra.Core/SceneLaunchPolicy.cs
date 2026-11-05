namespace IskraStudio;

public static class SceneLaunchPolicy
{
    public static bool RequiresSceneChoice(IReadOnlyList<IskraScene> scenes, IskraScene? currentScene)
    {
        if (scenes.Count <= 1)
        {
            return false;
        }

        return currentScene is null || currentScene.Id != scenes[0].Id;
    }
}
