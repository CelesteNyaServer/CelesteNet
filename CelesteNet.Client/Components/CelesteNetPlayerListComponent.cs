﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Monocle;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetPlayerListComponent : CelesteNetGameComponent {

        public static event Action<BlobPlayer, DataPlayerState>? OnGetState;

        public float Scale => Settings.UIScalePlayerList;
        private float LastScale;

        public readonly Color ColorCountHeader = Calc.HexToColor("FFFF77");
        public readonly Color ColorChannelHeader = Calc.HexToColor("DDDD88");
        public readonly Color ColorChannelHeaderOwn = Calc.HexToColor("FFFF77");
        public readonly Color ColorChannelHeaderPrivate = Calc.HexToColor("DDDD88") * 0.6f;
        public static readonly Color DefaultLevelColor = Color.LightGray;

        private static readonly char[] RandomizerEndTrimChars = "_0123456789".ToCharArray();

        public bool Active;
        public bool PropActive
        {
            get => Active;
            set
            {
                if (Client == null || !Client.IsReady) {
                    Active = false;
                    return;
                }
                if (Active == value)
                    return;
                ScrolledDistance = 0f;
                hasScrolled = false;
                ButtonHoldTime = 0f;

                Active = value;
            }
        }

        private bool ButtonInitialHold;
        private float ButtonHoldTime = 0f;

        public bool ShouldRebuild = false;

        private List<Blob> List = new();

        private Vector2 SizeAll;
        private Vector2 SizeUpper;
        private Vector2 SizeColumn;

        /*
         SizeAll - outer dimensions
        +------------------------------------+
        |                                    |
        |            own channel             |
        |                                    |
        |             SizeUpper              |
        |                                    |
        +-----------------+------------------+
        |                 |                  |
        |                 |                  |
        |   SizeColumn    |  (calculated     |
        |                 |   from other     |
        |                 |   three sizes)   |
        |                 |                  |
        |(extends all ->  |                  |
        | the way right   |                  |
        | if single-col)  |                  |
        +-----------------+------------------+
        When not in Channel Mode, only SizeAll gets used.
         */

        public DataChannelList? Channels;

        // UI Constants

        public static readonly float Margin = 25f;

        public static readonly float PaddingX = 25f;
        public static readonly float PaddingY = 15f;

        public static readonly float SplitGap = 10f;
        public static readonly float BlobSpacing = 10f;

        public static readonly float ChatOffset = 5f;

        public float TextScaleSizeThreshold => Settings.UICustomize.DynScaleThreshold / 100f;

        public int TextScaleRetryMax => Settings.UICustomize.DynScaleRange/10;

        // bar is in the top right of the player list, this is a percentage of the width it takes up / Height is absolute
        public static readonly float HoldScrollDelayBarWidth = 0.4f;
        public static readonly float HoldScrollDelayBarHeight = 6f;

        // Refers to the main timer, where the IL/File time is located.
        public static readonly float MainTimerOffset = 104f;

        // Refers to the sub timer, where the IL time is located when the
        // Speedrun Clock is set to File.
        public static readonly float SubTimerOffset = 24f;

        public float CustomAlpha => Settings.UICustomize.PlayerListOpacity / 20f;

        public ListModes ListMode => Settings.PlayerListUI.PlayerListMode;
        private ListModes LastListMode;

        public LocationModes LocationMode => Settings.PlayerListUI.ShowPlayerListLocations;
        private LocationModes LastLocationMode;

        public bool ShowPing => Settings.PlayerListUI.PlayerListShowPing;
        private bool LastShowPing;

        public bool AllowSplit => Settings.UICustomize.PlayerListAllowSplit;
        private bool LastAllowSplit;

        public bool HideOwnChannelName => Settings.PlayerListUI.HideOwnChannelName;
        private bool LastHideOwnChannelName;

        private static float? spaceWidth;
        protected static float SpaceWidth {
            get {
                if (MDraw.DefaultFont == null) return 0.0f;
                return spaceWidth ??= CelesteNetClientFont.Measure(" ").X;
            }
        }

        public const string IdleIconCode = ":celestenet_idle:";
        private static float? idleIconWidth;
        protected static float IdleIconWidth {
            get {
                if (MDraw.DefaultFont == null)
                    return 0.0f;
                return idleIconWidth ??= CelesteNetClientFont.Measure(IdleIconCode).X + SpaceWidth;
            }
        }

        private int SplittablePlayerCount = 0;

        private readonly int SplitThresholdLower = 10;
        private readonly int SplitThresholdUpper = 12;

        private bool _splitViewPartially = false;
        private bool SplitViewPartially {
            get {
                if (ListMode != ListModes.Channels || !AllowSplit)
                    return _splitViewPartially = false;
                // only flip value after passing threshold to prevent flipping on +1/-1s at threshold
                if (!_splitViewPartially && SplittablePlayerCount > SplitThresholdUpper)
                    return _splitViewPartially = true;
                if (_splitViewPartially && SplittablePlayerCount < SplitThresholdLower)
                    return _splitViewPartially = false;

                return _splitViewPartially;
            }
        }

        private bool SplitSuccessfully = false;

        private int SplitStartsAt;

        protected float ScrolledDistance = 0f;

        private int InputScrollState = 0;
        public int InputScrollUpState => Settings.ButtonPlayerListScrollUp.Check() ? 1 : 0;
        public int InputScrollDownState => Settings.ButtonPlayerListScrollDown.Check() ? 1 : 0;

        private bool hasScrolled;

        // adding a slight minimum delay even to (Settings.PlayerListUI.PlayerListScrollDelay == 0) so that you don't
        // IMMEDIATELY scroll the player count off the top of the list
        private float ActualScrollDelay => Math.Max(Settings.PlayerListUI.ScrollDelay / 2f, .1f);
        private float ScrollDelayLeniency => 1f - Settings.PlayerListUI.ScrollDelayLeniency / 100f;

        public enum ListModes {
            Channels,
            Classic,
        }

        public enum ScrollModes {
            HoldTab = 0,
            Keybinds = 1,
            KeybindsOnHold = 2
        }

        [Flags]
        public enum LocationModes {
            OFF     = 0b00,
            Icons   = 0b01,
            Text    = 0b10,
            ON      = 0b11,
        }

        public CelesteNetPlayerListComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10001;
        }

        public void RebuildList() {
            if (MDraw.DefaultFont == null || Client?.PlayerInfo == null || Client?.Data == null || Channels == null)
                return;

            BlobPlayer.PingPadWidth = 0;

            DataPlayerInfo[] all = Client.Data.GetRefs<DataPlayerInfo>();

            List<Blob> list = new() {
                new() {
                    Name = $"{all.Length} player{(all.Length == 1 ? "" : "s")}",
                    Color = ColorCountHeader,
                    ScaleFactor = 0.25f
                }
            };

            switch (ListMode) {
                case ListModes.Classic:
                    RebuildListClassic(ref list, ref all);
                    break;

                case ListModes.Channels:
                    RebuildListChannels(ref list, ref all);
                    break;
            }

            List = list;
        }

        public void RebuildListClassic(ref List<Blob> list, ref DataPlayerInfo[] all) {
            DataChannelList.Channel? own = null;
            if (Client?.PlayerInfo != null)
                own = Channels?.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));

            foreach (DataPlayerInfo player in all.OrderBy(GetOrderKey)) {
                if (string.IsNullOrWhiteSpace(player.DisplayName))
                    continue;

                BlobPlayer blob = new() {
                    Player = player,
                    Name = player.DisplayName,
                    Color = player.NameColor,
                    ScaleFactor = 0.75f,
                };

                DataChannelList.Channel? channel = Channels?.List.FirstOrDefault(c => c.Players.Contains(player.ID));
                if (!string.IsNullOrEmpty(channel?.Name) && !(HideOwnChannelName && channel == own))
                    blob.Name += $" #{channel.Name}";

                if (Client?.Data != null) {
                    if (Client.Data.TryGetBoundRef(player, out DataPlayerState? state) && state != null) {
                        if (LocationMode != LocationModes.OFF)
                            GetState(blob, state);

                        blob.Idle = state.Idle;
                    }

                    if (ShowPing && Client.Data.TryGetBoundRef(player, out DataConnectionInfo? conInfo) && conInfo != null)
                        blob.PingMs = conInfo.UDPPingMs ?? conInfo.TCPPingMs;
                }

                list.Add(blob);
            }

            PrepareRenderLayout(out float scale, out _, out _);

            foreach (Blob blob in list)
                blob.Generate();

            int textScaleTry = 0;
            float textScale = scale;
            RetryLineScale:

            Vector2 sizeAll = Vector2.Zero;

            foreach (Blob blob in list) {
                blob.DynScale = Calc.LerpClamp(scale, textScale, blob.ScaleFactor);
                blob.Dyn.Y = sizeAll.Y;

                Vector2 size = blob.Measure();
                sizeAll.X = Math.Max(sizeAll.X, size.X);
                sizeAll.Y += size.Y + BlobSpacing * scale;

                if ((
                    (sizeAll.X + 2 * (Margin + PaddingX) * scale) >  UI_WIDTH * TextScaleSizeThreshold ||
                    (sizeAll.Y + 2 * (Margin + PaddingY) * scale) > UI_HEIGHT * TextScaleSizeThreshold
                    ) && textScaleTry < TextScaleRetryMax) {
                    textScaleTry++;
                    textScale -= scale * 0.1f;
                    goto RetryLineScale;
                }
            }

            // remove the stray spacing once we reach the end
            sizeAll.Y -= BlobSpacing * scale;

            SizeAll = sizeAll;
            SizeUpper = sizeAll;
            SizeColumn = Vector2.Zero;
        }

        public void RebuildListChannels(ref List<Blob> list, ref DataPlayerInfo[] all) {
            // this value gets updated at every blob that we could split at
            // i.e. every channel header besides our own.
            int lastPossibleSplit = 0;

            HashSet<DataPlayerInfo> listed = new();
            DataChannelList.Channel? own = null;
            if (Client?.PlayerInfo != null)
                own = Channels?.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));

            void AddChannel(ref List<Blob> list, DataChannelList.Channel channel, Color color, float scaleFactorHeader, float scaleFactor, LocationModes locationMode) {
                bool hideChannel = channel == own && channel.Name != "main" && HideOwnChannelName;
                list.Add(new() {
                    Name = hideChannel ? "<hidden>" : channel.Name,
                    Color = hideChannel ? ColorChannelHeaderPrivate : color,
                    ScaleFactor = scaleFactorHeader,
                    CanSplit = channel != own
                });
                if (channel != own)
                    lastPossibleSplit = list.Count - 1;

               
                foreach (DataPlayerInfo? player in channel.Players.Select(GetPlayerInfo).OrderBy(GetOrderKey)) {
                    BlobPlayer blob = new() { ScaleFactor = scaleFactor, Color = player?.NameColor ?? Color.White };
                    listed.Add(ListPlayerUnderChannel(blob, player, locationMode, channel == own));
                    list.Add(blob);
                }
            }

            SplittablePlayerCount = all.Length;

            if (own != null) {
                SplittablePlayerCount -= own.Players.Length;
                AddChannel(ref list, own, ColorChannelHeaderOwn, 0.25f, 0.5f, LocationMode);
            }
            // this is the index of the first element relevant for splitting,
            // i.e. first blob after our own channel is fully listed.
            int splitStartsAt = list.Count - 1;

            if (Channels != null)
                foreach (DataChannelList.Channel channel in Channels.List)
                    if (channel != own)
                        AddChannel(ref list, channel, ColorChannelHeader, 0.75f, 1f, LocationModes.OFF);

            bool wrotePrivate = false;
            foreach (DataPlayerInfo player in all.OrderBy(GetOrderKey)) {
                if (listed.Contains(player) || string.IsNullOrWhiteSpace(player.DisplayName))
                    continue;

                if (!wrotePrivate) {
                    wrotePrivate = true;
                    list.Add(new() {
                        Name = "!<private>",
                        Color = ColorChannelHeaderPrivate,
                        ScaleFactor = 0.75f,
                        CanSplit = true
                    });
                    lastPossibleSplit = list.Count - 1;
                }

                list.Add(new() {
                    Name = player.DisplayName,
                    Color = player.NameColor,
                    ScaleFactor = 1f
                });
            }

            // if nothing was actually added after recording splitStartsAt, reset it to 0 (nothing to split up)
            splitStartsAt = list.Count > splitStartsAt + 1 ? splitStartsAt + 1 : 0;

            PrepareRenderLayout(out float scale, out _, out _);

            foreach (Blob blob in list)
                blob.Generate();

            int textScaleTry = 0;
            float textScale = scale;
            RetryLineScale:

            Vector2 sizeAll = Vector2.Zero;
            Vector2 sizeToSplit = Vector2.Zero;
            Vector2 sizeUpper = Vector2.Zero;

            bool trimmedExcessSpacing = false;

            for (int i = 0; i < list.Count; i++) {
                Blob blob = list[i];
                blob.DynScale = Calc.LerpClamp(scale, textScale, blob.ScaleFactor);

                // introducing gap after own channel
                if (splitStartsAt > 0 && i == splitStartsAt) {
                    // remove the blob spacing, that's all the blobs for sizeUpper
                    sizeAll.Y -= BlobSpacing * scale;
                    trimmedExcessSpacing = true;
                    sizeUpper = sizeAll;
                    sizeAll.Y += (2 * PaddingY + SplitGap) * scale;
                }
                blob.Dyn.Y = sizeAll.Y;

                Vector2 size = blob.Measure();

                // proceed as we usually did if not splitting or before split starts
                if (!SplitViewPartially || splitStartsAt == 0 || i < splitStartsAt) {
                    sizeAll.X = Math.Max(sizeAll.X, size.X);
                    sizeAll.Y += size.Y + BlobSpacing * scale;
                } else {
                    // ... otherwise we record the sizes seperately
                    sizeToSplit.X = Math.Max(sizeToSplit.X, size.X);
                    sizeToSplit.Y += size.Y + BlobSpacing * scale;
                }

                if ((
                    (Math.Max(sizeAll.X, sizeToSplit.X) + 2 * (Margin + PaddingX) * scale) > UI_WIDTH * TextScaleSizeThreshold ||
                    (sizeAll.Y + sizeToSplit.Y / 2f + 2 * (Margin + PaddingY) * scale) > UI_HEIGHT * TextScaleSizeThreshold
                    ) && textScaleTry < TextScaleRetryMax) {
                    textScaleTry++;
                    textScale -= scale * 0.1f;
                    goto RetryLineScale;
                }
            }

            if (!trimmedExcessSpacing)
                sizeAll.Y -= BlobSpacing * scale;

            if (splitStartsAt == 0)
                sizeUpper = sizeAll;

            int forceSplitAt = -1;
            RetryPartialSplit:
            bool splitSuccessfully = false;
            Vector2 sizeColumn = Vector2.Zero;
            float maxColumnY = 0f;
            
            int switchedSidesAt = 0;
            if (SplitViewPartially && splitStartsAt > 0) {
                for (int i = splitStartsAt; i < list.Count; i++) {
                    Blob blob = list[i];

                    Vector2 size = blob.Measure();
                    sizeColumn.X = Math.Max(sizeColumn.X, size.X);

                    // have we reached half the splittable height or enforced a split?
                    if (!splitSuccessfully && (sizeColumn.Y > sizeToSplit.Y / 2f + BlobSpacing * scale || forceSplitAt == i)) {
                        if (blob.CanSplit) {
                            switchedSidesAt = i;
                            // trim the excess blob spacing below
                            sizeColumn.Y -= BlobSpacing * scale;
                            maxColumnY = sizeColumn.Y;
                            sizeColumn.Y = 0f;
                            splitSuccessfully = true;
                        } else if (lastPossibleSplit > splitStartsAt && i > lastPossibleSplit && forceSplitAt == -1) {
                            // this is for cases where the last possible split was before the half-way point in height; forcing with a "goto retry"
                            forceSplitAt = lastPossibleSplit;
                            list[lastPossibleSplit].CanSplit = true;
                            goto RetryPartialSplit;
                        }
                    }
                    blob.Dyn.Y = sizeAll.Y + sizeColumn.Y;
                    sizeColumn.Y += size.Y + BlobSpacing * scale;
                }

                // trim the excess blob spacing below
                sizeColumn.Y -= BlobSpacing * scale;

                if (splitSuccessfully) {

                    if (sizeColumn.X * 2 < sizeAll.X) {
                        sizeColumn.X = sizeAll.X / 2;
                    } else {
                        // some padding for when the individual rects get drawn later
                        sizeColumn.X += PaddingX * scale;
                    }


                    // move all the right column's elements to the right via Dyn.X
                    for (int i = switchedSidesAt; i < list.Count; i++)
                        list[i].Dyn.X = sizeColumn.X + PaddingX * scale + SplitGap * scale;
                }

                if (sizeColumn.Y > maxColumnY)
                    maxColumnY = sizeColumn.Y;

                sizeAll.Y += maxColumnY;
                sizeAll.X = Math.Max(sizeAll.X, sizeColumn.X * 2f);
            }

            SizeAll = sizeAll;
            SizeUpper = sizeUpper;
            SizeColumn = new(sizeColumn.X, maxColumnY);
            SplitSuccessfully = splitSuccessfully;
            SplitStartsAt = splitStartsAt;
        }

        private string GetOrderKey(DataPlayerInfo? player) {
            if (player == null)
                return "9";

            if (Client?.Data?.TryGetBoundRef(player, out DataPlayerState? state) == true && !string.IsNullOrEmpty(state?.SID))
                return $"0 {(state.SID.StartsWith("Celeste/") ? "0" : "1") + state.SID + (int) state.Mode} {player.FullName}";

            return $"8 {player.FullName}";
        }

        private DataPlayerInfo? GetPlayerInfo(uint id) {
            if (Client?.Data?.TryGetRef(id, out DataPlayerInfo? player) == true && !string.IsNullOrEmpty(player?.DisplayName))
                return player;
            return null;
        }

        private DataPlayerInfo? ListPlayerUnderChannel(BlobPlayer blob, DataPlayerInfo? player, LocationModes locationMode, bool withPing) {
            if (player != null) {
                blob.Player = player;
                blob.Name = player.DisplayName;

                blob.LocationMode = locationMode;

                if (Client?.Data != null) {
                    if (Client.Data.TryGetBoundRef(player, out DataPlayerState? state) && state != null) {
                        if (locationMode != LocationModes.OFF)
                            GetState(blob, state);

                        blob.Idle = state.Idle;
                    }

                    if (ShowPing && withPing && Client.Data.TryGetBoundRef(player, out DataConnectionInfo? conInfo) && conInfo != null)
                        blob.PingMs = conInfo.UDPPingMs ?? conInfo.TCPPingMs;
                }

                return player;

            } else {
                blob.Name = "?";
                blob.LocationMode = LocationModes.OFF;
                return null;
            }
        }

        public void GetState(BlobPlayer blob, DataPlayerState state) {
            if (!string.IsNullOrWhiteSpace(state.SID)) {
                CelesteNetLocationInfo location = new(state);

                blob.Location.Color = DefaultLevelColor;
                blob.Location.TitleColor = Color.Lerp(location.Area?.TitleBaseColor ?? Color.White, DefaultLevelColor, 0.5f);
                blob.Location.AccentColor = Color.Lerp(location.Area?.TitleAccentColor ?? Color.White, DefaultLevelColor, 0.8f);

                blob.Location.SID = location.SID ?? "";
                blob.Location.Name = location.Name ?? "";
                blob.Location.Side = location.Side ?? "";
                blob.Location.Level = location.Level ?? "";

                blob.Location.IsRandomizer = location.IsRandomizer;
                blob.Location.Icon = location.Icon ?? "";

                ShortenRandomizerLocation(ref blob.Location);
            }

            // Allow mods to override f.e. the displayed location name or icon very easily.
            OnGetState?.Invoke(blob, state);
        }

        private void ShortenRandomizerLocation(ref BlobLocation location) {
            /*
             * Randomizer Locations usually are very long like
             * Celeste/1-ForsakenCity/A/b-02/31 randomizer/Mirror Temple_0_1234567 A
             */

            if (!location.IsRandomizer || !Settings.UICustomize.PlayerListShortenRandomizer)
                return;

            // shorten the randomizer/ part down
            int split = location.Name.IndexOf('/');
            if (split >= 0)
                location.Name = "rnd/" + location.Name.Substring(split + 1);

            // yoink out all the funny numbers like _0_1234567 at the end
            location.Name = location.Name.TrimEnd(RandomizerEndTrimChars);

            // Only display the last two parts of the currently played level.
            split = location.Level.LastIndexOf('/');
            if ((split - 1) >= 0)
                split = location.Level.LastIndexOf('/', split - 1);
            if (split >= 0)
                location.Level = location.Level.Substring(split + 1);

            // Side seems to always be 0 = A so clear that
            location.Side = "";
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            RunOnMainThread(() => RebuildList());
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            RunOnMainThread(() => {
                if (MDraw.DefaultFont == null || Client?.PlayerInfo == null || Client?.Data == null || Channels == null)
                    return;

                // Don't rebuild the entire list
                // Try to find the player's blob
                BlobPlayer? playerBlob = (BlobPlayer?) List?.FirstOrDefault(b => b is BlobPlayer pb && pb.Player == state.Player);
                if (playerBlob == null || playerBlob.Location.SID.IsNullOrEmpty() || playerBlob.Location.SID != state.SID || playerBlob.Location.Level.Length < state.Level.Length - 1) {
                    RebuildList();
                    return;
                }

                // just update blob state, since SID hasn't changed
                if (LocationMode != LocationModes.OFF)
                    GetState(playerBlob, state);

                playerBlob.Idle = state.Idle;
                playerBlob.Generate();
            });

        }

        public void Handle(CelesteNetConnection con, DataConnectionInfo info) {
            RunOnMainThread(() => {
                if (!ShowPing)
                    return;

                if (MDraw.DefaultFont == null || Client?.PlayerInfo == null || Client?.Data == null || Channels == null || info?.Player == null)
                    return;

                // Don't rebuild the entire list
                // Try to find the player's blob
                BlobPlayer? playerBlob = (BlobPlayer?) List?.FirstOrDefault(b => b is BlobPlayer pb && pb.Player == info.Player);
                if (playerBlob == null)
                    return;

                DataChannelList.Channel? own = Channels.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));
                if (own == null)
                    return;

                if (ListMode == ListModes.Channels && !own.Players.Contains(info.Player.ID))
                    return;

                // Update the player's ping
                playerBlob.PingMs = info.UDPPingMs ?? info.TCPPingMs;

                // Regenerate the player blob
                playerBlob.Generate();

                Vector2 size = playerBlob.Measure();

                SizeAll.X = Math.Max(size.X, SizeAll.X);
            });
        }

        public void Handle(CelesteNetConnection con, DataChannelList channels) {
            RunOnMainThread(() => {
                Channels = channels;
                RebuildList();
            });
        }
        
        public void Handle(CelesteNetConnection con, DataNetEmoji netemoji) {
            if (!netemoji.MoreFragments)
                ShouldRebuild = true;
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (LastListMode != ListMode ||
                LastLocationMode != LocationMode ||
                LastShowPing != ShowPing ||
                LastAllowSplit != AllowSplit ||
                LastScale != Scale ||
                LastHideOwnChannelName != HideOwnChannelName ||
                ShouldRebuild) {
                LastListMode = ListMode;
                LastLocationMode = LocationMode;
                LastShowPing = ShowPing;
                LastAllowSplit = AllowSplit;
                LastScale = Scale;
                LastHideOwnChannelName = HideOwnChannelName;
                ShouldRebuild = false;
                RebuildList();
            }

            // shouldn't open player list on pause menu, although now with rebindable hotkey, should this still be this way?
            if (Engine.Scene?.Paused != true)
            {
                if (Settings.ButtonPlayerList.Pressed() && !Active)
                {
                    // open right away upon Pressed instead of Released, and remember that it's potentially held down now
                    PropActive = true;
                    ButtonInitialHold = true;
                }
                else if (Settings.ButtonPlayerList.Released())
                {
                    // if the bind is released, make sure it wasn't being held for scrolling purposes
                    // if scrolling while holding it down, we most likely don't want to close again
                    if (!ButtonInitialHold && ((!hasScrolled && ButtonHoldTime < ActualScrollDelay * ScrollDelayLeniency) || Settings.PlayerListUI.ScrollDelay == 0))
                        PropActive = !PropActive;

                    hasScrolled = ButtonInitialHold = false;
                }
            }

            if (Settings.PlayerListUI.PlayerListScrollMode == ScrollModes.HoldTab)
            {
                // scrolling mode: just keep scrolling down while button is held, after delay period
                if (Settings.PlayerListUI.ScrollDelay == 0 ? ButtonInitialHold : (Settings.ButtonPlayerList.Check()))
                {
                    // adding a slight minimum delay even to (Settings.PlayerListUI.PlayerListScrollDelay == 0) so that you don't
                    // IMMEDIATELY scroll the player count off the top of the list
                    if (ButtonHoldTime > ActualScrollDelay)
                    {
                        InputScrollState = 1;
                        hasScrolled = true;
                    }
                    else
                    {
                        ButtonHoldTime += Engine.RawDeltaTime;
                    }
                }
                else if (ButtonHoldTime > 0f)
                    ButtonHoldTime -= Engine.RawDeltaTime * 2f;
            }
            else if ((Settings.PlayerListUI.PlayerListScrollMode == ScrollModes.KeybindsOnHold && Settings.ButtonPlayerList.Check())
                    || Settings.PlayerListUI.PlayerListScrollMode == ScrollModes.Keybinds)
            {
                // in keybinds modes, deal with up/down buttons
                if (Settings.ButtonPlayerListScrollUp.Check() || Settings.ButtonPlayerListScrollDown.Check())
                {
                    InputScrollState = InputScrollDownState - InputScrollUpState;
                    hasScrolled = Settings.PlayerListUI.PlayerListScrollMode == ScrollModes.KeybindsOnHold;
                    Settings.ButtonPlayerListScrollDown?.ConsumePress();
                    Settings.ButtonPlayerListScrollUp?.ConsumePress();
                }
            }

            // adjusting the actual scroll value: Don't scroll player list while chat is open
            if (InputScrollState != 0 && Context?.Chat?.Active != true)
            {
                ScrolledDistance = ScrolledDistance + InputScrollState * 6f;
                if (ScrolledDistance < 0f)
                    ScrolledDistance = 0f;
                else if (ScrolledDistance > SizeUpper.Y)
                    ScrolledDistance = SizeUpper.Y;
            }
            InputScrollState = 0;
        }

        public override void Draw(GameTime? gameTime) {
            if (Active)
                base.Draw(gameTime);
        }

        protected void PrepareRenderLayout(out float scale, out float y, out Vector2 sizeAll) {
            scale = Scale;
            y = Margin * scale;
            sizeAll = SizeAll;

            SpeedrunTimerDisplay? timer = Engine.Scene?.Entities.FindFirst<SpeedrunTimerDisplay>();
            if (timer != null) {
                switch (global::Celeste.Settings.Instance?.SpeedrunClock ?? SpeedrunType.Off) {
                    case SpeedrunType.Off:
                        break;

                    case SpeedrunType.Chapter:
                        if (timer.CompleteTimer < 3f)
                            y += MainTimerOffset;
                        break;

                    case SpeedrunType.File:
                    default:
                        y += MainTimerOffset + SubTimerOffset;
                        break;
                }
            }
        }

        protected override void Render(GameTime? gameTime, bool toBuffer) {
            PrepareRenderLayout(out float scale, out float y, out Vector2 sizeAll);

            float x = Margin * scale;
            float sizeAllXPadded = sizeAll.X + 2 * PaddingX * scale;
            float sizeAllXBlobs = sizeAll.X;
            float chatStartY = (Context?.Chat?.RenderPositionY ?? UI_HEIGHT) - ChatOffset;
            Color colorFull = Color.Black * 0.95f * CustomAlpha;
            Color colorFaded = Color.Black * 0.6f * CustomAlpha;

            switch (ListMode) {
                case ListModes.Classic:
                    SplitRectAbsolute(
                        x, y,
                        sizeAllXPadded, sizeAll.Y + 2 * PaddingY * scale - ScrolledDistance,
                        chatStartY,
                        colorFull, colorFaded
                    );
                    break;

                case ListModes.Channels:
                    if (SplitViewPartially && SplitSuccessfully) {
                        sizeAllXPadded += SplitGap * scale;
                        sizeAllXBlobs += SplitGap * scale;
                    }

                    // own channel box always there
                    SplitRectAbsolute(
                        x, y,
                        sizeAllXPadded, SizeUpper.Y + 2 * PaddingY * scale - ScrolledDistance,
                        chatStartY,
                        colorFull, colorFaded
                    );

                    // skip below the drawn rect and include a gap
                    float columnY = y + SizeUpper.Y + 2 * PaddingY * scale + SplitGap * scale - ScrolledDistance;

                    if (SplitViewPartially && SplitSuccessfully) {
                        // two rects for the two columns
                        float sizeColXPadded = SizeColumn.X + PaddingX * scale;
                        SplitRectAbsolute(
                            x, columnY,
                            sizeColXPadded, SizeColumn.Y + 2 * PaddingY * scale,
                            chatStartY,
                            colorFull, colorFaded
                        );

                        //skip past the left column and include the gap
                        float rightColumnX = x + sizeColXPadded + SplitGap * scale;

                        SplitRectAbsolute(
                            rightColumnX, columnY,
                            sizeAllXPadded - sizeColXPadded - SplitGap * scale, SizeColumn.Y + 2 * PaddingY * scale,
                            chatStartY,
                            colorFull, colorFaded
                        );
                    } else if (SplitStartsAt > 0) {
                        // single rect below the other, nothing was split after all
                        SplitRectAbsolute(
                            x, columnY,
                            sizeAllXPadded, sizeAll.Y - SizeUpper.Y - SplitGap * scale,
                            chatStartY,
                            colorFull, colorFaded
                        );
                        // NO Y PADDING??
                        // sizeAll.Y   => top blobs + padding + split gap + padding + bottom blobs
                        // SizeUpper.Y => top blobs
                        // therefore, (sizeAll.Y - SizeUpper.Y - SplitGap * scale) is exactly what we need:
                        // bottom blobs + 2 pads
                    }
                    // else no other channels, don't draw any rects
                    break;
            }

            y += PaddingY * scale;
            // Blobs need to use this width because there's a chance the split list padded its entire
            // width to account for the bottom split (start of case ListModes.Channels above)
            sizeAll.X = sizeAllXBlobs;

            float alpha;
            foreach (Blob blob in List) {
                if (blob.Dyn.Y < ScrolledDistance)
                {
                    continue;
                }
                alpha = (y + blob.Dyn.Y - ScrolledDistance < chatStartY) ? 1f : 0.5f;
                blob.Render(y - ScrolledDistance, scale, ref sizeAll, alpha * CustomAlpha);
            }

            if (ButtonHoldTime > 0.01f && Settings.PlayerListUI.ScrollDelay > 0) {
                float heldDelayRatio = ButtonHoldTime / (Settings.PlayerListUI.ScrollDelay/2f);
                float barFullWidth = sizeAll.X * HoldScrollDelayBarWidth;
                float barWidth = barFullWidth * heldDelayRatio;
                float barAlphaAdjust = (ButtonHoldTime < ActualScrollDelay * ScrollDelayLeniency) ? 0.5f : 1f;
                MDraw.Rect(x + sizeAll.X - barFullWidth - 1, y, barFullWidth + 1, HoldScrollDelayBarHeight,     Color.White   * .15f * barAlphaAdjust * CustomAlpha);
                MDraw.Rect(x + sizeAll.X - barWidth,     y + 1, barWidth,         HoldScrollDelayBarHeight - 2, Color.HotPink * .60f * barAlphaAdjust * CustomAlpha);
            }
        }

        private void SplitRectAbsolute(float x, float y, float width, float height, float splitAtY, Color colorA, Color colorB) {
            if (splitAtY > y + height) {
                Context?.RenderHelper?.Rect(x, y, width, height, colorA);
            }
            else if (splitAtY < y) {
                Context?.RenderHelper?.Rect(x, y, width, height, colorB);
            }
            else {
                SplitRect(x, y, width, height, splitAtY - y, colorA, colorB);
            }
        }

        private void SplitRect(float x, float y, float width, float height, float splitheight, Color colorA, Color colorB) {
            if (splitheight >= height) {
                Context?.RenderHelper?.Rect(x, y, width, height, colorA);
                return;
            }

            Context?.RenderHelper?.Rect(x, y, width, splitheight, colorA);
            Context?.RenderHelper?.Rect(x, y + splitheight, width, height - splitheight, colorB);
        }

        public class Blob {

            public string TextCached = "";

            public string Name = "";

            public Color Color = Color.White;

            public float ScaleFactor = 0f;
            public Vector2 Dyn = Vector2.Zero;
            public float DynScale;
            public bool CanSplit = false;

            public LocationModes LocationMode = LocationModes.ON;

            public virtual void Generate() {
                if (GetType() == typeof(Blob)) {
                    TextCached = Name;
                    return;
                }

                StringBuilder sb = new();
                Generate(sb);
                TextCached = sb.ToString();
            }

            protected virtual void Generate(StringBuilder sb) {
                sb.Append(Name);
            }

            public virtual Vector2 Measure() {
                return CelesteNetClientFont.Measure(TextCached) * DynScale;
            }

            public virtual void Render(float y, float scale, ref Vector2 sizeAll, float alpha, Vector2? justify = null) {
                CelesteNetClientFont.Draw(
                    TextCached,
                    new((Margin + PaddingX) * scale + Dyn.X, y + Dyn.Y),
                    justify ?? Vector2.Zero,
                    new(DynScale),
                    Color * alpha
                );
            }

        }

        public class BlobPlayer : Blob {

            public const string NoPingData = "???";

            public DataPlayerInfo? Player;
            public BlobLocation Location = new();

            public int? PingMs = null;
            public Blob PingBlob = new() {
                Color = Color.Gray
            };
            public static float PingPadWidth = 0;

            public bool Idle;

            protected override void Generate(StringBuilder sb) {
                sb.Append(Name);
                if (Idle)
                    sb.Append(" ").Append(IdleIconCode);

                if (PingMs.HasValue) {
                    int ping = PingMs.Value;
                    PingBlob.Name = $"{(ping > 0 ? ping : NoPingData)}ms";
                } else {
                    PingBlob.Name = string.Empty;
                }

                // If the player blob was forced to regenerate its text, forward that to the location and ping blobs too.
                PingBlob.Generate();
                Location.LocationMode = LocationMode;
                Location.Generate();
            }

            public override Vector2 Measure() {

                Vector2 size = base.Measure();

                // insert extra space for the idle icon on non-idle players too.
                if (!Idle)
                    size.X += IdleIconWidth * DynScale;

                // update ping blob
                PingBlob.Dyn.Y = Dyn.Y;
                PingBlob.DynScale = DynScale;

                // insert space for ping first, because it offsets location
                float pingWidth = PingBlob.Measure().X;
                PingPadWidth = Math.Max(PingPadWidth, pingWidth);
                size.X += PingPadWidth;

                // update & insert space for location
                Location.Offset = -PingPadWidth;
                Location.Dyn.Y = Dyn.Y;
                Location.DynScale = DynScale;
                Location.LocationMode = LocationMode;
                size.X += Location.Measure().X;

                return size;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float alpha, Vector2? justify = null) {
                base.Render(y, scale, ref sizeAll, alpha, justify);
                Location.Render(y, scale, ref sizeAll, alpha);
                // right-justify ping
                PingBlob.Dyn.X = Dyn.X + sizeAll.X;
                PingBlob.Render(y, scale, ref sizeAll, alpha, Vector2.UnitX);
            }

        }

        public class BlobRightToLeft : Blob {

            protected class TextPart {
                public string Text = "";
                public Color Color = Color.White;
                public float widthScaled;
            }

            protected List<TextPart> parts = new ();

            public float Offset = 0;

            protected void AddTextPart(string content, Color color) {
                parts.Add(new TextPart { Text = content, Color = color });
            }

            public override Vector2 Measure() {
                Vector2 size = new();

                foreach (TextPart p in parts) {
                    Vector2 measurement = CelesteNetClientFont.Measure(p.Text);
                    p.widthScaled = measurement.X * DynScale;
                    size.X += p.widthScaled;
                    if (measurement.Y > size.Y)
                        size.Y = measurement.Y;
                }
                size.X += SpaceWidth * DynScale * parts.Count;

                return size;
            }

            protected void DrawTextPart(string text, float textWidthScaled, Color color, float y, float scale, ref float x) {
                CelesteNetClientFont.Draw(
                    text,
                    new((Margin + PaddingX) * scale + x, y + Dyn.Y),
                    Vector2.UnitX, // Rendering bits right-to-left
                    new(DynScale),
                    color
                );
                x -= textWidthScaled;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float alpha, Vector2? justify = null) {
                float x = sizeAll.X + Dyn.X + Offset;

                for (int i = parts.Count - 1; i >= 0; i--) {
                    TextPart p = parts[i];
                    DrawTextPart(p.Text, p.widthScaled, p.Color * alpha, y, scale, ref x);
                    x -= SpaceWidth * DynScale;
                }
            }

        }


        public class BlobLocation : BlobRightToLeft {

            public const string LocationSeparator = ":";

            protected MTexture? GuiIconCached;

            public float IconSize => GuiIconCached != null ? 64f : 0f;
            public Vector2 IconOrigSize => GuiIconCached != null ? new Vector2(GuiIconCached.Width, GuiIconCached.Height) : new();
            public float IconScale => GuiIconCached != null ? Math.Min(IconSize / GuiIconCached.Width, IconSize / GuiIconCached.Height) : 1f;

            public string Side = "";
            public string Level = "";
            public string Icon = "";

            public string SID = "";

            public bool IsRandomizer;

            public Color TitleColor = DefaultLevelColor;
            public Color AccentColor = DefaultLevelColor;

            public BlobLocation() {
                Color = DefaultLevelColor;
            }

            public override void Generate() {
                GuiIconCached = (LocationMode & LocationModes.Icons) != 0 && GFX.Gui.Has(Icon) ? GFX.Gui[Icon] : null;
                if ((LocationMode & LocationModes.Text) == 0)
                    Name = "";
                if (parts.Count < 4) {
                    parts.Clear();
                    AddTextPart(Level, Color);
                    AddTextPart(LocationSeparator, Color.Lerp(Color, Color.Black, 0.5f));
                    AddTextPart(Name, TitleColor);
                    AddTextPart(Side, AccentColor);
                } else {
                    parts[0].Text = Level;
                    parts[2].Text = Name;
                    parts[3].Text = Side;
                }
            }

            public override Vector2 Measure() {
                if (string.IsNullOrEmpty(Name) || (LocationMode & LocationModes.Text) == 0)
                    return new(GuiIconCached != null ? IconSize * DynScale : 0f);

                Vector2 size = base.Measure();
                if (GuiIconCached != null)
                    size.X += (SpaceWidth + IconSize) * DynScale;
                return size;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float alpha, Vector2? justify = null) {
                if (!string.IsNullOrEmpty(Name)) {
                    Vector2 size = new(sizeAll.X + Dyn.X, sizeAll.Y);
                    if (GuiIconCached != null) {
                        size.X -= IconSize * DynScale;
                        size.X -= SpaceWidth * DynScale;
                    }

                    base.Render(y, scale, ref size, alpha, justify);
                }

                GuiIconCached?.Draw(
                    new((Margin + PaddingX) * scale + sizeAll.X + Dyn.X - IconSize * DynScale + Offset, y + Dyn.Y),
                    Vector2.Zero,
                    Color.White * alpha,
                    new Vector2(IconScale * DynScale)
                );
            }

        }

    }
}
