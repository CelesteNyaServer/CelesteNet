using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.CelesteNet.Client.Utils;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Policy;
using YamlDotNet.Serialization;
using Keys = Microsoft.Xna.Framework.Input.Keys;

namespace Celeste.Mod.CelesteNet.Client
{
    [SettingName("modoptions_celestenetclient_title")]
    public class CelesteNetClientSettings : EverestModuleSettings
    {

        public const int SettingsVersionCurrent = 2;
        // since with this PR (probably going into v2.2) a big chunk of options are being
        // moved into SubMenu sub-classes, some migrating of settings will be in order
        [SettingIgnore]
        public int SettingsVersionDoNotEdit { get; set; } = 0;
        // NOTE: The default should be 0 or unset, and not the current version number,
        // because otherwise "unversioned" settings files could not be detected.
        [SettingIgnore, YamlIgnore]
        public int Version
        {
            get => SettingsVersionDoNotEdit;
            set
            {
                if (SettingsVersionDoNotEdit <= value && value <= SettingsVersionCurrent)
                    SettingsVersionDoNotEdit = value;
                else
                    Logger.LogDetailed(LogLevel.WRN, "CelesteNetClientSettings", $"Attempt to change Settings.Version from {SettingsVersionDoNotEdit} to {value} which is not allowed!");
            }
        }
        public string RefreshToken { get; set; }

        public string ExpiredTime { get; set; }
        #region Top Level Settings

        [SettingIgnore]
        public bool WantsToBeConnected { get; set; }

        [SettingIgnore, YamlIgnore]
        public TextMenu.Button LoginButton { get; protected set; }

        public enum ServerSelectOption
        {
            //AutoSelect = 0,
            MainServer = 0,
            BackupServer = 1
        }

        private ServerSelectOption serverSelect;

        public ServerSelectOption ServerSelect
        {
            get => serverSelect;
            set
            {
                //if (HttpUtils.Get("https://miaoedit.centralteam.cn/setting", 15000) == "1")
                //{
                //    serverSelect = ServerSelectOption.AutoSelect;
                //    return;
                //}
                serverSelect = value;
            }
        }

        [YamlIgnore]
        public bool Connected
        {
            get => CelesteNetClientModule.Instance.IsAlive;
            set
            {
                WantsToBeConnected = value;

                Server = serverSelect switch
                {
                    //ServerSelectOption.AutoSelect => "celesteserver.centralteam.cn:17230",
                    ServerSelectOption.MainServer => "main.server.celemiao.com:17231",
                    ServerSelectOption.BackupServer => "back.server.celemiao.com:17231",
                    _ => Server
                };

                if (value && !Connected)
                {
                    if (!CelesteNetClientModule.Instance.MayReconnect && !(CelesteNetClientModule.Instance.AnyContext?.Client?.IsAlive ?? false))
                        CelesteNetClientModule.Instance.AnyContext?.Status?.Set("Reconnect delayed...", 3f, true, false);
                    CelesteNetClientModule.Instance.Start();
                }
                else if (!value && Connected)
                {
                    CelesteNetClientModule.Instance.Stop();
                }

                if (!value && EnabledEntry != null && Engine.Scene != null)
                    Engine.Scene.OnEndOfFrame += () => EnabledEntry?.LeftPressed();
                if (ServerEntry != null)
                    ServerEntry.Disabled = value || !(Engine.Scene is Overworld);
                if (KeyEntry != null)
                    SetKeyEntryDisabled(value || !(Engine.Scene is Overworld) || _loginMode != LoginModeType.Key);
                if (ExtraServersEntry != null)
                    ExtraServersEntry.Disabled = value;
                if (ResetGeneralButton != null)
                    ResetGeneralButton.Disabled = value;
                if (ConnectLocallyButton != null)
                    ConnectLocallyButton.Disabled = value;
            }
        }

        [SettingName("MODOPTIONS_CELESTENETCLIENT_OPATICY_NEAR_SELF")]
        public bool OpacityNearSelf { get; set; } = true;

        [SettingName("MODOPTIONS_CELESTENETCLIENT_AUTO_CONNECT")]
        public bool AutoConnect { get; set; }

        [SettingName("MODOPTIONS_CELESTENETCLIENT_USE_EN_FONT_WHEN_POSSIBLE")]
        public bool UseENFontWhenPossible { get; set; } = false;

        [SettingIgnore, YamlIgnore]
        public TextMenu.OnOff? EnabledEntry { get; protected set; }

        // A button that only shows when the "visible" bool below gets set, to easily allow connecting back to official server
        [SettingIgnore, YamlIgnore]
        public TextMenu.Button? ConnectDefaultButton { get; protected set; }
        [SettingIgnore, YamlIgnore]
        public TextMenuExt.EaseInSubHeaderExt? ConnectDefaultButtonHint { get; protected set; }

        private bool _ConnectDefaultVisible = false;
        [SettingIgnore, YamlIgnore]
        public bool ConnectDefaultVisible
        {
            get => _ConnectDefaultVisible;
            set
            {
                if (ConnectDefaultButton != null)
                    ConnectDefaultButton.Visible = value;

                if (_ConnectDefaultVisible != value && ConnectDefaultButtonHint != null)
                    ConnectDefaultButtonHint.FadeVisible = value;

                _ConnectDefaultVisible = value;
            }
        }

        public bool AutoReconnect { get; set; } = true;
        [SettingIgnore, YamlIgnore]
        public TextMenu.OnOff? AutoReconnectEntry { get; protected set; }

        public bool ReceivePlayerAvatars { get; set; } = true;
        [SettingIgnore, YamlIgnore]
        public TextMenu.OnOff? ReceivePlayerAvatarsEntry { get; protected set; }

        public const string DefaultServer = "celesteserver.centralteam.cn";

