using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Maps;

public enum MapEncounterMode { Disabled, PaintedOnly, WholeMap }

public sealed record MapEncounterTile(Guid TableId, int? Rate = null);

public sealed class MapEncounters
{
    public MapEncounterMode Mode { get; set; }
    public Guid DefaultTableId { get; set; }
    public int? Rate { get; set; }

    // An absent key inherits. An empty UUID marks a safe tile.
    public Dictionary<int, MapEncounterTile> Tiles { get; set; } = [];

    public static int TileKey(int x, int y) => (y << 16) | x;

    public MapEncounterTile? Resolve(int x, int y)
    {
        if (Mode is not (MapEncounterMode.PaintedOnly or MapEncounterMode.WholeMap)) return null;
        if (Tiles?.TryGetValue(TileKey(x, y), out var tile) == true)
            return tile is { TableId: var id } && id != Guid.Empty ? tile : null;
        return Mode == MapEncounterMode.WholeMap && DefaultTableId != Guid.Empty
            ? new(DefaultTableId, Rate)
            : null;
    }

    public bool References(Guid id) =>
        id != Guid.Empty && (DefaultTableId == id || Tiles?.Values.Any(tile => tile?.TableId == id) == true);

    public bool IsValid(int width, int height, Func<Guid, bool> exists) =>
        Enum.IsDefined(Mode) && Rate is null or (>= 0 and <= 100) &&
        (Mode != MapEncounterMode.WholeMap || DefaultTableId != Guid.Empty) &&
        (DefaultTableId == Guid.Empty || exists(DefaultTableId)) && Tiles != null &&
        Tiles.All(pair => (pair.Key & 0xffff) < width && (pair.Key >> 16) is >= 0 &&
            (pair.Key >> 16) < height && pair.Value != null && pair.Value.Rate is null or (>= 0 and <= 100) &&
            (pair.Value.TableId == Guid.Empty || exists(pair.Value.TableId)));

    public static MapEncounters Parse(string? json)
    {
        try { return JsonConvert.DeserializeObject<MapEncounters>(json ?? "{}") ?? new(); }
        catch (JsonException) { return new(); }
    }

    public MapEncounters Copy() => new()
    {
        Mode = Mode, DefaultTableId = DefaultTableId, Rate = Rate,
        Tiles = Tiles == null ? [] : new(Tiles),
    };
}

public partial class MapDescriptor
{
    [NotMapped]
    public MapEncounters Encounters { get; set; } = new();

    public override void Load(string json, bool keepCreationTime = false)
    {
        Encounters = new();
        base.Load(json, keepCreationTime);
        Encounters ??= new();
    }

    [Column("Encounters"), JsonIgnore]
    public string? EncountersJson
    {
        get => Encounters == null ? null : JsonConvert.SerializeObject(Encounters);
        set => Encounters = MapEncounters.Parse(value);
    }
}
