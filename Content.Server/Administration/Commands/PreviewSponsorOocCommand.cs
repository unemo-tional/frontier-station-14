using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
internal sealed class PreviewSponsorOocCommand : IConsoleCommand
{
    [Dependency] private readonly IChatManager _chat = default!;

    public string Command => "preview_sponsor_ooc";
    public string Description => "Preview UNEON sponsor nick colors in your chat.";
    public string Help => "preview_sponsor_ooc";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player || args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        string[] labels =
        [
            "uneon-sub-tier-adventurer",
            "uneon-sub-tier-cosmopioneer",
            "uneon-sub-tier-shuttle-captain",
            "uneon-sub-tier-sector-baron",
            "uneon-sub-tier-frontier-legend"
        ];
        for (var i = 0; i < labels.Length; i++)
        {
            var message = Loc.GetString(labels[i]);
            var wrapped = Loc.GetString("chat-manager-send-ooc-wrap-message",
                ("playerName", ChatManager.FormatSponsorOocName(player.Name, i + 1)!),
                ("message", message));
            _chat.ChatMessageToOne(ChatChannel.OOC, message, wrapped, EntityUid.Invalid, false, player.Channel);
        }
    }
}
