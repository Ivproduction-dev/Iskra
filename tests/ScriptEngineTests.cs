using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ScriptEngineTests
{
    private sealed class FakeHost : IEngineHost
    {
        public List<string> OutputLines { get; } = [];
        public bool OverlapResult { get; set; }

        public double SurfaceWidth => 1920d;

        public double SurfaceHeight => 1080d;

        public bool Paused => false;

        public Task WaitWhilePausedAsync() => Task.CompletedTask;

        public CancellationToken Token => CancellationToken.None;

        public void Output(string objectName, string text) => OutputLines.Add(text);

        public void VariablesChanged(IReadOnlyList<RuntimeDebugVariable> variables)
        {
        }

        public Task<string?> AskAsync(string question) => Task.FromResult<string?>("тест");

        public IEngineSound? PlaySound(string path, bool loop, double positionSeconds) => null;

        public void OrientationChanged(ProjectOrientation orientation)
        {
        }

        public void SceneSwitchRequested(Guid sceneId, bool resume)
        {
        }

        public void StateChanged()
        {
        }

        public bool Overlaps(EngineObject first, EngineObject second) => OverlapResult;

        public Guid? TopmostAt(double x, double y) => null;

        public bool IsOverPoint(EngineObject item, double x, double y) => false;
    }

    private sealed record TestApp(ScriptEngine Engine, FakeHost Host, CompiledProject Project, CompiledScene Scene, IskraProject Source);

    private static TestApp Build(params (string SceneName, string Script)[] scenes)
    {
        var store = new ProjectStore();
        var compiler = new ScriptCompiler(store, new ProjectContentStore(store));
        var project = new IskraProject { Name = "Тест Движок" };
        var compiledScenes = new List<CompiledScene>();
        foreach (var (sceneName, _) in scenes)
        {
            var scene = new IskraScene { Name = sceneName };
            scene.Objects.Add(new IskraObject { Id = IskraObject.BackgroundId, Name = "Фон" });
            project.Scenes.Add(scene);
        }
        foreach (var (sceneName, script) in scenes)
        {
            var scene = project.Scenes.First(candidate => candidate.Name == sceneName);
            var item = scene.Objects[0];
            var diagnostics = new List<RuntimeDiagnostic>();
            var commands = compiler.CompileScript(project, scene, item, script, scene.Objects.ToList(), diagnostics);
            Assert.True(diagnostics.All(item => item.Severity != RuntimeDiagnosticSeverity.Error),
                string.Join("; ", diagnostics.Where(item => item.Severity == RuntimeDiagnosticSeverity.Error).Select(item => item.Message)));
            compiledScenes.Add(new CompiledScene(scene.Id, scene.Name, [new CompiledObject(item.Id, item.Name, string.Empty, commands)]));
        }
        var host = new FakeHost();
        var compiled = new CompiledProject(1, project.Name, project.Orientation, compiledScenes);
        var engine = new ScriptEngine(compiled, compiledScenes[0], host, new ProjectContentStore(store), project);
        engine.Prepare();
        return new TestApp(engine, host, compiled, compiledScenes[0], project);
    }

    private static async Task<TestApp> StartedAsync(params (string SceneName, string Script)[] scenes)
    {
        var app = Build(scenes);
        await app.Engine.Start();
        await app.Engine.WaitForBlocksAsync();
        return app;
    }

    [Fact]
    public async Task VariablesSetChangeAndPrintWork()
    {
        var app = await StartedAsync(("Сцена", """
            При. запуске
            Задать счёт = 5
            Изменить счёт 2
            Печать счёт
            Конец
            """));
        Assert.Equal("7", app.Engine.Variables["счёт"]);
        Assert.Contains("7", app.Host.OutputLines);
    }

    [Fact]
    public async Task IfElseTakesFirstTrueBranch()
    {
        var app = await StartedAsync(("Сцена", """
            При. запуске
            Задать счёт = 5
            Если счёт = 5 то
            Печать "да"
            Иначе
            Печать "нет"
            Конец
            Конец
            """));
        Assert.Equal(["да"], app.Host.OutputLines);
    }

    [Fact]
    public async Task IfElseTakesElseBranchWhenFalse()
    {
        var app = await StartedAsync(("Сцена", """
            При. запуске
            Задать счёт = 1
            Если счёт = 5 то
            Печать "да"
            Иначе
            Печать "нет"
            Конец
            Конец
            """));
        Assert.Equal(["нет"], app.Host.OutputLines);
    }

    [Fact]
    public async Task WhileLoopRunsUntilConditionFails()
    {
        var app = await StartedAsync(("Сцена", """
            При. запуске
            Задать счёт = 0
            Пока счёт < 3 то
            Изменить счёт 1
            Конец
            Печать счёт
            Конец
            """));
        Assert.Equal("3", app.Engine.Variables["счёт"]);
        Assert.Contains("3", app.Host.OutputLines);
    }

    [Fact]
    public async Task LaunchSceneSetsPendingSwitch()
    {
        var app = Build(
            ("Меню", """
            При. запуске
            Запустить.сцену.Игра
            Конец
            """),
            ("Игра", """
            При. запуске
            Печать "игра"
            Конец
            """));
        var requested = false;
        app.Engine.SceneRequested += () => requested = true;
        app.Engine.Start();
        await app.Engine.WaitForBlocksAsync();
        Assert.True(requested);
        Assert.NotNull(app.Engine.PendingScene);
        Assert.Equal(app.Project.Scenes[1].Id, app.Engine.PendingScene!.Value.SceneId);
        Assert.False(app.Engine.PendingScene.Value.Resume);
        Assert.Empty(app.Host.OutputLines);
    }

    [Fact]
    public async Task VisibilityAndPositionCommandsWork()
    {
        var app = await StartedAsync(("Сцена", """
            При. запуске
            Спрятать
            Поместить 100 50
            Подвинуть. 10 -5
            Конец
            """));
        var background = app.Engine.Find(IskraObject.BackgroundId)!;
        Assert.False(background.Visible);
        Assert.Equal(110, background.X);
        Assert.Equal(45, background.Y);
    }

    [Fact]
    public async Task SnapshotRoundtripRestoresState()
    {
        var source = await StartedAsync(("Сцена", """
            При. запуске
            Задать счёт = 9
            Поместить 42 21
            Спрятать
            Конец
            """));
        var snapshot = source.Engine.CaptureSnapshot();

        var app = Build(("Сцена", "При. запуске\nПечать счёт\nКонец"));
        var restored = new ScriptEngine(app.Project, app.Scene, app.Host, new ProjectContentStore(new ProjectStore()), app.Source, snapshot);
        restored.Prepare();
        restored.Start();
        await restored.WaitForBlocksAsync();

        var background = restored.Find(IskraObject.BackgroundId)!;
        Assert.Equal("9", restored.Variables["счёт"]);
        Assert.Equal(42, background.X);
        Assert.Equal(21, background.Y);
        Assert.False(background.Visible);
    }

    [Fact]
    public void PositionPropertyDefaultsToZero()
    {
        var app = Build(("Сцена", "При. запуске\nКонец"));
        var values = app.Engine.Find(IskraObject.BackgroundId)!.CurrentValues(app.Engine.Variables);
        Assert.Equal("0", values["Свойство.х"]);
        Assert.Equal("100", values["Свойство.размер"]);
    }

    [Fact]
    public async Task ShowAndHideOtherObjectWork()
    {
        var app = await StartedAsync(("Сцена", "При. запуске\nСпрятать\nКонец"));
        Assert.False(app.Engine.Find(IskraObject.BackgroundId)!.Visible);
    }
}
