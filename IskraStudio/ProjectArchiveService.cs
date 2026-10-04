using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace IskraStudio;

public sealed class ProjectArchiveService(ProjectStore projectStore)
{
    private const string ManifestEntryName = "manifest.json";
    private const int CurrentFormatVersion = 1;
    private const long MaximumExpandedBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumFiles = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Export(IskraProject project, string destination)
    {
        var root = projectStore.GetProjectDirectoryFor(project);
        if (!Directory.Exists(root))
        {
            throw new IOException("Папка проекта не найдена.");
        }

        var files = new SortedDictionary<string, ArchiveFileReference>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(source);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("В проекте есть ссылка на файл, которую нельзя включить в архив.");
            }

            var relative = NormalizeRelativePath(Path.GetRelativePath(root, source));
            if (relative.StartsWith(".delete-", StringComparison.Ordinal) || relative.Contains("/.delete-", StringComparison.Ordinal))
            {
                continue;
            }

            EnsureSafeRelativePath(relative);
            var info = new FileInfo(source);
            totalBytes = checked(totalBytes + info.Length);
            if (totalBytes > MaximumExpandedBytes || files.Count >= MaximumFiles)
            {
                throw new InvalidDataException("Проект слишком велик для текущего формата архива.");
            }

            var hash = HashFile(source);
            files.Add(relative, new ArchiveFileReference(hash, info.Length));
            sources.TryAdd(hash, source);
        }

        if (!files.ContainsKey("project.iskra.json"))
        {
            throw new InvalidDataException("В проекте отсутствуют метаданные.");
        }

        var manifest = new ProjectArchiveManifest(CurrentFormatVersion, files);
        var stagingRoot = Path.GetTempPath();
        var temporaryDestination = Path.Combine(stagingRoot, $"iskra-export-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporaryDestination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                using (var manifestStream = manifestEntry.Open())
                {
                    JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
                }

                foreach (var (hash, source) in sources)
                {
                    var compression = IsAlreadyCompressed(source) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    var entry = archive.CreateEntry($"objects/{hash}", compression);
                    using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var blob = entry.Open();
                    input.CopyTo(blob);
                }
            }

            File.Move(temporaryDestination, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryDestination))
            {
                File.Delete(temporaryDestination);
            }
        }
    }

    public string ReadProjectName(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = IndexEntries(archive);
        if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
        {
            throw new InvalidDataException("В архиве отсутствует манифест проекта.");
        }

        var manifest = ReadManifest(manifestEntry);
        if (!manifest.Files.TryGetValue("project.iskra.json", out var metadataReference))
        {
            throw new InvalidDataException("В архиве отсутствуют данные проекта.");
        }

        if (metadataReference.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("Данные проекта в архиве слишком велики.");
        }

        var blob = GetBlob(entries, metadataReference.Sha256);
        using var stream = blob.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var project = JsonSerializer.Deserialize<IskraProject>(memory.ToArray(), ProjectStore.SerializerOptionsForArchive)
            ?? throw new InvalidDataException("Не удалось прочитать данные проекта.");
        return project.Name;
    }

    public IskraProject Import(string archivePath, string newName)
    {
        if (!ProjectStore.IsValidProjectName(newName, out var nameError))
        {
            throw new InvalidDataException(nameError ?? "Недопустимое имя проекта.");
        }

        var finalPath = Path.Combine(projectStore.ProjectsRoot, newName);
        if (Directory.Exists(finalPath) || File.Exists(finalPath))
        {
            throw new IOException("Проект с таким именем уже существует.");
        }

        Directory.CreateDirectory(projectStore.ProjectsRoot);
        var temporaryPath = Path.Combine(projectStore.ProjectsRoot, $".import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryPath);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = IndexEntries(archive);
            if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
            {
                throw new InvalidDataException("В архиве отсутствует манифест проекта.");
            }

            var manifest = ReadManifest(manifestEntry);
            if (manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count > MaximumFiles)
            {
                throw new InvalidDataException("Список файлов проекта пуст или слишком велик.");
            }

            var totalBytes = 0L;
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (relativePath, reference) in manifest.Files)
            {
                EnsureSafeRelativePath(relativePath);
                if (!uniquePaths.Add(relativePath) || reference is null || reference.Length < 0 ||
                    reference.Length > MaximumExpandedBytes || reference.Sha256 is null ||
                    !System.Text.RegularExpressions.Regex.IsMatch(reference.Sha256, "^[0-9A-Fa-f]{64}$"))
                {
                    throw new InvalidDataException("В манифесте указаны некорректные данные файла.");
                }

                totalBytes = checked(totalBytes + reference.Length);
                if (totalBytes > MaximumExpandedBytes)
                {
                    throw new InvalidDataException("Распакованный проект превышает допустимый размер.");
                }

                var source = GetBlob(entries, reference.Sha256);
                if (source.Length != reference.Length)
                {
                    throw new InvalidDataException("Размер файла в архиве не совпадает с манифестом.");
                }

                var target = GetSafeDestination(temporaryPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = source.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    copied += read;
                    if (copied > reference.Length)
                    {
                        throw new InvalidDataException("Файл в архиве больше указанного размера.");
                    }
                    hash.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }

                var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (copied != reference.Length || !string.Equals(actualHash, reference.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Контрольная сумма файла проекта не совпадает.");
                }
            }

            var metadataPath = Path.Combine(temporaryPath, "project.iskra.json");
            if (!File.Exists(metadataPath))
            {
                throw new InvalidDataException("В архиве отсутствуют метаданные проекта.");
            }

            var project = JsonSerializer.Deserialize<IskraProject>(
                File.ReadAllText(metadataPath), ProjectStore.SerializerOptionsForArchive)
                ?? throw new InvalidDataException("Не удалось прочитать метаданные проекта.");
            ValidateProjectData(project);
            project.Name = newName;
            Directory.CreateDirectory(Path.Combine(temporaryPath, "sounds"));
            Directory.CreateDirectory(Path.Combine(temporaryPath, "sprites"));
            File.WriteAllText(Path.Combine(temporaryPath, "code.txt"), File.Exists(Path.Combine(temporaryPath, "code.txt"))
                ? File.ReadAllText(Path.Combine(temporaryPath, "code.txt"))
                : string.Empty);
            foreach (var scene in project.Scenes)
            {
                ProjectStore.MigrateLegacySceneDirectories(temporaryPath, scene);
                Directory.CreateDirectory(Path.Combine(temporaryPath, "sounds", scene.Id.ToString("N")));
                Directory.CreateDirectory(Path.Combine(temporaryPath, "sprites", scene.Id.ToString("N")));
            }
            ProjectStore.SaveProjectToDirectory(project, temporaryPath);
            Directory.Move(temporaryPath, finalPath);
            return project;
        }
        catch
        {
            if (Directory.Exists(temporaryPath))
            {
                Directory.Delete(temporaryPath, recursive: true);
            }
            throw;
        }
    }

    private static Dictionary<string, ZipArchiveEntry> IndexEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumFiles + 1)
        {
            throw new InvalidDataException("В архиве слишком много файлов.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') || entry.Name.Length == 0 ||
                (entry.FullName != ManifestEntryName &&
                 !System.Text.RegularExpressions.Regex.IsMatch(entry.FullName, "^objects/[0-9A-Fa-f]{64}$")))
            {
                throw new InvalidDataException("В архиве найден недопустимый путь.");
            }
            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException("В архиве есть повторяющиеся имена файлов.");
            }
        }
        return entries;
    }

    private static ProjectArchiveManifest ReadManifest(ZipArchiveEntry entry)
    {
        if (entry.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("Манифест архива слишком велик.");
        }

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (!root.TryGetProperty("FormatVersion", out var versionElement) ||
            !TryReadFormatVersion(versionElement, out var version))
        {
            throw new InvalidDataException("Не удалось прочитать версию формата проекта.");
        }
        if (version > CurrentFormatVersion)
        {
            throw new InvalidDataException("Проект создан в новой версии Искры. Обновите приложение.");
        }
        if (version < 1)
        {
            throw new InvalidDataException("Эта версия формата проекта не поддерживается.");
        }
        if (!root.TryGetProperty("Files", out var filesElement))
        {
            throw new InvalidDataException("Не удалось прочитать манифест проекта.");
        }
        var files = JsonSerializer.Deserialize<SortedDictionary<string, ArchiveFileReference>>(filesElement.GetRawText())
            ?? throw new InvalidDataException("Не удалось прочитать манифест проекта.");
        return new ProjectArchiveManifest(version, files);
    }

    private static bool TryReadFormatVersion(JsonElement element, out int version)
    {
        version = 0;
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out version);
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (string.Equals(text, "1", StringComparison.Ordinal) ||
                string.Equals(text, "1.0", StringComparison.Ordinal))
            {
                version = 1;
                return true;
            }
        }
        return false;
    }

    private static ZipArchiveEntry GetBlob(IReadOnlyDictionary<string, ZipArchiveEntry> entries, string hash)
    {
        if (!entries.TryGetValue($"objects/{hash.ToLowerInvariant()}", out var entry))
        {
            throw new InvalidDataException("В архиве отсутствует часть одного из файлов проекта.");
        }
        return entry;
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        var destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Путь файла выходит за пределы проекта.");
        }
        return destination;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static void EnsureSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') || path.Contains('\\'))
        {
            throw new InvalidDataException("В архиве найден некорректный путь.");
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.StartsWith(".delete-", StringComparison.Ordinal) ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            throw new InvalidDataException("В архиве найден небезопасный путь.");
        }
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsAlreadyCompressed(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".mp3" or ".ogg" or ".mp4" or ".zip" or ".iskp";

    private static void ValidateProjectData(IskraProject project)
    {
        if (project.Scenes is null || project.Scenes.Count == 0 ||
            project.Scenes.Any(scene => !ProjectStore.IsValidProjectName(scene.Name, out _) || scene.Objects is null))
        {
            throw new InvalidDataException("Структура сцен в архиве некорректна.");
        }

        foreach (var scene in project.Scenes)
        {
            if (scene.Objects.Any(item => !ProjectStore.IsValidProjectName(item.Name, out _)))
            {
                throw new InvalidDataException("Имя объекта в архиве некорректно.");
            }
        }
    }
}

internal sealed record ProjectArchiveManifest(int FormatVersion, SortedDictionary<string, ArchiveFileReference> Files);
internal sealed record ArchiveFileReference(string Sha256, long Length);
