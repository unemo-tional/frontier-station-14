using Content.Shared.Roles;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking.Events;

[ByRefEvent]
public struct IsJobAllowedEvent(ICommonSession player, ProtoId<JobPrototype> jobId, EntityUid station, bool cancelled = false)
{
    public readonly ICommonSession Player = player;
    public readonly ProtoId<JobPrototype> JobId = jobId;
    public readonly EntityUid Station = station;
    public bool Cancelled = cancelled;
}