        [SettingIgnore, YamlIgnore]
        public string EffectiveServer
        {
            get => ServerOverride.IsNullOrEmpty() ? Server : ServerOverride;
            private set
            {
                Server = value;
            }
        }

#if DEBUG
        [SettingSubHeader("modoptions_celestenetclient_subheading_general")]
#else
        [SettingIgnore]
#endif
        [SettingSubText("modoptions_celestenetclient_devonlyhint")]
        public string Server
        {
            get => _Server;
            set
            {
                if (_Server == value)
                    return;

                _Server = value;

                UpdateServerInDialogs();
            }
        }
        private string _Server = DefaultServer;

        // Any non-empty string will override Server property temporarily. (setting not saved)
        // Currently only used for "connect locally" button (for Nucleus etc.)
        [SettingIgnore, YamlIgnore]
        public string ServerOverride
        {
            get => _ServerOverride;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _ServerOverride = "";
                }
                else
                {
                    _ServerOverride = value;
                }
                UpdateServerInDialogs();
            }
        }

        // best way I can come up with to do this in various places, rather than a lot of erratic logic in Server & ServerOverride setters
        public void UpdateServerInDialogs()
        {
            if (ServerEntry != null)
                ServerEntry.Label = "modoptions_celestenetclient_server".DialogClean().Replace("((server))", EffectiveServer);

            if (EnabledEntry != null)
                EnabledEntry.Label = "modoptions_celestenetclient_connected".DialogClean().Replace("((server))", EffectiveServer);

            if (ConnectDefaultButton != null)
                ConnectDefaultButton.Label = "modoptions_celestenetclient_connectdefault".DialogClean().Replace("((default))", DefaultServer);

            if (ConnectDefaultButtonHint != null)
                ConnectDefaultButtonHint.Title = "modoptions_celestenetclient_connectdefaulthint".DialogClean().Replace("((server))", EffectiveServer);
        }

        private string _ServerOverride = "";

        [SettingIgnore, YamlIgnore]
        public TextMenu.Button? ServerEntry { get; protected set; }

        [SettingIgnore]
        public string[] ExtraServers { get; set; } = new string[0];

        [SettingIgnore, YamlIgnore]
        public TextMenu.Slider? ExtraServersEntry { get; protected set; }

#if !DEBUG
        [SettingSubHeader("modoptions_celestenetclient_subheading_general")]
#endif
        [SettingSubText("modoptions_celestenetclient_loginmodehint")]
        public LoginModeType LoginMode
        {
            get
            {
                return _loginMode;
            }
            set
            {
                _loginMode = value;
                switch (value)
                {
                case LoginModeType.Key:
                    // Enable Key (unless in-game), disable Name input
                    if (KeyEntry != null)
                        SetKeyEntryDisabled(!(Engine.Scene is Overworld) || Connected);
                    break;
                }
            }
        }
        private LoginModeType _loginMode = LoginModeType.Key;

        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                value = value.TrimStart('#');
                KeyError = KeyErrors.None;
                _Key = "#" + value;
            }
        }

        [YamlIgnore]
        private string _Key = "";

        [SettingIgnore, YamlIgnore]
        public TextMenu.Button? KeyEntry { get; protected set; }

        [SettingIgnore, YamlIgnore]
        public string NameKey => Key;

        [SettingIgnore, YamlIgnore]
        public KeyErrors KeyError
        {
            get
            {
                return _KeyError;
            }
            set
            {
                _KeyError = value;
                if (KeyEntry != null)
                    KeyEntry.Label = "modoptions_celestenetclient_key".DialogClean().Replace("((key))", Key.Length > 0 ? KeyDisplayDialog(_KeyError) : "-");
            }
        }
        [YamlIgnore]
        private KeyErrors _KeyError = KeyErrors.None;

        [SettingIgnore]
        public ulong ClientID { get; set; } = 0;

        [SettingIgnore, YamlIgnore]
        public uint InstanceID { get; set; } = 0;

        [SettingIgnore]
        public ClientIDSendMode ClientIDSending { get; set; } = ClientIDSendMode.NotOnLocalhost;

        [SettingIgnore, YamlIgnore]
        public TextMenu.Button? ConnectLocallyButton { get; protected set; }

        [SettingIgnore, YamlIgnore]
        public TextMenu.Button? ResetGeneralButton { get; protected set; }


        #endregion

        #region Debug

        public DebugMenu Debug { get; set; } = new();

#if DEBUG
        [SettingSubMenu]
