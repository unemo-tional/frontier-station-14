using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._UNEON.VipUplink;

[Serializable, NetSerializable]
public enum VipUplinkCategory : byte
{
    Resources,
    Hardsuits,
    Food,
    Cloaks,
    Misc
}

[Serializable, NetSerializable]
public sealed class VipUplinkBuyMessage : CartridgeMessageEvent
{
    public readonly string ProductId;
    public VipUplinkBuyMessage(string productId) { ProductId = productId; }
}

[Serializable, NetSerializable]
public sealed class VipUplinkState : BoundUserInterfaceState
{
    public readonly bool Allowed;
    public readonly int Balance;
    public readonly List<VipUplinkListing> Products;
    public VipUplinkState(bool allowed, int balance, List<VipUplinkListing> products)
    { Allowed = allowed; Balance = balance; Products = products; }
}

[Serializable, NetSerializable]
public sealed class VipUplinkListing
{
    public readonly string Id;
    public readonly string? Name;
    public readonly int Price;
    public readonly string Prototype;
    public readonly VipUplinkCategory Category;
    public readonly int StackAmount;

    public VipUplinkListing(VipUplinkProductPrototype product)
    {
        Id = product.ID;
        Name = product.Name?.ToString();
        Price = product.Price;
        Prototype = product.Product.Id;
        Category = product.Category;
        StackAmount = product.StackAmount;
    }
}

/// <summary>Personal catalogue and balance are sent only to the PDA viewer concerned, never broadcast.</summary>
[Serializable, NetSerializable]
public sealed class VipUplinkStateMessage : BoundUserInterfaceMessage
{
    public readonly VipUplinkState State;
    public VipUplinkStateMessage(VipUplinkState state) { State = state; }
}
