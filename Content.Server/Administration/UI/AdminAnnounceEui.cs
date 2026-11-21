using Content.Server.Administration.Managers;
using Content.Server.Chat;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.EUI;
using Content.Server._UNEON.Tts;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Robust.Shared.Audio; // Frontier
using Robust.Shared.Player;

namespace Content.Server.Administration.UI
{
    public sealed partial class AdminAnnounceEui : BaseEui
    {
        [Dependency] private IAdminManager _adminManager = default!;
        [Dependency] private IChatManager _chatManager = default!;
        private readonly ChatSystem _chatSystem;
        private readonly UneonTtsSystem _tts;

        public AdminAnnounceEui()
        {
            IoCManager.InjectDependencies(this);
            var systems = IoCManager.Resolve<IEntitySystemManager>();
            _chatSystem = systems.GetEntitySystem<ChatSystem>();
            _tts = systems.GetEntitySystem<UneonTtsSystem>();
        }

        public override void Opened()
        {
            StateDirty();
        }

        public override EuiStateBase GetNewState()
        {
            return new AdminAnnounceEuiState();
        }

        public override void HandleMessage(EuiMessageBase msg)
        {
            base.HandleMessage(msg);

            switch (msg)
            {
                case AdminAnnounceEuiMsg.DoAnnounce doAnnounce:
                    if (!_adminManager.HasAdminFlag(Player, AdminFlags.Admin))
                    {
                        Close();
                        break;
                    }

                    switch (doAnnounce.AnnounceType)
                    {
                        case AdminAnnounceType.Server:
                            _chatManager.DispatchServerAnnouncement(doAnnounce.Announcement);
                            _tts.SpeakAnnouncement(Filter.Broadcast(), doAnnounce.Announcement);
                            break;
                        // TODO: Per-station announcement support
                        case AdminAnnounceType.Station:
                            _chatSystem.DispatchGlobalAnnouncement(doAnnounce.Announcement, doAnnounce.Announcer, colorOverride: Color.Gold);
                            break;
                        case AdminAnnounceType.Antag: // Frontier
                            _chatSystem.DispatchGlobalAnnouncement(doAnnounce.Announcement, doAnnounce.Announcer, true, new SoundPathSpecifier("/Audio/Announcements/war.ogg"), colorOverride: Color.Red);
                            break;
                    }

                    StateDirty();

                    if (doAnnounce.CloseAfter)
                        Close();

                    break;
            }
        }
    }
}
