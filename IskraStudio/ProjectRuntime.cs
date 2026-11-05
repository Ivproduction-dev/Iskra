namespace IskraStudio;

internal sealed partial class ProjectRuntime
{
    private readonly ScriptCompiler compiler;
    private readonly ProjectContentStore contentStore;

    public ProjectRuntime(ProjectStore projectStore, ProjectContentStore contentStore)
    {
        this.contentStore = contentStore;
        compiler = new ScriptCompiler(projectStore, contentStore);
    }

    internal ProjectContentStore ContentStore => contentStore;

    public RuntimeCompileResult Compile(IskraProject project, bool createCache = true, IProgress<string>? progress = null) =>
        compiler.Compile(project, createCache, progress);

    public bool ClearCache(IskraProject project, out string? error) =>
        compiler.ClearCache(project, out error);

    internal string GetProjectCacheDirectory(IskraProject project) =>
        compiler.GetProjectCacheDirectory(project);
}
