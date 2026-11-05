using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class SceneLaunchPolicyTests
{
    [Fact]
    public void OneSceneStartsWithoutAskingEvenWhenItIsNotTheCurrentScene()
    {
        var onlyScene = new IskraScene { Name = "Одна" };

        Assert.False(SceneLaunchPolicy.RequiresSceneChoice([onlyScene], null));
        Assert.False(SceneLaunchPolicy.RequiresSceneChoice([onlyScene], new IskraScene()));
    }

    [Fact]
    public void FirstSceneStartsDirectlyWhileOtherCurrentScenesRequireAChoice()
    {
        var scenes = new[]
        {
            new IskraScene { Name = "А" },
            new IskraScene { Name = "Б" },
            new IskraScene { Name = "В" }
        };

        Assert.False(SceneLaunchPolicy.RequiresSceneChoice(scenes, scenes[0]));
        Assert.True(SceneLaunchPolicy.RequiresSceneChoice(scenes, scenes[1]));
        Assert.True(SceneLaunchPolicy.RequiresSceneChoice(scenes, null));
    }

    [Fact]
    public void SceneChoiceScalesToLargeProjects()
    {
        var scenes = Enumerable.Range(1, 150)
            .Select(number => new IskraScene { Name = $"Сцена {number}" })
            .ToArray();

        Assert.False(SceneLaunchPolicy.RequiresSceneChoice(scenes, scenes[0]));
        Assert.True(SceneLaunchPolicy.RequiresSceneChoice(scenes, scenes[149]));
    }
}
