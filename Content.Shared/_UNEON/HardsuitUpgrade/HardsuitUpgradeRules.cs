namespace Content.Shared._UNEON.HardsuitUpgrade;

/// <summary>Values and recipes are per operation; material quantities are whole sheets/crystals, not lathe volume.</summary>
public static class HardsuitUpgradeRules
{
    public const string Slot = "uneon-hardsuit";
    public const float Step = 5;

    public static float Limit(HardsuitUpgradeKind kind) => kind switch
    {
        HardsuitUpgradeKind.Blunt or HardsuitUpgradeKind.Slash or HardsuitUpgradeKind.Piercing => 80,
        HardsuitUpgradeKind.Heat or HardsuitUpgradeKind.Radiation => 100,
        HardsuitUpgradeKind.Caustic => 95,
        HardsuitUpgradeKind.Explosion => 70,
        HardsuitUpgradeKind.Speed => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string? DamageType(HardsuitUpgradeKind kind) => kind switch
    {
        HardsuitUpgradeKind.Blunt => "Blunt",
        HardsuitUpgradeKind.Slash => "Slash",
        HardsuitUpgradeKind.Piercing => "Piercing",
        HardsuitUpgradeKind.Heat => "Heat",
        HardsuitUpgradeKind.Radiation => "Radiation",
        HardsuitUpgradeKind.Caustic => "Caustic",
        _ => null
    };

    public static float Protection(float coefficient) => MathF.Round((1 - coefficient) * 100, 2);

    public static float Target(HardsuitUpgradeKind kind, float current) => kind == HardsuitUpgradeKind.Speed
        ? MathF.Max(0, MathF.Round(current - Step, 2))
        : MathF.Max(current, MathF.Min(Limit(kind), MathF.Round(current + Step, 2)));

    public static bool CanImprove(HardsuitUpgradeKind kind, float current) =>
        float.IsFinite(current) && (kind == HardsuitUpgradeKind.Speed ? current > 0 : current < Limit(kind));

    public static int Seconds(HardsuitUpgradeKind kind, float target) =>
        kind == HardsuitUpgradeKind.Speed || target > 60 ? 20 : target > 40 ? 10 : 5;

    public static Dictionary<string, int> Cost(HardsuitUpgradeKind kind, float target) => (kind, target) switch
    {
        (HardsuitUpgradeKind.Blunt, <= 40) => new() { ["Steel"] = 30, ["Plasteel"] = 15, ["Plastic"] = 10 },
        (HardsuitUpgradeKind.Blunt, <= 60) => new() { ["Steel"] = 30, ["Plasteel"] = 20, ["Plastic"] = 10 },
        (HardsuitUpgradeKind.Blunt, _) => new() { ["Steel"] = 30, ["Plasteel"] = 30, ["Plastic"] = 20 },
        (HardsuitUpgradeKind.Slash, <= 40) => new() { ["Steel"] = 20, ["PlasmaGlass"] = 15 },
        (HardsuitUpgradeKind.Slash, <= 60) => new() { ["Steel"] = 20, ["PlasmaGlass"] = 20 },
        (HardsuitUpgradeKind.Slash, _) => new() { ["Steel"] = 30, ["PlasmaGlass"] = 30 },
        (HardsuitUpgradeKind.Piercing, <= 40) => new() { ["Steel"] = 30, ["Plasteel"] = 15, ["Plasma"] = 10 },
        (HardsuitUpgradeKind.Piercing, <= 60) => new() { ["Steel"] = 30, ["Plasteel"] = 25, ["Uranium"] = 10 },
        (HardsuitUpgradeKind.Piercing, _) => new() { ["Steel"] = 45, ["Plasteel"] = 30, ["Uranium"] = 30 },
        (HardsuitUpgradeKind.Heat, <= 40) => new() { ["Durathread"] = 10 },
        (HardsuitUpgradeKind.Heat, <= 60) => new() { ["Durathread"] = 20 },
        (HardsuitUpgradeKind.Heat, _) => new() { ["Durathread"] = 25 },
        (HardsuitUpgradeKind.Radiation, <= 40) => new() { ["UraniumGlass"] = 10, ["Bananium"] = 5 },
        (HardsuitUpgradeKind.Radiation, <= 60) => new() { ["UraniumGlass"] = 20, ["Bananium"] = 7 },
        (HardsuitUpgradeKind.Radiation, <= 90) => new() { ["UraniumGlass"] = 30, ["Bananium"] = 10 },
        (HardsuitUpgradeKind.Radiation, _) => new() { ["UraniumGlass"] = 30, ["Uranium"] = 30, ["Bananium"] = 10 },
        (HardsuitUpgradeKind.Caustic, <= 40) => new() { ["Cloth"] = 15, ["Plasma"] = 10 },
        (HardsuitUpgradeKind.Caustic, <= 60) => new() { ["Cloth"] = 20, ["Plasma"] = 10 },
        (HardsuitUpgradeKind.Caustic, _) => new() { ["Cloth"] = 20, ["Plasma"] = 15 },
        (HardsuitUpgradeKind.Explosion, <= 40) => new() { ["Steel"] = 30, ["Plasteel"] = 10 },
        (HardsuitUpgradeKind.Explosion, <= 60) => new() { ["Steel"] = 30, ["Plasteel"] = 20 },
        (HardsuitUpgradeKind.Explosion, _) => new() { ["Steel"] = 40, ["Plasteel"] = 40 },
        (HardsuitUpgradeKind.Speed, _) => new() { ["Bluespace"] = 5 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
