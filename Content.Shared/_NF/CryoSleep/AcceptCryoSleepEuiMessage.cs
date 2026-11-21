using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.CryoSleep;

[Serializable, NetSerializable]
public sealed class CryoSleepEuiState(TimeSpan bodyRetention) : EuiStateBase
{
    public readonly TimeSpan BodyRetention = bodyRetention;
}

[Serializable, NetSerializable]
public enum AcceptCryoUiButton
{
    Deny,
    Accept,
}

[Serializable, NetSerializable]
public sealed class AcceptCryoChoiceMessage : EuiMessageBase
{
    public readonly AcceptCryoUiButton Button;

    public AcceptCryoChoiceMessage(AcceptCryoUiButton button)
    {
        Button = button;
    }
}
