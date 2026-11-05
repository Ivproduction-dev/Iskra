using System.Globalization;
using System.IO;
using System.Text;

namespace IskraStudio;

internal static class RuntimeValueResolver
{
    internal static string ResolveValue(string source, IReadOnlyDictionary<string, string> variables)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;
        for (var index = 0; index < source.Length; index++)
        {
            if (inQuotes && source[index] == '\\' && index + 1 < source.Length &&
                (source[index + 1] == '"' || source[index + 1] == '\\'))
            {
                index++;
            }
            else if (source[index] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && source[index] == '.' && index + 1 < source.Length && source[index + 1] == '.')
            {
                parts.Add(source[start..index]);
                start = index + 2;
                index++;
            }
        }
        parts.Add(source[start..]);
        var output = new StringBuilder();
        foreach (var part in parts)
        {
            var value = part.Trim();
            if (IsQuoted(value)) output.Append(value[1..^1]);
            else if (long.TryParse(value, out _)) output.Append(value);
            else if (ArithmeticExpressionSyntax.TryEvaluate(value, variables, out var number))
                output.Append(number.ToString("R", CultureInfo.InvariantCulture));
            else output.Append(ConditionalDirectiveParser.ResolveVariable(value, variables));
        }
        return output.ToString();
    }

    internal static bool TryResolveSound(string source, CompiledObject item, CompiledScene scene, IskraProject project,
        ProjectContentStore contentStore, IReadOnlyDictionary<string, string> variables, out string path)
    {
        var reference = variables.TryGetValue(source, out var value) ? value : source;
        var sourceScene = project.Scenes.First(value => value.Id == scene.Id);
        var localSoundFiles = ProjectContentStore.ListResourceFiles(
            contentStore.GetObjectResourceDirectory(project, sourceScene, item.Id, "sounds"), ProjectContentStore.AudioExtensions);
        var objectReferences = scene.Objects.Select(candidate => new IskraObject { Id = candidate.Id, Name = candidate.Name }).ToArray();
        var resolved = ImageCommandSyntax.ResolveReference(reference, item.Id, objectReferences,
            fileName => localSoundFiles.Any(candidate => string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase)));
        var directory = contentStore.GetObjectResourceDirectory(project, sourceScene, resolved.OwnerId, "sounds");
        path = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.AudioExtensions)
            .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), resolved.FileName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return path.Length > 0;
    }

    internal static bool IsQuoted(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"';
}
