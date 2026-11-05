using System.Text.Json.Serialization;

namespace IskraStudio;

public enum ProjectOrientation
{
    Horizontal,
    Vertical
}

public sealed class IskraProject
{
    public string Name { get; set; } = string.Empty;
    public ProjectOrientation Orientation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public List<IskraScene> Scenes { get; set; } = [];

    [JsonIgnore]
    public string OrientationLabel => Orientation == ProjectOrientation.Horizontal
        ? "Горизонтальная"
        : "Вертикальная";

    [JsonIgnore]
    public string ScenesCountLabel => Scenes.Count switch
    {
        1 => "1 сцена",
        >= 2 and <= 4 => $"{Scenes.Count} сцены",
        _ => $"{Scenes.Count} сцен"
    };

    [JsonIgnore]
    public string MetaSummary => $"{OrientationLabel}  ·  {ScenesCountLabel}";
}

public sealed class IskraScene
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<IskraObject> Objects { get; set; } = [];
}

public sealed class IskraObject
{
    public static readonly Guid BackgroundId = new("A7B3C9D1-E5F2-4A8B-9C0D-1234567890AB");

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
