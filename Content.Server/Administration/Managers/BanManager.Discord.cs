using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Discord;
using Content.Shared.CCVar;

namespace Content.Server.Administration.Managers;

public sealed partial class BanManager
{
    [Dependency] private DiscordWebhook _discordWebhook = default!;

    private async Task SendServerBanWebhook(int banId, ServerBanDef ban, string? targetName, string adminName)
    {
        var url = _cfg.GetCVar(CCVars.DiscordBanWebhook);
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (await _discordWebhook.GetWebhook(url) is not { } webhook)
                return;

            var duration = Loc.GetString("discord-ban-permanent");
            if (ban.ExpirationTime is { } expires)
            {
                var timestamp = expires.ToUnixTimeSeconds();
                duration = Loc.GetString("discord-ban-expires",
                    ("absolute", FormattableString.Invariant($"<t:{timestamp}:F>")),
                    ("relative", FormattableString.Invariant($"<t:{timestamp}:R>")));
            }
            var reason = FormatBanWebhookText(ban.Reason, 1000);
            var payload = new WebhookPayload
            {
                AllowedMentions = new WebhookMentions(),
                Embeds =
                [
                    new WebhookEmbed
                    {
                        Title = Loc.GetString("discord-ban-title", ("id", banId.ToString())),
                        Description = duration,
                        Color = 0xED4245,
                        Fields =
                        [
                            new WebhookEmbedField
                            {
                                Name = Loc.GetString("discord-ban-player"),
                                Value = $"`{FormatBanWebhookText(targetName, 200)}`",
                            },
                            new WebhookEmbedField
                            {
                                Name = Loc.GetString("discord-ban-admin"),
                                Value = $"`{FormatBanWebhookText(adminName, 200)}`",
                            },
                            new WebhookEmbedField
                            {
                                Name = Loc.GetString("discord-ban-reason"),
                                Value = $"```\n{reason}\n```",
                                Inline = false,
                            },
                        ],
                        Footer = ban.RoundId is { } round
                            ? new WebhookEmbedFooter { Text = Loc.GetString("discord-ban-round", ("id", round.ToString())) }
                            : null,
                    },
                ],
            };

            using var response = await _discordWebhook.CreateMessage(webhook.ToIdentifier(), payload);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Could not send Discord notification for server ban #{banId} ({e.GetType().Name}).");
        }
    }

    private static string FormatBanWebhookText(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var text = value.Replace('`', 'ˋ').Trim();
        if (text.Length <= limit)
            return text;

        var end = char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;
        return text[..end] + "…";
    }
}
