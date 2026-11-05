using System.IO;
using System.Text.RegularExpressions;

namespace IskraStudio;

internal readonly record struct ImageAssignment(Guid TargetId, Guid ResourceOwnerId, string FileName);

internal static class ImageCommandSyntax
{
    private static readonly Regex StepCommand = new(
        "^Изменить\\s+изображение\\s*\\.\\s*(?<argument>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static (Guid OwnerId, string FileName) ResolveReference(
        string reference, Guid defaultOwnerId, IReadOnlyList<IskraObject> objects,
        Func<string, bool>? exactLocalResourceExists = null)
    {
        var trimmedReference = reference.Trim();
        if (exactLocalResourceExists?.Invoke(trimmedReference) == true)
        {
            return (defaultOwnerId, trimmedReference);
        }

        var separator = reference.IndexOf('.');
        if (separator > 0)
        {
            var ownerName = reference[..separator].Trim();
            var owner = objects.FirstOrDefault(item =>
                string.Equals(item.Name, ownerName, StringComparison.OrdinalIgnoreCase));
            if (owner is not null)
            {
                return (owner.Id, reference[(separator + 1)..].Trim());
            }
        }

        return (defaultOwnerId, reference.Trim());
    }

    internal static ImageAssignment ResolveAssignment(
        string reference, Guid currentObjectId, IReadOnlyList<IskraObject> objects,
        Func<string, bool>? exactLocalResourceExists = null)
    {
        var (resourceOwnerId, fileName) = ResolveReference(reference, currentObjectId, objects, exactLocalResourceExists);
        var separator = reference.IndexOf('.');
        var hasExplicitTarget = separator > 0 && separator + 1 < reference.Length &&
            char.IsWhiteSpace(reference[separator + 1]);
        var objectName = separator > 0 ? reference[..separator].Trim() : string.Empty;
        var ownerExists = objects.Any(item => item.Id == resourceOwnerId &&
            string.Equals(item.Name, objectName, StringComparison.OrdinalIgnoreCase));
        var destinationId = hasExplicitTarget && ownerExists ? resourceOwnerId : currentObjectId;

        return new ImageAssignment(destinationId, resourceOwnerId, fileName);
    }

    internal static bool TryParseStepCommand(string line, out string? objectName, out string direction)
    {
        objectName = null;
        direction = string.Empty;
        var match = StepCommand.Match(line.Trim());
        if (!match.Success) return false;

        var argument = match.Groups["argument"].Value.Trim();
        var separator = argument.LastIndexOf('.');
        if (separator > 0 && IsDirection(argument[(separator + 1)..].Trim()))
        {
            objectName = argument[..separator].Trim();
            direction = argument[(separator + 1)..].Trim().ToLowerInvariant();
            return objectName.Length > 0;
        }

        if (!IsDirection(argument)) return false;
        direction = argument.ToLowerInvariant();
        return true;
    }

    internal static bool StartsStepCommand(string line) =>
        line.TrimStart().StartsWith("Изменить изображение", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirection(string value) =>
        value.Equals("перед", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("назад", StringComparison.OrdinalIgnoreCase);
}
