using System.Globalization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("IskraStudio")]
[assembly: InternalsVisibleTo("Iskra.Android")]
[assembly: InternalsVisibleTo("IskraStudio.Tests")]

namespace IskraStudio;

internal enum RuntimeDiagnosticSeverity
{
    Warning,
    Error
}

internal sealed record RuntimeDiagnostic(
    RuntimeDiagnosticSeverity Severity,
    string Message,
    string SceneName,
    string ObjectName,
    int Line,
    Guid SceneId,
    Guid ObjectId);

internal sealed record RuntimeCommand(
    string Kind,
    string Argument,
    int Line,
    int Target = -1,
    Guid? TargetObjectId = null,
    Guid? ResourceOwnerId = null,
    string? Condition = null);

internal sealed record RuntimeDebugVariable(string SceneName, string ObjectName, string Name, string Value);

internal sealed record RuntimeCursorSnapshot(int Start, int End, int Index);

internal sealed record RuntimeObjectSnapshot(
    Guid Id,
    double X,
    double Y,
    double SizePercent,
    double DirectionDegrees,
    double TransparencyPercent,
    double BrightnessPercent,
    bool Visible,
    bool FlipX,
    bool FlipY,
    Guid? SpriteOwnerId,
    int SpriteIndex,
    int ImageSpeed,
    List<RuntimeCursorSnapshot> Cursors);

internal sealed record RuntimeSoundSnapshot(string Path, bool Loop, double PositionSeconds);

internal sealed record RuntimeSceneSnapshot(
    Guid SceneId,
    Dictionary<string, string> Variables,
    List<RuntimeObjectSnapshot> Objects,
    List<RuntimeSoundSnapshot> Sounds);

internal sealed record CompiledObject(Guid Id, string Name, string ScriptPath, List<RuntimeCommand> Commands);

internal sealed record CompiledScene(Guid Id, string Name, List<CompiledObject> Objects);

internal sealed record CompiledProject(int Version, string Name, ProjectOrientation Orientation, List<CompiledScene> Scenes);

internal sealed record RuntimeCacheEntry(CompiledProject Project, List<RuntimeDiagnostic> Diagnostics);

internal sealed record RuntimeCompileResult(
    CompiledProject? Project,
    IReadOnlyList<RuntimeDiagnostic> Diagnostics,
    string? CachePath)
{
    public bool HasErrors => Diagnostics.Any(item => item.Severity == RuntimeDiagnosticSeverity.Error);
    public bool HasWarnings => Diagnostics.Any(item => item.Severity == RuntimeDiagnosticSeverity.Warning);
}

internal static class SpriteSizeSyntax
{
    public const double MaximumPercent = 100_000d;

    public static bool IsValidSetSize(double amount) =>
        double.IsFinite(amount) && amount is >= 0 and <= MaximumPercent;

    public static double ApplyChange(double currentPercent, double change) =>
        Math.Clamp(currentPercent + change, 0d, MaximumPercent);
}
