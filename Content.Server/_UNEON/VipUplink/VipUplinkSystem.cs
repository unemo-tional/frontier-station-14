using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Server._NF.Bank;
using Content.Server._UNEON.Subscriptions;
using Content.Shared._UNEON.VipUplink;
using Content.Shared.CartridgeLoader;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.PDA;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._UNEON.VipUplink;

public sealed class VipUplinkSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private readonly UneonSubscriptionsSystem _subscriptions = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    private static readonly Vector2i[] AdjacentOffsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
    private readonly Dictionary<EntityUid, TimeSpan> _lastPurchase = new();
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PdaComponent, MapInitEvent>(OnPdaInit, after: new[] { typeof(CartridgeLoaderSystem) });
        SubscribeLocalEvent<VipUplinkCartridgeComponent, CartridgeUiReadyEvent>(OnReady);
        SubscribeLocalEvent<VipUplinkCartridgeComponent, CartridgeProgramAccessAttemptEvent>(OnAccess);
        // The loader relays this as its base event, as with other cartridge applications.
        SubscribeLocalEvent<VipUplinkCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnAccess(EntityUid uid, VipUplinkCartridgeComponent component, CartridgeProgramAccessAttemptEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session) || _subscriptions.GetTier(session.UserId) <= 0)
            args.Cancel();
    }

    private void OnPdaInit(EntityUid uid, PdaComponent component, MapInitEvent args)
    {
        if (!TryComp<CartridgeLoaderComponent>(uid, out var loader) ||
            _cartridges.HasProgram<VipUplinkCartridgeComponent>(uid))
            return;
        // Reserve an additional slot: existing PDA programs keep all their original disk space.
        loader.DiskSpace++;
        _cartridges.InstallProgram(uid, "UneonVipUplinkCartridge", deinstallable: false, loader: loader);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRefresh)
            return;
        _nextRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } actor)
                continue;
            var access = EnsureComp<VipUplinkAccessComponent>(actor);
            var tier = _subscriptions.GetTier(session.UserId);
            if (access.Tier != tier)
            {
                access.Tier = tier;
                Dirty(actor, access);
            }
        }
        var query = EntityQueryEnumerator<VipUplinkCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is { } loader &&
                TryComp<CartridgeLoaderComponent>(loader, out var comp) && comp.ActiveProgram == uid)
                UpdateViewers(loader, comp);
        }
        foreach (var actor in _lastPurchase.Keys.Where(a => Deleted(a) ||
                     _timing.CurTime - _lastPurchase[a] > TimeSpan.FromMinutes(1)).ToArray())
            _lastPurchase.Remove(actor);
    }

    private void OnReady(EntityUid uid, VipUplinkCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        if (TryComp<CartridgeLoaderComponent>(args.Loader, out var loader) && loader.ActiveProgram == uid)
            UpdateViewers(args.Loader, loader);
    }

    private void UpdateViewers(EntityUid loader, CartridgeLoaderComponent component)
    {
        foreach (var actor in _ui.GetActors(loader, component.UiKey))
        {
            var products = new List<VipUplinkListing>();
            var tier = 0;
            var account = "";
            string? login = null;
            if (_players.TryGetSessionByEntity(actor, out var session))
            {
                tier = _subscriptions.GetTier(session.UserId);
                account = session.UserId.ToString();
                login = session.Name;
            }
            // No other subscriber ever receives another account's personal suit in this response.
            if (tier > 0)
            {
                foreach (var product in _prototypes.EnumeratePrototypes<VipUplinkProductPrototype>().OrderBy(p => p.ID))
                {
                    if (product.IsAvailable(tier, account, login))
                        products.Add(new VipUplinkListing(product));
                }
            }
            _bank.TryGetBalance(actor, out var balance);
            _ui.ServerSendUiMessage(loader, component.UiKey,
                new VipUplinkStateMessage(new VipUplinkState(tier, balance, products)), actor);
        }
    }

    private void OnMessage(EntityUid uid, VipUplinkCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not VipUplinkBuyMessage message)
            return;
        var loader = GetEntity(args.LoaderUid);
        var actor = args.Actor;
        if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp) || loaderComp.ActiveProgram != uid ||
            !TryComp<CartridgeComponent>(uid, out var cartridge) || cartridge.LoaderUid != loader ||
            !_ui.GetActors(loader, loaderComp.UiKey).Contains(actor) ||
            !_players.TryGetSessionByEntity(actor, out var session) || !_mobs.IsAlive(actor) ||
            !_interaction.InRangeUnobstructed(actor, loader) || Transform(actor).MapID == MapId.Nullspace)
            return;
        if (_lastPurchase.TryGetValue(actor, out var last) && _timing.CurTime - last < TimeSpan.FromSeconds(1))
            return;
        _lastPurchase[actor] = _timing.CurTime;
        if (string.IsNullOrEmpty(message.ProductId) || message.ProductId.Length > 128 ||
            !_prototypes.TryIndex<VipUplinkProductPrototype>(message.ProductId, out var product) ||
            !product.IsAvailable(_subscriptions.GetTier(session.UserId), session.UserId.ToString(), session.Name) ||
            !_bank.TryGetBalance(actor, out var balance) || balance < product.Price)
        {
            _popups.PopupEntity(Loc.GetString("uneon-vip-failed"), actor, actor);
            return;
        }

        var delivery = Transform(actor).Coordinates;
        if (product.DeliverAdjacent && !TryGetAdjacentDelivery(actor, delivery, out delivery))
        {
            _popups.PopupEntity(Loc.GetString("uneon-vip-no-delivery-space"), actor, actor);
            return;
        }

        // Stage the item outside the world before charging. A failed prototype spawn never costs money.
        var items = new List<EntityUid>();
        try
        {
            var remaining = product.StackAmount;
            do
            {
                var item = Spawn(product.Product);
                items.Add(item);
                if (remaining == 0)
                    break;

                var stack = Comp<StackComponent>(item);
                var count = Math.Min(remaining, _stacks.GetMaxCount(stack));
                if (count <= 0)
                    throw new InvalidOperationException("The product has no usable stack capacity.");
                _stacks.SetCount(item, count, stack);
                remaining -= count;
            } while (remaining > 0);
        }
        catch (Exception e)
        {
            foreach (var item in items)
                QueueDel(item);
            Log.Error($"VIP uplink could not spawn product {product.ID}: {e.GetType().Name}");
            _popups.PopupEntity(Loc.GetString("uneon-vip-failed"), actor, actor);
            return;
        }
        if (!_bank.TryBankWithdraw(actor, product.Price))
        {
            foreach (var item in items)
                QueueDel(item);
            _popups.PopupEntity(Loc.GetString("uneon-vip-failed"), actor, actor);
            return;
        }
        // Use the actor's coordinates, never a client supplied position or the PDA's ID card owner.
        foreach (var item in items)
        {
            _transform.SetCoordinates(item, delivery);
            if (!product.DeliverAdjacent)
                _hands.TryPickupAnyHand(actor, item);
        }
        Log.Info($"VIP uplink: {session.UserId} bought {product.ID} for {product.Price}");
        _popups.PopupEntity(Loc.GetString("uneon-vip-success"), actor, actor);
        UpdateViewers(loader, loaderComp);
    }

    private bool TryGetAdjacentDelivery(EntityUid actor, EntityCoordinates origin, out EntityCoordinates delivery)
    {
        delivery = default;
        if (!_turf.TryGetTileRef(origin, out var current) ||
            !TryComp<MapGridComponent>(current.Value.GridUid, out var grid))
            return false;

        var gridUid = current.Value.GridUid;
        foreach (var offset in AdjacentOffsets)
        {
            var indices = current.Value.GridIndices + offset;
            if (!_map.TryGetTileRef(gridUid, grid, indices, out var tile) ||
                tile.Tile.IsEmpty || _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                continue;

            var coordinates = _map.GridTileToLocal(gridUid, grid, indices);
            if (!_interaction.InRangeUnobstructed(actor, coordinates, popup: false))
                continue;

            delivery = coordinates;
            return true;
        }

        return false;
    }
}
