using Content.Shared.Roles;

namespace Content.Shared._UNEON.Subscriptions;

public static class UneonJobPlaytime
{
    public static bool BypassesTimers(int tier, string job, LocId? station, bool preview = false)
    {
        return tier switch
        {
            5 => true,
            4 => job is "PlantManager" or "PlantTechnician" or "DirectorOfCare" ||
                 (BaronStation(job) is { } requiredStation && (preview || station == requiredStation)),
            _ => false
        };
    }

    public static LocId? BaronStation(string job) => job switch
    {
        "Pilot" => (LocId) "frontier-lobby-medical-subtext",
        "Cadet" or "Deputy" or "NFDetective" or "Brigmedic" or "PublicAffairsLiaison" =>
            (LocId) "frontier-lobby-nfsd-subtext",
        _ => default(LocId?)
    };

    public static bool BypassesRequirement(JobRequirement requirement, bool bypassTimers) =>
        bypassTimers && requirement is OverallPlaytimeRequirement or DepartmentTimeRequirement or RoleTimeRequirement;
}
