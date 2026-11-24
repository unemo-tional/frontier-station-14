// Adapted from Lua-Frontier/Monolith-DS (AGPL-3.0).
using System.Linq;
using System.Numerics;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.GameTicking;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Client._NF.Shipyard.UI;

/// <summary>
/// Layout data read from the loaded map grids.
/// </summary>
public sealed record ShipyardPreviewInfo(Vector2i Dimensions, int Tiles, int Grids = 1);

/// <summary>
/// A cached map picture: all its grids rendered once.
/// </summary>
public sealed class ShipyardPreviewEntry
{
    public required string Key;
    public string Error = string.Empty;
    public IRenderTexture? Texture;
    public int PixelsPerTile;
    public ShipyardPreviewInfo? Info;
    public bool Failed;
    public long Bytes;
    public long LastUsed;
}

/// <summary>
/// Renders ships, stations and drones into cached textures on a client-only map,
/// drawn once through an off-screen viewport and deleted right away; nothing reaches the server.
/// </summary>
public sealed partial class ShipyardPreviewRenderSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IDependencyCollection _deps = default!;
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const long CacheBudgetBytes = 96L * 1024 * 1024;
    private const long MaxEntryBytes = 32L * 1024 * 1024;
    private const int MaxTextureSide = 4096;
    private const int BaseTilePixels = 32;
    private static readonly HashSet<string> PreviewComponents = new()
    {
        "Transform", "MetaData", "Map", "MapGrid", "DecalGrid", "Physics", "Fixtures",
        "Sprite", "Appearance", "IconSmooth",
    };
    private const int SettleFrames = 2;
    private const int MaxUndrawnFrames = 120;
    // -- Спрайты нарисованы 32 пикселя на тайл: рендер в родном размере и вывод без сглаживания дают чёткую картинку при любом зуме
    private static readonly int[] PixelsPerTileSteps = { 32, 16 };
    private static readonly Color Transparent = new(0f, 0f, 0f, 0f);

    private readonly Dictionary<string, ShipyardPreviewEntry> _cache = new();
    private long _cacheBytes;
    private long _useCounter;
    private Job? _job;
    private bool _shuttingDown;
    private readonly Dictionary<string, Job> _pending = new();
    private readonly Dictionary<object, string> _owners = new();
    private readonly List<string> _notify = new();

    /// <summary>
    /// Raised with the map path once its picture is ready or failed; an empty id means the cache was reset.
    /// </summary>
    public event Action<string>? PreviewChanged;

    private enum Stage : byte
    {
        Queued,
        Settling,
        Ready,
    }

    private sealed class Job
    {
        public required string Path;
        public LoadResult? Loaded;
        public string Error = string.Empty;
        public Stage Stage;
        public int Frames;
        public EntityUid Map;
        public MapId MapId;
        public Box2 Bounds;
        public ShipyardPreviewInfo? Info;
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => Clear());
        _net.Disconnect += OnDisconnect;
        _proto.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        _net.Disconnect -= OnDisconnect;
        _proto.PrototypesReloaded -= OnPrototypesReloaded;
        _shuttingDown = true;
        Clear();
        base.Shutdown();
    }

    private void OnDisconnect(object? sender, NetDisconnectedArgs args)
    {
        Clear();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<VesselPrototype>() || args.WasModified<EntityPrototype>())
            Clear();
    }

    public ShipyardPreviewEntry? Get(string vesselId)
    {
        if (!_cache.TryGetValue(vesselId, out var entry))
            return null;

        entry.LastUsed = ++_useCounter;
        return entry;
    }

    public bool IsLoading(string key) => _job?.Path == key || _pending.ContainsKey(key);

    public void Request(string path, object owner)
    {
        if (_shuttingDown) return;
        if (_owners.TryGetValue(owner, out var previous) && previous != path) Release(owner);
        _owners[owner] = path;
        if (_cache.ContainsKey(path) || IsLoading(path)) return;
        _pending[path] = new Job { Path = path };
    }

    public void Release(object owner)
    {
        if (!_owners.Remove(owner, out var path) || _owners.ContainsValue(path)) return;
        _pending.Remove(path);
        if (_job?.Path == path) Cancel();
        if (_cache.TryGetValue(path, out var entry) && entry.Failed) _cache.Remove(path);
    }

    public void Cancel()
    {
        if (_job?.Loaded is { } loaded)
            QueuePreviewDeletion(loaded);
        _job = null;
    }

    private void QueuePreviewDeletion(LoadResult loaded)
    {
        foreach (var map in loaded.Maps)
            QueueDel(map.Owner);
        foreach (var uid in loaded.Entities)
            QueueDel(uid);
    }

    public void Clear()
    {
        Cancel();
        _pending.Clear();
        _owners.Clear();

        foreach (var entry in _cache.Values)
        {
            entry.Texture?.Dispose();
        }

        _cache.Clear();
        _cacheBytes = 0;
        _notify.Clear();
        PreviewChanged?.Invoke(string.Empty);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // -- Уведомление из Draw откладывается до кадра: окно перестраивает свои элементы, а посреди отрисовки дерево UI менять нельзя
        if (_notify.Count > 0)
        {
            var ids = _notify.ToArray();
            _notify.Clear();
            foreach (var id in ids)
            {
                PreviewChanged?.Invoke(id);
            }
        }

        if (_job == null && _pending.Count > 0)
        {
            var next = _pending.First();
            _pending.Remove(next.Key);
            _job = next.Value;
        }
        if (_job is not { } job) return;

        switch (job.Stage)
        {
            // -- Кадр паузы: надпись загрузки успевает отрисоваться до пролага от загрузки грида
            case Stage.Queued when job.Frames == 0:
                job.Frames++;
                break;
            case Stage.Queued:
                Load(job);
                break;
            case Stage.Settling:
                // -- Сглаживание стен и визуалайзеры обновляются в FrameUpdate, без ожидания снимок выйдет "сырым"
                if (--job.Frames <= 0)
                    job.Stage = Stage.Ready;
                break;
            case Stage.Ready:
                // -- Рендер идёт только из Draw окна верфи; закрытое окно не держит временную карту
                if (++job.Frames > MaxUndrawnFrames)
                {
                    job.Error = "Окно предпросмотра не смогло отрисовать план. Откройте его снова.";
                    Fail(job);
                }
                break;
        }
    }

    private void Load(Job job)
    {
        try
        {
            if (!TryBuildMapCore(job.Path, out job.Map, out job.MapId, out job.Bounds, out job.Info, out job.Loaded))
            {
                Fail(job);
                return;
            }

            job.Stage = Stage.Settling;
            job.Frames = SettleFrames;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load shipyard preview for {job.Path}: {e}");
            Fail(job);
        }
    }

    /// <summary>
    /// Loads the saved grids on a new client-only map. The caller owns the map and deletes it.
    /// </summary>
    public bool TryBuildGrid(VesselPrototype vessel, out EntityUid map, out MapId mapId, out Box2 bounds, out ShipyardPreviewInfo? info)
        => TryBuildMap(vessel.ShuttlePath.ToRootedPath().ToString(), out map, out mapId, out bounds, out info);

    public bool TryBuildMap(string path, out EntityUid map, out MapId mapId, out Box2 bounds, out ShipyardPreviewInfo? info)
        => TryBuildMapCore(path, out map, out mapId, out bounds, out info, out _);

    private bool TryBuildMapCore(string path, out EntityUid map, out MapId mapId, out Box2 bounds,
        out ShipyardPreviewInfo? info, out LoadResult? loaded)
    {
        loaded = null;
        map = EntityUid.Invalid;
        mapId = MapId.Nullspace;
        bounds = default;
        info = null;

        if (!(path.StartsWith("/Maps/", StringComparison.Ordinal) || path.StartsWith("/SharedMaps/", StringComparison.Ordinal))
            || path.Contains("..", StringComparison.Ordinal) || !_loader.TryReadFile(new(path), out var data))
            return false;

        // Bound entity creation before deserialization; resolution alone cannot bound RAM/loading work.
        if (!data.TryGet("entities", out SequenceDataNode? savedGroups)
            || savedGroups.OfType<MappingDataNode>().Sum(g => g.Get<SequenceDataNode>("entities").Count) > 50000)
            return false;


        var read = new BeforeEntityReadEvent();
        RaiseLocalEvent(read);
        KeepPreviewComponents(data, read.RenamedPrototypes);

        // -- Свой проход вместо TryLoadGrid: спрайты чинятся между созданием и стартом сущностей
        var deserializer = new EntityDeserializer(_deps,
            data,
            new DeserializationOptions { LogOrphanedGrids = false, PauseMaps = true },
            read.RenamedPrototypes,
            read.DeletedPrototypes);

        if (!deserializer.TryProcessData())
            return false;

        var result = deserializer.Result;
        try
        {
            deserializer.CreateEntities();
            foreach (var uid in result.Entities)
            {
                RestoreSprite(uid);
            }

            deserializer.StartEntities();
            if (result.Grids.Count == 0 || result.Maps.Count != 1)
            {
                _loader.Delete(result);
                return false;
            }

            var mapEntity = result.Maps.First();
            map = mapEntity.Owner;
            mapId = mapEntity.Comp.MapId;
            _meta.SetEntityName(map, "Shipyard preview");
            Box2? combined = null;
            var tiles = 0;
            foreach (var grid in result.Grids)
            {
                var worldBounds = _xform.GetWorldMatrix(grid.Owner).TransformBox(grid.Comp.LocalAABB);
                combined = combined?.Union(worldBounds) ?? worldBounds;
                tiles += _map.GetAllTiles(grid.Owner, grid.Comp).Count();
            }
            bounds = combined!.Value;
            info = new ShipyardPreviewInfo(new Vector2i((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height)), tiles, result.Grids.Count);
            loaded = result;
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load shipyard preview grid {path}: {e}");
            _loader.Delete(result);
            map = EntityUid.Invalid;
            mapId = MapId.Nullspace;
            bounds = default;
            info = null;
            return false;
        }
    }

    /// <summary>
    /// Applies the sprite data a map-loaded entity received after its sprite was added.
    /// </summary>
    private void RestoreSprite(EntityUid uid)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        // Copy the initialized prototype layers when map deserialization only copied the data fields.
        if (MetaData(uid).EntityPrototype is not { } prototype
            || !prototype.TryGetComponent<SpriteComponent>(out var prototypeSprite, _factory))
            return;

        if (!sprite.AllLayers.Any())
            _sprite.CopySprite((EntityUid.Invalid, prototypeSprite), (uid, sprite));
        else if (sprite.BaseRSI == null && prototypeSprite.BaseRSI is { } rsi)
            _sprite.SetBaseRsi((uid, sprite), rsi);
    }

    private void KeepPreviewComponents(MappingDataNode data, Dictionary<string, string> renamedPrototypes)
    {
        if (!data.TryGet("entities", out SequenceDataNode? groups))
            return;

        foreach (var group in groups.Sequence.OfType<MappingDataNode>())
        {
            if (!group.TryGet("entities", out SequenceDataNode? entities))
                continue;

            EntityPrototype? prototype = null;
            if (group.TryGet("proto", out ValueDataNode? id) && id.Value.Length != 0)
                _proto.TryIndex(renamedPrototypes.GetValueOrDefault(id.Value, id.Value), out prototype);

            // MarkerSystem normally hides these sprites, but preview entities omit its Marker component.
            var hideMarkerSprite = prototype != null &&
                (prototype.Components.ContainsKey("Marker") || prototype.Components.ContainsKey("SpawnPoint"));
            bool KeepComponent(string name) =>
                PreviewComponents.Contains(name) && (!hideMarkerSprite || name != "Sprite");

            var excluded = prototype?.Components.Keys.Where(name => !KeepComponent(name)).ToArray()
                ?? Array.Empty<string>();
            foreach (var entity in entities.Sequence.OfType<MappingDataNode>())
            {
                if (entity.TryGet("components", out SequenceDataNode? components))
                {
                    for (var i = components.Sequence.Count - 1; i >= 0; i--)
                    {
                        if (components.Sequence[i] is MappingDataNode component
                            && component.TryGet("type", out ValueDataNode? type)
                            && !KeepComponent(type.Value))
                            components.RemoveAt(i);
                    }
                }

                if (excluded.Length == 0)
                    continue;

                if (!entity.TryGet("missingComponents", out SequenceDataNode? missing))
                {
                    missing = new SequenceDataNode();
                    entity.Add("missingComponents", missing);
                }

                foreach (var name in excluded)
                    missing.Add(new ValueDataNode(name));
            }
        }
    }


    /// <summary>
    /// Renders the pending grid; called from a preview control's Draw, where rendering into targets is allowed.
    /// </summary>
    public void RenderPending(IRenderHandle handle)
    {
        if (_job is not { Stage: Stage.Ready } job)
            return;

        var size = job.Bounds.Size + new Vector2(2f, 2f);
        var pixelsPerTile = PickPixelsPerTile(size);
        if (pixelsPerTile == 0)
        {
            Fail(job);
            return;
        }
        var pixelSize = new Vector2i(
            Math.Max(1, (int) MathF.Ceiling(size.X * pixelsPerTile)),
            Math.Max(1, (int) MathF.Ceiling(size.Y * pixelsPerTile)));

        var required = (long)pixelSize.X * pixelSize.Y * 4;
        while (_cacheBytes + required > CacheBudgetBytes)
        {
            var oldest = _cache.Values.Where(e => !_owners.ContainsValue(e.Key)).MinBy(e => e.LastUsed);
            if (oldest == null)
            {
                job.Error = "Лимит памяти планов: закройте другое окно предпросмотра и откройте план снова.";
                Fail(job);
                return;
            }
            _cache.Remove(oldest.Key); _cacheBytes -= oldest.Bytes; oldest.Texture?.Dispose();
        }
        IClydeViewport? viewport = null;
        IRenderTexture? texture = null;
        try
        {
            viewport = _clyde.CreateViewport(pixelSize, "ShipyardPreview");
            viewport.AutomaticRender = false;
            viewport.ClearColor = Transparent;
            // -- ZEye без параллакса: обычный глаз рисует фон-параллакс и эффекты z-уровней поверх шаттла
            viewport.Eye = new Robust.Shared.Graphics.Eye
            {
                DrawFov = false,
                DrawLight = false,
                Position = new MapCoordinates(job.Bounds.Center, job.MapId),
                Zoom = new Vector2(BaseTilePixels / (float) pixelsPerTile),
            };
            viewport.Render();

            texture = _clyde.CreateRenderTarget(pixelSize,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                new TextureSampleParameters { Filter = false },
                "ShipyardPreviewCache");

            var source = viewport.RenderTarget.Texture;
            handle.RenderInRenderTarget(texture, () =>
            {
                var screen = handle.DrawingHandleScreen;
                screen.SetTransform(Matrix3x2.Identity);
                screen.DrawTextureRect(source, UIBox2.FromDimensions(Vector2.Zero, pixelSize));
            }, Transparent);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to render shipyard preview for {job.Path}: {e}");
            texture?.Dispose();
            texture = null;
        }
        finally
        {
            viewport?.Dispose();
        }

        Finish(job, texture, pixelsPerTile, pixelSize);
    }

    private static int PickPixelsPerTile(Vector2 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0)
            return 0;
        foreach (var step in PixelsPerTileSteps)
        {
            if (Fits(step))
                return step;
        }

        // Keep both limits after fallback and account for pixel rounding before allocation.
        for (var step = 15; step >= 1; step--)
            if (Fits(step)) return step;
        return 0;

        bool Fits(int step)
        {
            var width = Math.Ceiling((double)size.X * step);
            var height = Math.Ceiling((double)size.Y * step);
            return width <= MaxTextureSide && height <= MaxTextureSide && width * height * 4 <= MaxEntryBytes;
        }
    }

    private void Fail(Job job)
    {
        Finish(job, null, 0, Vector2i.Zero);
    }

    private void Finish(Job job, IRenderTexture? texture, int pixelsPerTile, Vector2i pixelSize)
    {
        if (job.Loaded is { } loaded)
            QueuePreviewDeletion(loaded);

        if (_job == job)
            _job = null;

        var entry = new ShipyardPreviewEntry
        {
            Key = job.Path,
            Error = job.Error,
            Texture = texture,
            PixelsPerTile = pixelsPerTile,
            Info = job.Info,
            Failed = texture == null,
            Bytes = texture == null ? 0 : (long) pixelSize.X * pixelSize.Y * 4,
            LastUsed = ++_useCounter,
        };

        if (_cache.Remove(entry.Key, out var old))
        {
            _cacheBytes -= old.Bytes;
            old.Texture?.Dispose();
        }

        _cache[entry.Key] = entry;
        _cacheBytes += entry.Bytes;
        Evict(entry.Key);

        _notify.Add(entry.Key);
    }

    private void Evict(string keep)
    {
        while (_cacheBytes > CacheBudgetBytes && _cache.Count > 1)
        {
            var oldest = _cache.Values.Where(e => e.Key != keep && !_owners.ContainsValue(e.Key)).MinBy(e => e.LastUsed);
            if (oldest == null)
                return;

            _cache.Remove(oldest.Key);
            _cacheBytes -= oldest.Bytes;
            oldest.Texture?.Dispose();
        }
    }
}