#endif
        public class DebugMenu
        {
            [SettingSubText("modoptions_celestenetclient_devonlyhint")]
            public ConnectionType ConnectionType { get; set; } = ConnectionType.Auto;

            [SettingSubText("modoptions_celestenetclient_devonlyhint")]
            public LogLevel DevLogLevel
            {
                get => Logger.Level;
                set => Logger.Level = value;
            }

        }

        #endregion

        #region In-Game

        public InGameMenu InGame { get; set; } = new();
        [SettingSubMenu]
        public class InGameMenu
        {

            [SettingSubText("modoptions_celestenetclient_interactionshint")]
            public bool Interactions { get; set; } = true;

            [SettingSubText("modoptions_celestenetclient_entitieshint")]
            public SyncMode Entities { get; set; } = SyncMode.ON;
            [SettingSubHeader("modoptions_celestenetclient_subheading_players")]

            [SettingRange(0, 20)]
            public int OtherPlayerOpacity { get; set; } = 18;

            [SettingSubHeader("modoptions_celestenetclient_subheading_sound")]
            public SyncMode Sounds { get; set; } = SyncMode.ON;

            [SettingRange(1, 10)]
            public int SoundVolume { get; set; } = 8;

            public void CreateOtherPlayerOpacityEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    new TextMenu.Slider("modoptions_celestenetclient_otherplayeropacity".DialogClean(), i => $"{i * 5}%", 0, 20, OtherPlayerOpacity).Change(v => OtherPlayerOpacity = v)
                );
            }

            public void CreateSoundVolumeEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    new TextMenu.Slider("modoptions_celestenetclient_soundvolume".DialogClean(), i => $"{i * 10}%", 0, 10, SoundVolume).Change(v => SoundVolume = v)
                );
            }
        }

        [SettingIgnore, YamlIgnore]
        public float OtherPlayerAlpha => InGame.OtherPlayerOpacity / 20f;

        #endregion


        #region In-Game HUD Elements

        public InGameHUDMenu InGameHUD { get; set; } = new();
        [SettingSubMenu]
        public class InGameHUDMenu
        {

            public bool ShowOwnName { get; set; } = true;

            [SettingRange(0, 20)]
            public int NameOpacity { get; set; } = 20;

            [YamlIgnore]
            private OffScreenModes _OffScreenNames = OffScreenModes.Same;
            public OffScreenModes OffScreenNames
            {
                get
                {
                    return _OffScreenNames;
                }
                set
                {
                    _OffScreenNames = value;
                    if (_OffScreenNameOpacityEntry != null)
                        _OffScreenNameOpacityEntry.Disabled = _OffScreenNames != OffScreenModes.Other;
                }
            }

            [YamlIgnore]
            private TextMenu.Slider? _OffScreenNameOpacityEntry;
            public int OffScreenNameOpacity { get; set; } = 10;

            [SettingRange(0, 20)]
            public int EmoteOpacity { get; set; } = 20;

            [YamlIgnore]
            private OffScreenModes _OffScreenEmotes = OffScreenModes.Same;
            public OffScreenModes OffScreenEmotes
            {
                get
                {
                    return _OffScreenEmotes;
                }
                set
                {
                    _OffScreenEmotes = value;
                    if (_OffScreenEmoteOpacityEntry != null)
                        _OffScreenEmoteOpacityEntry.Disabled = _OffScreenEmotes != OffScreenModes.Other;
                }
            }

            [YamlIgnore]
            private TextMenu.Slider? _OffScreenEmoteOpacityEntry;
            public int OffScreenEmoteOpacity { get; set; } = 10;

            [SettingRange(1, 12)]
            public int ScreenMargins { get; set; } = 4;

            public void CreateEmoteOpacityEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    new TextMenu.Slider("modoptions_celestenetclient_emoteopacity".DialogClean(), i => $"{i * 5}%", 0, 20, EmoteOpacity).Change(v => EmoteOpacity = v)
                );
            }

            public void CreateOffScreenNameOpacityEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    (_OffScreenNameOpacityEntry = new TextMenu.Slider("modoptions_celestenetclient_offscreennameopacity".DialogClean(), i => $"{i * 5}%", 0, 20, OffScreenNameOpacity))
                    .Change(v => OffScreenNameOpacity = v)
                );
                _OffScreenNameOpacityEntry.Disabled = OffScreenNames != OffScreenModes.Other;
            }

            public void CreateOffScreenEmoteOpacityEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    (_OffScreenEmoteOpacityEntry = new TextMenu.Slider("modoptions_celestenetclient_offscreenemoteopacity".DialogClean(), i => $"{i * 5}%", 0, 20, OffScreenEmoteOpacity))
                    .Change(v => OffScreenEmoteOpacity = v)
                );
                _OffScreenEmoteOpacityEntry.Disabled = OffScreenEmotes != OffScreenModes.Other;
            }
        }

        #endregion

        #region Top-level UI

        public const int UISizeMin = 1, UISizeMax = 20, UISizeDefault = 8;
        [SettingIgnore, YamlIgnore]
        public int _UISize { get; private set; } = UISizeDefault;
        [SettingSubHeader("modoptions_celestenetclient_subheading_ui")]
        [SettingSubText("modoptions_celestenetclient_uisizehint")]
        [SettingRange(UISizeMin, UISizeMax)]
        public int UISize
        {
            get => _UISize;
            set
            {
                if (value != _UISize)
                {
                    // update both chat and player UI size properties to the same value
                    UISizeChat = UISizePlayerList = value;
                }
                _UISize = value;
            }
        }

        [SettingIgnore, YamlIgnore]
        public int _UISizeChat { get; private set; }
        [SettingRange(UISizeMin, UISizeMax)]
        public int UISizeChat
        {
            get => _UISizeChat;
            set
            {
                if (UISizeChatSlider != null && value != _UISizeChat)
                {
                    // all this is to make the OUI elements update and "react" properly (and visually)
                    if (value < _UISizeChat)
                    {
                        UISizeChatSlider.LeftPressed();
                    }
                    else
                    {
                        UISizeChatSlider.RightPressed();
                    }

                    UISizeChatSlider.Index = Calc.Clamp(value, UISizeMin, UISizeMax) - 1;
                    UISizeChatSlider.OnValueChange.Invoke(value);
                }
                _UISizeChat = value;
            }
        }

        [SettingIgnore, YamlIgnore]
        public TextMenu.Slider? UISizeChatSlider { get; protected set; }

        [SettingIgnore, YamlIgnore]
        public int _UISizePlayerList { get; private set; }
        [SettingRange(UISizeMin, UISizeMax)]
        public int UISizePlayerList
        {
            get => _UISizePlayerList;
            set
            {
                if (UISizePlayerListSlider != null && value != _UISizePlayerList)
                {
                    // all this is to make the OUI elements update and "react" properly (and visually)
                    if (value < _UISizePlayerList)
                    {
                        UISizePlayerListSlider.LeftPressed();
                    }
                    else
                    {
                        UISizePlayerListSlider.RightPressed();
                    }

                    UISizePlayerListSlider.Index = Calc.Clamp(value, UISizeMin, UISizeMax) - 1;
                    UISizePlayerListSlider.OnValueChange.Invoke(value);
                }
                _UISizePlayerList = value;
            }
        }


        [SettingIgnore, YamlIgnore]
        public TextMenu.Slider? UISizePlayerListSlider { get; protected set; }

        [SettingIgnore]
        public float UIScaleOverride { get; set; } = 0f;
        [SettingIgnore, YamlIgnore]
        public float UIScale => CalcUIScale(UISize);
        [SettingIgnore, YamlIgnore]
        public float UIScaleChat => CalcUIScale(UISizeChat);
        [SettingIgnore, YamlIgnore]
        public float UIScalePlayerList => CalcUIScale(UISizePlayerList);

        #endregion

        #region UI Chat

        public ChatUIMenu ChatUI { get; set; } = new();
        [SettingSubMenu]
        public class ChatUIMenu
        {

            public CelesteNetChatComponent.ChatMode ShowNewMessages { get; set; }

            public int NewMessagesFadeTime { get; set; } = 6;

            [SettingRange(-5, 5)]
            public int NewMessagesSizeAdjust { get; set; } = 0;

            public bool ShowScrollingControls { get; set; } = true;

            public bool ChatCloseCancelsSuggestions { get; set; } = true;

            [SettingIgnore]
            [SettingRange(4, 16)]
            public int ChatLogLength { get; set; } = 8;

            [SettingRange(1, 5)]
            public int ChatScrollSpeed { get; set; } = 2;

            public CelesteNetChatComponent.ChatScrollFade ChatScrollFading { get; set; } = CelesteNetChatComponent.ChatScrollFade.Fast;

            public void CreateNewMessagesFadeTimeEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    new TextMenu.Slider("modoptions_celestenetclient_chatui_newmessagesfadetime".DialogClean(), i => $"{i / 2f:F1} s", 1, 20, NewMessagesFadeTime).Change(v => NewMessagesFadeTime = v)
                );
            }
        }

        #endregion

        #region UI Player List

        public PlayerListUIMenu PlayerListUI { get; set; } = new();
        [SettingSubMenu]
        public class PlayerListUIMenu
        {

            public CelesteNetPlayerListComponent.ListModes PlayerListMode { get; set; } = CelesteNetPlayerListComponent.ListModes.Channels;

            public CelesteNetPlayerListComponent.LocationModes ShowPlayerListLocations { get; set; } = CelesteNetPlayerListComponent.LocationModes.ON;

            public bool PlayerListShowPing { get; set; } = true;

            [SettingSubText("modoptions_celestenetclient_hideownchannelhint")]
            public bool HideOwnChannelName { get; set; } = false;

            [SettingSubText("modoptions_celestenetclient_plscrollmodehint")]
            public CelesteNetPlayerListComponent.ScrollModes PlayerListScrollMode { get; set; } = CelesteNetPlayerListComponent.ScrollModes.HoldTab;

            [SettingSubText("modoptions_celestenetclient_scrolldelayhint")]
            public int ScrollDelay { get; set; } = 1;

            [SettingRange(0, 100, true)]
            public int ScrollDelayLeniency { get; set; } = 50;

            public void CreateScrollDelayEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    new TextMenu.Slider("modoptions_celestenetclient_scrolldelay".DialogClean(), i => $"{i / 2f:F1} s", 0, 5, ScrollDelay).Change(v => ScrollDelay = v)
                );
            }
        }

        #endregion

        #region UI Customize

        public UICustomizeMenu UICustomize { get; set; } = new();
        [SettingSubMenu]
        public class UICustomizeMenu
        {
            public int ChatOpacity { get; set; } = 16;

            public int PlayerListOpacity { get; set; } = 17;

            [SettingIgnore]
            public bool PlayerListShortenRandomizer { get; set; } = true;

            public bool PlayerListAllowSplit { get; set; } = true;

            [SettingRange(50, 90, true)]
            public int DynScaleThreshold { get; set; } = 70;

            [SettingRange(0, 50, true)]
            public int DynScaleRange { get; set; } = 50;

            public void CreateChatOpacityEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    new TextMenu.Slider("modoptions_celestenetclient_ChatOpacity".DialogClean(), i => $"{i * 5}%", 0, 20, ChatOpacity).Change(v => ChatOpacity = v)
                );
            }

            public void CreatePlayerListOpacityEntry(TextMenuExt.SubMenu menu, bool inGame)
            {
                menu.Add(
                    new TextMenu.Slider("modoptions_celestenetclient_PlayerListOpacity".DialogClean(), i => $"{i * 5}%", 0, 20, PlayerListOpacity).Change(v => PlayerListOpacity = v)
                );
            }

            // Everest TODO: SettingRange -> optional stepping, optional string suffixes
            // TODO: Reset button (Everest Submenu Attribute?)
        }

        #endregion

        #region Performance

        // TODO: Add some more performance-focused settings like maybe skipping Blur RTs entirely
        [SettingSubText("modoptions_celestenetclient_uiblurhint")]
        public CelesteNetRenderHelperComponent.BlurQuality UIBlur { get; set; } = CelesteNetRenderHelperComponent.BlurQuality.MEDIUM;

        #endregion

        #region Legacy properties

        // For compatibility with other mods that access these

        // Debug
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ConnectionType is now CelesteNetClientSettings.Debug.ConnectionType")]
        public ConnectionType ConnectionType
        {
            get { return Debug?.ConnectionType ?? ConnectionType.Auto; }
            set { if (Debug != null) Debug.ConnectionType = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.DevLogLevel is now CelesteNetClientSettings.Debug.DevLogLevel")]
        public LogLevel DevLogLevel
        {
            get { return Debug?.DevLogLevel ?? LogLevel.INF; }
            set { if (Debug != null) Debug.DevLogLevel = value; }
        }
        // In-Game
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.Interactions is now CelesteNetClientSettings.InGame.Interactions")]
        public bool Interactions
        {
            get { return InGame?.Interactions ?? true; }
            set { if (InGame != null) InGame.Interactions = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.Sounds is now CelesteNetClientSettings.InGame.Sounds")]
        public SyncMode Sounds
        {
            get { return InGame?.Sounds ?? SyncMode.ON; }
            set { if (InGame != null) InGame.Sounds = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.SoundVolume is now CelesteNetClientSettings.InGame.SoundVolume")]
        public int SoundVolume
        {
            get { return InGame?.SoundVolume ?? 8; }
            set { if (InGame != null) InGame.SoundVolume = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.Entities is now CelesteNetClientSettings.InGame.Entities")]
        public SyncMode Entities
        {
            get { return InGame?.Entities ?? SyncMode.ON; }
            set { if (InGame != null) InGame.Entities = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerOpacity is now CelesteNetClientSettings.InGame.OtherPlayerOpacity")]
        public int PlayerOpacity
        {
            get { return InGame?.OtherPlayerOpacity / 5 ?? 4; }
            set { if (InGame != null) InGame.OtherPlayerOpacity = value * 5; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.NameOpacity is now CelesteNetClientSettings.InGameHUD.NameOpacity")]
        public int NameOpacity
        {
            get { return InGameHUD?.NameOpacity / 5 ?? 4; }
            set { if (InGameHUD != null) InGameHUD.NameOpacity = value * 5; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ShowOwnName is now CelesteNetClientSettings.InGameHUD.ShowOwnName")]
        public bool ShowOwnName
        {
            get { return InGameHUD?.ShowOwnName ?? true; }
            set { if (InGameHUD != null) InGameHUD.ShowOwnName = value; }
        }
        // Chat UI
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ShowNewMessages is now CelesteNetClientSettings.ChatUI.ShowNewMessages")]
        public CelesteNetChatComponent.ChatMode ShowNewMessages
        {
            get { return ChatUI?.ShowNewMessages ?? CelesteNetChatComponent.ChatMode.All; }
            set { if (ChatUI != null) ChatUI.ShowNewMessages = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ChatLogLength is now CelesteNetClientSettings.ChatUI.ChatLogLength")]
        public int ChatLogLength
        {
            get { return ChatUI?.ChatLogLength ?? 8; }
            set { if (ChatUI != null) ChatUI.ChatLogLength = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ChatScrollSpeed is now CelesteNetClientSettings.ChatUI.ChatScrollSpeed")]
        public int ChatScrollSpeed
        {
            get { return ChatUI?.ChatScrollSpeed ?? 2; }
            set { if (ChatUI != null) ChatUI.ChatScrollSpeed = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ChatScrollFading is now CelesteNetClientSettings.ChatUI.ChatScrollFading")]
        public CelesteNetChatComponent.ChatScrollFade ChatScrollFading
        {
            get { return ChatUI?.ChatScrollFading ?? CelesteNetChatComponent.ChatScrollFade.Fast; }
            set { if (ChatUI != null) ChatUI.ChatScrollFading = value; }
        }
        // Player List UI
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListMode is now CelesteNetClientSettings.PlayerListUI.PlayerListMode")]
        public CelesteNetPlayerListComponent.ListModes PlayerListMode
        {
            get { return PlayerListUI?.PlayerListMode ?? CelesteNetPlayerListComponent.ListModes.Channels; }
            set { if (PlayerListUI != null) PlayerListUI.PlayerListMode = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ShowPlayerListLocations is now CelesteNetClientSettings.PlayerListUI.ShowPlayerListLocations")]
        public CelesteNetPlayerListComponent.LocationModes ShowPlayerListLocations
        {
            get { return PlayerListUI?.ShowPlayerListLocations ?? CelesteNetPlayerListComponent.LocationModes.ON; }
            set { if (PlayerListUI != null) PlayerListUI.ShowPlayerListLocations = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListShortenRandomizer is now CelesteNetClientSettings.UICustomize.PlayerListShortenRandomizer")]
        public bool PlayerListShortenRandomizer
        {
            get { return UICustomize?.PlayerListShortenRandomizer ?? true; }
            set { if (UICustomize != null) UICustomize.PlayerListShortenRandomizer = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListAllowSplit is now CelesteNetClientSettings.UICustomize.PlayerListAllowSplit")]
        public bool PlayerListAllowSplit
        {
            get { return UICustomize?.PlayerListAllowSplit ?? true; }
            set { if (UICustomize != null) UICustomize.PlayerListAllowSplit = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListShowPing is now CelesteNetClientSettings.PlayerListUI.PlayerListShowPing")]
        public bool PlayerListShowPing
        {
            get { return PlayerListUI?.PlayerListShowPing ?? true; }
            set { if (PlayerListUI != null) PlayerListUI.PlayerListShowPing = value; }
        }

        #endregion

        [SettingSubHeader("modoptions_celestenetclient_subheading_other")]
        public bool EmoteWheel { get; set; } = true;

        #region Key Bindings

        [DefaultButtonBinding(Buttons.Back, Keys.Tab)]
        public ButtonBinding? ButtonPlayerList { get; set; }

        [DefaultButtonBinding(0, Keys.T)]
        public ButtonBinding? ButtonChat { get; set; }

        [SettingSubHeader("modoptions_celestenetclient_binds_playerlist")]
        [DefaultButtonBinding(Buttons.LeftThumbstickUp, Keys.Up)]
        public ButtonBinding? ButtonPlayerListScrollUp { get; set; }

        [DefaultButtonBinding(Buttons.LeftThumbstickDown, Keys.Down)]
        public ButtonBinding? ButtonPlayerListScrollDown { get; set; }

        [SettingSubHeader("modoptions_celestenetclient_binds_chat")]
        [DefaultButtonBinding(0, Keys.Enter)]
        public ButtonBinding? ButtonChatSend { get; set; }

        [DefaultButtonBinding(0, Keys.Escape)]
        public ButtonBinding? ButtonChatClose { get; set; }

        [DefaultButtonBinding(Buttons.LeftThumbstickUp, Keys.PageUp)]
        public ButtonBinding? ButtonChatScrollUp { get; set; }

        [DefaultButtonBinding(Buttons.LeftThumbstickDown, Keys.PageDown)]
        public ButtonBinding? ButtonChatScrollDown { get; set; }

        //[DefaultButtonBinding(0, Keys.Back)]
        //public ButtonBinding? ButtonChatBackspace { get; set; }

        //[DefaultButtonBinding(0, Keys.Delete)]
        //public ButtonBinding? ButtonChatDelete { get; set; }

        // TODO: Customize ways to open the emote wheel, which stick or maybe even on KB?
        //[DefaultButtonBinding(0, 0)]
        //public ButtonBinding? ButtonEmoteWheel { get; set; }

        [SettingSubHeader("modoptions_celestenetclient_binds_emote")]
        [DefaultButtonBinding(Buttons.RightStick, Keys.Q)]
        public ButtonBinding? ButtonEmoteWheelSend { get; set; }

        [DefaultButtonBinding(0, Keys.D1)]
        public ButtonBinding? ButtonEmote1 { get; set; }

        [DefaultButtonBinding(0, Keys.D2)]
        public ButtonBinding? ButtonEmote2 { get; set; }

        [DefaultButtonBinding(0, Keys.D3)]
        public ButtonBinding? ButtonEmote3 { get; set; }

        [DefaultButtonBinding(0, Keys.D4)]
        public ButtonBinding? ButtonEmote4 { get; set; }

        [DefaultButtonBinding(0, Keys.D5)]
        public ButtonBinding? ButtonEmote5 { get; set; }

        [DefaultButtonBinding(0, Keys.D6)]
        public ButtonBinding? ButtonEmote6 { get; set; }

        [DefaultButtonBinding(0, Keys.D7)]
        public ButtonBinding? ButtonEmote7 { get; set; }

        [DefaultButtonBinding(0, Keys.D8)]
        public ButtonBinding? ButtonEmote8 { get; set; }

        [DefaultButtonBinding(0, Keys.D9)]
        public ButtonBinding? ButtonEmote9 { get; set; }

        [DefaultButtonBinding(0, Keys.D0)]
        public ButtonBinding? ButtonEmote10 { get; set; }

        #endregion

        [SettingIgnore]
        public string[] Emotes { get; set; } = new string[0];

        #region Helpers

        private float CalcUIScale(int uisize)
        {
            if (UIScaleOverride > 0f)
                return UIScaleOverride;
            return ((uisize - 1f) / (UISizeMax - 1f));
        }

        [SettingIgnore, YamlIgnore]
        public string Host
        {
            get
            {
                string server = EffectiveServer.ToLowerInvariant();
                int indexOfPort;
                if (!string.IsNullOrEmpty(server) &&
                    (indexOfPort = server.LastIndexOf(':')) != -1 &&
                    int.TryParse(server.Substring(indexOfPort + 1), out _))
                    return server.Substring(0, indexOfPort);

                return server;
            }
        }
        [SettingIgnore, YamlIgnore]
        public int Port
        {
            get
            {
                string server = EffectiveServer;
                int indexOfPort;
                if (!string.IsNullOrEmpty(server) &&
                    (indexOfPort = server.LastIndexOf(':')) != -1 &&
                    int.TryParse(server.Substring(indexOfPort + 1), out int port))
                    return port;

                return 17230;
            }
        }

        #endregion

        #region Custom Entry Creators

        public TextMenu.Slider CreateMenuSlider(TextMenu menu, string dialogLabel, int min, int max, int val, Func<int, string> values, Action<int> onChange)
        {
            TextMenu.Slider item = new TextMenu.Slider($"modoptions_celestenetclient_{dialogLabel}".DialogClean(), values, min, max, val);
            item.Change(onChange);
            menu.Add(item);
            return item;
        }

        public TextMenu.Button CreateMenuButton(TextMenu menu, string dialogLabel, Func<string, string>? dialogTransform, Action onPress)
        {
            string label = $"modoptions_celestenetclient_{dialogLabel}".DialogClean();
            TextMenu.Button item = new TextMenu.Button(dialogTransform?.Invoke(label) ?? label);
            item.Pressed(onPress);
            menu.Add(item);
            return item;
        }
        public void CreateLoginButtonEntry(TextMenu menu, bool inGame)
        {
            LoginButton = CreateMenuButton(menu, "Login", null, () =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "https://bbs.celemiao.com/oauth/authorize?client_id=FSygRsIuDy0edjcJzYuw2PpJL1TwkWa&response_type=code&redirect_uri=http://localhost:38038/auth&scope=celeste.read",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception e)
                {
                    Logger.LogDetailedException(e, "CelesteLogin");
                    var ctx = CelesteNetClientModule.Instance.Context;
                    if (ctx is null)
                        CelesteNetClientModule.Instance.Context = ctx = new(Celeste.Instance, null);
                    ctx.Status?.Set("Please Go to celeste.centralteam.cn Press [CelesteLogin].", 5f);
                    Logger.Log(LogLevel.INF, "celestemodcore", "Open browser failed.");
                }
            });
            LoginButton.Disabled = Connected;
        }

        public TextMenu.Button CreateMenuStringInput(TextMenu menu, string dialogLabel, Func<string, string> dialogTransform, int maxValueLength, Func<string> currentValue, Action<string> newValue)
        {
            string label = $"modoptions_celestenetclient_{dialogLabel}".DialogClean();
            TextMenu.Button item = new TextMenu.Button(dialogTransform?.Invoke(label) ?? label);
            item.Pressed(() =>
            {
                Audio.Play("event:/ui/main/savefile_rename_start");
                menu.SceneAs<Overworld>().Goto<OuiModOptionString>().Init<OuiModOptions>(
                    currentValue.Invoke(),
                    v => newValue.Invoke(v),
                    maxValueLength
                );
            });
            menu.Add(item);
            return item;
        }

        public void CreateConnectedEntry(TextMenu menu, bool inGame)
        {
            menu.Add(
                (EnabledEntry = new TextMenu.OnOff("modoptions_celestenetclient_connected".DialogClean().Replace("((server))", EffectiveServer), Connected))
                .Change(v => Connected = v)
            );
            EnabledEntry.AddDescription(menu, "modoptions_celestenetclient_connectedhint".DialogClean());
        }

        public void CreateServerEntry(TextMenu menu, bool inGame)
        {
#if DEBUG
            ServerEntry = CreateMenuStringInput(menu, "SERVER", s => s.Replace("((server))", EffectiveServer), 30, () => EffectiveServer, newVal => EffectiveServer = newVal);
            ServerEntry.Disabled = inGame || Connected;
            ServerEntry.AddDescription(menu, "modoptions_celestenetclient_devonlyhint".DialogClean());
#endif
        }

        public void CreateRefreshTokenEntry(TextMenu menu, bool inGame)
        {

        }
        public void CreateExpiredTimeEntry(TextMenu menu, bool inGame)
        {

        }
        public void CreateKeyEntry(TextMenu menu, bool inGame)
        {
            KeyError = KeyErrors.None;
            KeyEntry = CreateMenuStringInput(menu, "KEY", s => s.Replace("((key))", Key.Length > 0 ? KeyDisplayDialog(KeyError) : "-"), 17, () => Key, newVal => Key = newVal);
            KeyEntry.AddDescription(menu, "modoptions_celestenetclient_keyhint".DialogClean());
            SetKeyEntryDisabled(inGame || Connected || _loginMode != LoginModeType.Key);
        }

        public void SetKeyEntryDisabled(bool value = true)
        {
            if (KeyEntry == null)
                return;
            KeyEntry.Disabled = value;
            KeyEntry.Label = "modoptions_celestenetclient_key".DialogClean().Replace("((key))", Key.Length > 0 ? KeyDisplayDialog(_KeyError) : "-");
        }

        public string KeyDisplayDialog(KeyErrors val)
        {
            if (_loginMode != LoginModeType.Key)
                return "modoptions_celestenetclient_keydisplay_none".DialogClean();

            if (!Connected)
            {
                // Don't show the error if we managed to connect, I guess :clueless:
                switch (val)
                {
                case KeyErrors.InvalidChars:
                    return "modoptions_celestenetclient_keyerror_invalidchars".DialogClean();
                case KeyErrors.InvalidLength:
                    return "modoptions_celestenetclient_keyerror_invalidlength".DialogClean();
                case KeyErrors.InvalidKey:
                    return "modoptions_celestenetclient_keyerror_invalidkey".DialogClean();
                }
            }

            return "modoptions_celestenetclient_keydisplay_hide".DialogClean();
        }

        public void CreateEmotesEntry(TextMenu menu, bool inGame)
        {
            TextMenu.Button item = CreateMenuButton(menu, "RELOAD", null, () =>
            {
                CelesteNetClientSettings settingsOld = CelesteNetClientModule.Settings;
                CelesteNetClientModule.Instance.LoadSettings();
                CelesteNetClientSettings settingsNew = CelesteNetClientModule.Settings;
                CelesteNetClientModule.Instance._Settings = settingsOld;

                settingsOld.Emotes = settingsNew.Emotes;
            });
            item.AddDescription(menu, "modoptions_celestenetclient_reloadhint".DialogClean());
        }

        public void CreateExtraServersEntry(TextMenu menu, bool inGame)
        {
            int selected = 0;
            for (int i = 0; i < ExtraServers.Length; i++)
                if (ExtraServers[i] == Server)
                    selected = i + 1;

            ExtraServersEntry = CreateMenuSlider(
                menu, "EXTRASERVERS_SLIDER",
                0, ExtraServers.Length, selected,
                i => i == 0 ? DefaultServer : ExtraServers[i - 1],
                i =>
                {
                    if (!Connected && i <= ExtraServers.Length)
                        Server = i == 0 ? DefaultServer : ExtraServers[i - 1];
                }
            );

            ExtraServersEntry.Visible = ExtraServers.Length > 0;
            ExtraServersEntry.Disabled = Connected;

            TextMenu.Button item = CreateMenuButton(menu, "EXTRASERVERS_RELOAD", null, () =>
            {
                CelesteNetClientSettings settingsOld = CelesteNetClientModule.Settings;
                CelesteNetClientModule.Instance.LoadSettings();
                CelesteNetClientSettings settingsNew = CelesteNetClientModule.Settings;
                CelesteNetClientModule.Instance._Settings = settingsOld;

                settingsOld.ExtraServers = settingsNew.ExtraServers;

                int old_idx = ExtraServersEntry.Index;
                ExtraServersEntry.Index = 0;
                ExtraServersEntry.Values.Clear();

                for (int i = 0; i <= ExtraServers.Length; i++)
                    ExtraServersEntry.Add(i == 0 ? DefaultServer : ExtraServers[i - 1], i, i == old_idx);

                ExtraServersEntry.Visible = ExtraServers.Length > 0;
            });
            item.AddDescription(menu, "modoptions_celestenetclient_reloadhint".DialogClean());
#if !DEBUG
            item.Visible = ExtraServers.Length > 0;
#endif
        }

        public void CreateUISizeChatEntry(TextMenu menu, bool inGame)
        {
            if (UISizeChat < UISizeMin || UISizeChat > UISizeMax)
                UISizeChat = UISize;
            UISizeChatSlider = CreateMenuSlider(menu, "UISIZECHAT", UISizeMin, UISizeMax, UISizeChat, i => i.ToString(), v => _UISizeChat = v);
        }

        public void CreateUISizePlayerListEntry(TextMenu menu, bool inGame)
        {
            if (UISizePlayerList < UISizeMin || UISizePlayerList > UISizeMax)
                UISizePlayerList = UISize;
            UISizePlayerListSlider = CreateMenuSlider(menu, "UISIZEPLAYERLIST", UISizeMin, UISizeMax, UISizePlayerList, i => i.ToString(), v => _UISizePlayerList = v);
        }

        public void CreateResetGeneralButtonEntry(TextMenu menu, bool inGame)
        {
            ResetGeneralButton = CreateMenuButton(menu, "RESETGENERAL", null, () =>
            {
                SettingsVersionDoNotEdit = SettingsVersionCurrent;
                Server = DefaultServer;
                // do this which is hopefully visually correct on the OnOff items...
                AutoReconnectEntry?.RightPressed();
                ReceivePlayerAvatarsEntry?.RightPressed();
                // ... but also making sure these are in fact set to these values
                AutoReconnect = true;
                ReceivePlayerAvatars = true;
                ClientID = GenerateClientID();
                ServerOverride = "";
                KeyError = KeyErrors.None;
                ConnectDefaultVisible = false;
            });
            ResetGeneralButton.AddDescription(menu, "modoptions_celestenetclient_resetgeneralhint".DialogClean());
            ResetGeneralButton.Disabled = Connected;
        }

        public void CreateConnectLocallyButtonEntry(TextMenu menu, bool inGame)
        {
            ConnectLocallyButton = CreateMenuButton(menu, "CONNECTLOCALLY", null, () =>
            {
                ServerOverride = "localhost";
                Connected = true;
            });
            ConnectLocallyButton.AddDescription(menu, "modoptions_celestenetclient_connectlocallyhint".DialogClean());
            ConnectLocallyButton.Disabled = Connected;
        }

        public void CreateAutoReconnectEntry(TextMenu menu, bool inGame)
        {
            menu.Add(
                (AutoReconnectEntry = new TextMenu.OnOff("modoptions_celestenetclient_autoreconnect".DialogClean(), AutoReconnect))
                .Change(v => AutoReconnect = v)
            );
            AutoReconnectEntry.AddDescription(menu, "modoptions_celestenetclient_autoreconnecthint".DialogClean());
        }

        public void CreateReceivePlayerAvatarsEntry(TextMenu menu, bool inGame)
        {
            menu.Add(
                (ReceivePlayerAvatarsEntry = new TextMenu.OnOff("modoptions_celestenetclient_avatars".DialogClean(), ReceivePlayerAvatars))
                .Change(v => ReceivePlayerAvatars = v)
            );
            ReceivePlayerAvatarsEntry.AddDescription(menu, "modoptions_celestenetclient_avatarshint".DialogClean());
        }

        public void CreateConnectDefaultButtonEntry(TextMenu menu, bool inGame)
        {
            ConnectDefaultButton = CreateMenuButton(menu, "CONNECTDEFAULT", (label) => label.Replace("((default))", DefaultServer), () =>
            {
                ServerOverride = "";
                Server = DefaultServer;
                CelesteNetClientModule.Instance.ResetReconnectPenalty();
                Connected = true;
            });

            ConnectDefaultButtonHint = null;
            ConnectDefaultButton.AddDescription(menu, "modoptions_celestenetclient_connectdefaulthint".DialogClean().Replace("((server))", EffectiveServer));

            int descriptionIndex = menu.Items.IndexOf(ConnectDefaultButton) + 1;
            if (descriptionIndex > 0 && descriptionIndex < menu.Items.Count && menu.Items[descriptionIndex] is TextMenuExt.EaseInSubHeaderExt desc)
                ConnectDefaultButtonHint = desc;

            ConnectDefaultButton.Visible = ConnectDefaultVisible;
        }

        #endregion

        public static ulong GenerateClientID()
        {
            return ulong.Parse(Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16), System.Globalization.NumberStyles.HexNumber);
        }

        [Flags]
        public enum SyncMode
        {
            OFF = 0b00,
            Send = 0b01,
            Receive = 0b10,
            ON = 0b11
        }

        public enum LoginModeType
        {
            Key = 1
        }
        public enum OffScreenModes
        {
            Same = 0,
            Hidden = 1,
            Other = 2
        }

        public enum KeyErrors
        {
            None = 0,
            InvalidLength,
            InvalidChars,
            InvalidKey
        }

        public enum ClientIDSendMode
        {
            Off = 0,
            NotOnLocalhost = 1,
            On = 2
        }
    }

    // Settings type with relevant properties from CelesteNetClientSettings.SettingsVersion < 2 (before Settings Version number existed)
    // will be loaded in CelesteNetClientModule.LoadOldSettings() to get old values and potentially adjust them to new format
    public class CelesteNetClientSettingsBeforeVersion2 : EverestModuleSettings
    {
        // technically at this time, some of these should load just fine even with the new class above, as they're unchanged there
        // but I figured it's a bit cleaner not to make any such assumptions and load them here in the old deserialization
        public bool AutoReconnect { get; set; } = true;

        public bool ReceivePlayerAvatars { get; set; } = true;

        public bool AutoConnect { get; set; } = true;

        public bool UseENFontWhenPossible { get; set; } = false;

        public string Server { get; set; } = "celesteserver.centralteam.cn";

        public ConnectionType ConnectionType { get; set; } = ConnectionType.Auto;

        public LogLevel DevLogLevel { get; set; }

        public bool Interactions { get; set; } = true;
        public CelesteNetClientSettings.SyncMode Sounds { get; set; } = CelesteNetClientSettings.SyncMode.ON;
        [SettingRange(1, 10)]
        public int SoundVolume { get; set; } = 8;
        public CelesteNetClientSettings.SyncMode Entities { get; set; } = CelesteNetClientSettings.SyncMode.ON;

        public CelesteNetPlayerListComponent.ListModes PlayerListMode { get; set; } = CelesteNetPlayerListComponent.ListModes.Channels;
        public CelesteNetPlayerListComponent.LocationModes ShowPlayerListLocations { get; set; } = CelesteNetPlayerListComponent.LocationModes.ON;

        public bool PlayerListShortenRandomizer { get; set; } = true;

        public bool PlayerListAllowSplit { get; set; } = true;
        public bool PlayerListShowPing { get; set; } = true;

        public CelesteNetChatComponent.ChatMode ShowNewMessages { get; set; }

        [SettingRange(0, 4)]
        public int PlayerOpacity { get; set; } = 4;

        [SettingRange(0, 4)]
        public int NameOpacity { get; set; } = 4;

        public bool ShowOwnName { get; set; } = true;

        [SettingRange(4, 16)]
        public int ChatLogLength { get; set; } = 8;

        [SettingRange(1, 5)]
        public int ChatScrollSpeed { get; set; } = 2;
        public CelesteNetChatComponent.ChatScrollFade ChatScrollFading { get; set; } = CelesteNetChatComponent.ChatScrollFade.Fast;

        public CelesteNetRenderHelperComponent.BlurQuality UIBlur { get; set; } = CelesteNetRenderHelperComponent.BlurQuality.MEDIUM;

        public bool EmoteWheel { get; set; } = true;

        [DefaultButtonBinding(Buttons.Back, Keys.Tab)]
        public ButtonBinding? ButtonPlayerList { get; set; }

        [DefaultButtonBinding(0, Keys.T)]
        public ButtonBinding? ButtonChat { get; set; }

        public string[] Emotes { get; set; } = new string[0];

        public string refreshToken { get; set; }

    }
}
