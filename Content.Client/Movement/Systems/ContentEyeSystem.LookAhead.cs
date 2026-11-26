using System.Numerics;
using Content.Shared.Ghost;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Client.Movement.Systems;

public sealed partial class ContentEyeSystem
{
    [Dependency] private EyeCursorOffsetSystem _cursorOffset = default!;

    private const float LookAheadRange = 3f;
    private const float LookAheadSharpness = 8f;
    private EntityUid? _lookAheadOwner;
    private bool _lookAheadEnabled;
    private Vector2 _lookAheadTarget;
    private Vector2 _lookAheadOffset;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ToggleLookAhead, InputCmdHandler.FromDelegate(ToggleLookAhead))
            .Register<ContentEyeSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<ContentEyeSystem>();
        base.Shutdown();
    }

    private void ToggleLookAhead(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } uid || uid != _player.LocalEntity || !CanLookAhead(uid))
            return;

        SetLookAheadOwner(uid);
        _lookAheadEnabled = !_lookAheadEnabled;
    }

    private bool CanLookAhead(EntityUid uid)
    {
        return HasComp<ContentEyeComponent>(uid)
            && !HasComp<GhostComponent>(uid)
            && (!TryComp<PilotComponent>(uid, out var pilot) || pilot.Console == null);
    }

    private void SetLookAheadOwner(EntityUid? uid)
    {
        if (uid == _lookAheadOwner)
            return;

        _lookAheadOwner = uid;
        _lookAheadEnabled = false;
        _lookAheadTarget = Vector2.Zero;
        _lookAheadOffset = Vector2.Zero;
    }

    private void UpdateLookAhead(float frameTime)
    {
        if (_player.LocalEntity is not { } uid || !CanLookAhead(uid))
        {
            SetLookAheadOwner(null);
            return;
        }

        SetLookAheadOwner(uid);
        if (!_lookAheadEnabled)
            _lookAheadTarget = Vector2.Zero;
        else if (_cursorOffset.GetMouseOffset(LookAheadRange) is { } target)
            _lookAheadTarget = target;

        _lookAheadOffset = Vector2.Lerp(_lookAheadOffset, _lookAheadTarget, 1f - MathF.Exp(-LookAheadSharpness * frameTime));
        if (Vector2.DistanceSquared(_lookAheadOffset, _lookAheadTarget) < 0.0001f)
            _lookAheadOffset = _lookAheadTarget;
    }

    protected override Vector2 GetLookAheadOffset(EntityUid uid)
    {
        return _lookAheadOwner == uid && CanLookAhead(uid) ? _lookAheadOffset : Vector2.Zero;
    }
}
