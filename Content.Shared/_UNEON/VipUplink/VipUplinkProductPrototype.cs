using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._UNEON.VipUplink;

/// <summary>Server-authoritative catalogue. Personal products require an account whitelist AND a tier.</summary>
[Prototype]
public sealed partial class VipUplinkProductPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public LocId? Name;
    [DataField(required: true)] public EntProtoId Product;
    [DataField] public VipUplinkCategory Category = VipUplinkCategory.Misc;
    [DataField] public int StackAmount;
    [DataField(required: true)] public int Price;
    [DataField] public int MinimumTier = 1;
    /// <summary>SS14 account UUIDs or login names. Empty means a shared product.</summary>
    [DataField] public HashSet<string> AllowedAccounts = new();

    public bool IsAvailable(int tier, string account, string? login = null)
    {
        return Price > 0 && StackAmount is >= 0 and <= 30 && MinimumTier > 0 && tier >= MinimumTier &&
               (AllowedAccounts.Count == 0 || AllowedAccounts.Contains(account) ||
                (login != null && AllowedAccounts.Contains(login, System.StringComparer.OrdinalIgnoreCase)));
    }
}
