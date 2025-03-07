using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Timers;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Microsoft.AspNetCore.StaticFiles;
using Newtonsoft.Json;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class Frontend : CelesteNetServerModule<FrontendSettings> {

        public static readonly string COOKIE_SESSION = "celestenet-session";
        public static readonly string COOKIE_DISCORDAUTH = "celestenet-discordauth";
        public static readonly string COOKIE_KEY = "celestenet-key";

        public readonly List<RCEndpoint> EndPoints = new();
        public readonly HashSet<string> CurrentSessionKeys = new();
        public readonly HashSet<string> CurrentSessionExecKeys = new();

        public readonly Dictionary<string, BasicUserInfo> TaggedUsers = new();

        private HttpServer? HTTPServer;
        private WebSocketServiceHost? WSHost;

        private Timer? StatsTimer;
        private readonly Dictionary<string, long> WSUpdateCooldowns = new();
        public static readonly long WSUpdateCooldownTime = 1000;

        private readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

        public JsonSerializer Serializer = new() {
            Formatting = Formatting.Indented
        };

        public override void Init(CelesteNetServerModuleWrapper wrapper) {
            base.Init(wrapper);

            Logger.Log(LogLevel.VVV, "frontend", "Scanning for endpoints");
            foreach (Type t in CelesteNetUtils.GetTypes()) {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                    foreach (RCEndpointAttribute epa in m.GetCustomAttributes(typeof(RCEndpointAttribute), false)) {
                        RCEndpoint ep = epa.Data;
                        Logger.Log(LogLevel.VVV, "frontend", $"Found endpoint: {ep.Path} - {ep.Name} ({m.Name}::{t.FullName})");
                        ep.Handle = (f, c) => m.Invoke(null, new object[] { f, c });
                        EndPoints.Add(ep);
                    }
                }
            }

            Server.OnConnect += OnConnect;
            Server.OnSessionStart += OnSessionStart;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.OnEnd += OnSessionEnd;
            Server.OnDisconnect += OnDisconnect;

            Server.Channels.OnBroadcastList += OnBroadcastChannels;
            Server.Channels.OnCreate += OnCreateChannel;
            Server.Channels.OnRemove += OnRemoveChannel;
            Server.Channels.OnMove += OnChannelMove;

            ChatModule chat = Server.Get<ChatModule>();
            chat.OnReceive += OnChatReceive;
            chat.OnApplyFilter += OnChatFilter;
            chat.OnForceSend += OnForceSend;
        }

        public override void Start() {
            base.Start();

            Logger.Log(LogLevel.INF, "frontend", $"Startup on port {Settings.Port}");

            HTTPServer = new(Settings.Port);

            HTTPServer.OnGet += HandleRequestRaw;
            HTTPServer.OnPost += HandleRequestRaw;

            HTTPServer.WebSocketServices.AddService<FrontendWebSocket>($"{Settings.APIPrefix}/ws", ws => ws.Frontend = this);

            HTTPServer.Start();

            HTTPServer.WebSocketServices.TryGetServiceHost($"{Settings.APIPrefix}/ws", out WSHost);

            StatsTimer = new Timer(Settings.NetPlusStatsUpdateRate);
            StatsTimer.AutoReset = true;
            StatsTimer.Elapsed += (_, _) => RCEndpoints.UpdateStats(Server);
            StatsTimer.Enabled = true;

            foreach (var kvp in RefreshTaggedUserInfos()) {
                Logger.Log(LogLevel.VVV, "frontend", $"Found tagged user: {kvp.Key} - {kvp.Value.Name} = {string.Join(", ", kvp.Value.Tags)}");
            }
        }

        public override void Dispose() {
            base.Dispose();

            Logger.Log(LogLevel.INF, "frontend", "Shutdown");

            StatsTimer?.Dispose();

            try {
                HTTPServer?.Stop();
            } catch {
            }
            HTTPServer = null;

            Server.OnConnect -= OnConnect;
            Server.OnSessionStart -= OnSessionStart;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.OnEnd -= OnSessionEnd;
            Server.OnDisconnect -= OnDisconnect;

            Server.Channels.OnBroadcastList -= OnBroadcastChannels;
            Server.Channels.OnCreate -= OnCreateChannel;
            Server.Channels.OnRemove -= OnRemoveChannel;
            Server.Channels.OnMove -= OnChannelMove;

            if (Server.TryGet(out ChatModule? chat)) {
                chat.OnReceive -= OnChatReceive;
                chat.OnApplyFilter -= OnChatFilter;
                chat.OnForceSend -= OnForceSend;
            }
        }

        private void OnConnect(CelesteNetServer server, CelesteNetConnection con) {
            TryBroadcastUpdate(Settings.APIPrefix + "/status");
        }

        private void OnSessionStart(CelesteNetPlayerSession session) {
            if (!session.Alive)
                return;
            Broadcast(frontendWS => frontendWS.SendCommand(
                "sess_join", PlayerSessionToFrontend(session, frontendWS.IsAuthorized, true)
            ));
            TryBroadcastUpdate(Settings.APIPrefix + "/status");
            //TryBroadcastUpdate(Settings.APIPrefix + "/players");
            session.OnEnd += OnSessionEnd;
        }

        private void OnSessionEnd(CelesteNetPlayerSession session, DataPlayerInfo? lastPlayerInfo) {
            Broadcast(frontendWS => frontendWS.SendCommand(
                "sess_leave", PlayerSessionToFrontend(session, frontendWS.IsAuthorized, true)
            ));
            TryBroadcastUpdate(Settings.APIPrefix + "/status");
            //TryBroadcastUpdate(Settings.APIPrefix + "/players");
        }

        private void OnDisconnect(CelesteNetServer server, CelesteNetConnection con, CelesteNetPlayerSession? session) {
            if (session == null)
                TryBroadcastUpdate(Settings.APIPrefix + "/status");
        }

        private void OnBroadcastChannels(Channels obj) {
            TryBroadcastUpdate(Settings.APIPrefix + "/channels");
        }

        private void OnChannelMove(CelesteNetPlayerSession session, Channel? from, Channel? to) {
            BroadcastCMD(false, "chan_move",
                new {
                    session.SessionID,
                    fromID = from?.ID,
                    toID = to?.ID
                });
        }

        private void OnCreateChannel(Channel channel, int total) {
            BroadcastCMD(channel.IsPrivate, "chan_create",
                new {
                    Channel = new {
                        channel.ID,
                        channel.Name,
                        channel.IsPrivate,
                        Players = channel.Players.Select(p => p.SessionID)
                    },
                    Count = total
                });
        }

        private void OnRemoveChannel(string name, uint id, int total) {
            BroadcastCMD(false, "chan_remove",
                new {
                    Name = name,
                    ID = id,
                    Count = total
                });
        }

        private void OnChatReceive(ChatModule chat, DataChat msg) {
            BroadcastCMD(msg.Targets != null, "chat", msg.ToDetailedFrontendChat());
        }

        private void OnChatFilter(ChatModule chat, FilterDecision chatFilterDecision) {
            BroadcastCMD(true, "filter", new {
                // bit annoying but I'd rather have the enum values as strings in the JSON rather than ints
                Handling = chatFilterDecision.Handling.ToString(),
                Cause = chatFilterDecision.Cause.ToString(),

                PlayerID = chatFilterDecision.playerID,
                Name = chatFilterDecision.playerName,

                MsgID = chatFilterDecision.chatID,
                Text = chatFilterDecision.chatText,
                Tag = chatFilterDecision.chatTag
            });
        }

        private void OnForceSend(ChatModule chat, DataChat msg) {
            BroadcastCMD(msg.Targets != null, "chat", msg.ToDetailedFrontendChat());
        }

        public Dictionary<string, BasicUserInfo> RefreshTaggedUserInfos() {
            Logger.Log(LogLevel.VVV, "frontend", "RefreshTaggedUserInfos: ");

            string[] uids = Server.UserData.GetAll();

            Logger.Log(LogLevel.VVV, "frontend", "RefreshTaggedUserInfos: Got UIDs");
            TaggedUsers.Clear();
            foreach(string uid in uids) {
                BasicUserInfo info = Server.UserData.Load<BasicUserInfo>(uid);

                if (!info.Tags.Any())
                    continue;

                TaggedUsers.Add(uid, info);
            }
            Logger.Log(LogLevel.VVV, "frontend", "RefreshTaggedUserInfos: Got BasicUserInfos");

            return TaggedUsers;
        }

        public object PlayerSessionToFrontend(CelesteNetPlayerSession p, bool auth = false, bool shorten = false) {
            
            dynamic player = new ExpandoObject();

            player.ID = p.SessionID;
            player.Name = p.PlayerInfo?.Name;
            player.FullName = p.PlayerInfo?.FullName;
            player.DisplayName = p.PlayerInfo?.DisplayName;
            if (Server.UserData.HasFile(p.UID, "avatar.png"))
                player.Avatar =  $"{Settings.APIPrefix}/avatar?uid={p.UID}";

            if (auth) {
                player.UID = p.UID;
                player.Connection = p.Con.ID;
                player.ConnectionUID = p.Con.UID;
                player.DisplayName = p.PlayerInfo?.DisplayName;
            }

            ConPlusTCPUDPConnection? pCon = p.Con as ConPlusTCPUDPConnection;

            if (!shorten && auth && pCon != null && pCon.IsAlive) {
                try {
                    // 安全地获取连接速率信息
                    player.TCPPingMs = SafeGetProperty(() => pCon.TCPPingMs);
                    player.UDPPingMs = SafeGetProperty(() => pCon.UDPPingMs);
                    
                    // 安全地获取TCP接收速率
                    player.TCPDownlinkBpS = SafeGetProperty(() => pCon.TCPRecvRate?.ByteRate);
                    player.TCPDownlinkPpS = SafeGetProperty(() => pCon.TCPRecvRate?.PacketRate);
                    
                    // 安全地获取TCP发送速率
                    player.TCPUplinkBpS = SafeGetProperty(() => pCon.TCPSendRate?.ByteRate);
                    player.TCPUplinkPpS = SafeGetProperty(() => pCon.TCPSendRate?.PacketRate);
                    
                    // 安全地获取UDP接收速率
                    player.UDPDownlinkBpS = SafeGetProperty(() => pCon.UDPRecvRate?.ByteRate);
                    player.UDPDownlinkPpS = SafeGetProperty(() => pCon.UDPRecvRate?.PacketRate);
                    
                    // 安全地获取UDP发送速率
                    player.UDPUplinkBpS = SafeGetProperty(() => pCon.UDPSendRate?.ByteRate);
                    player.UDPUplinkPpS = SafeGetProperty(() => pCon.UDPSendRate?.PacketRate);
                } catch (Exception ex) {
                    Logger.Log(LogLevel.WRN, "frontend", $"获取连接速率信息时出错: {ex.Message}");
                }
            }

            try {
                // 使用反射安全地获取ExtendedHandshake.ConnectionData
                if (pCon != null && pCon.IsAlive) {
                    var getAssociatedDataMethod = pCon.GetType().GetMethod("GetAssociatedData", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (getAssociatedDataMethod != null) {
                        var connectionDataType = Type.GetType("Celeste.Mod.CelesteNet.ExtendedHandshake+ConnectionData, CelesteNet.Server");
                        if (connectionDataType != null) {
                            var conData = getAssociatedDataMethod.MakeGenericMethod(connectionDataType).Invoke(pCon, Array.Empty<object>());
                            if (conData != null) {
                                var checkEntriesProperty = connectionDataType.GetProperty("CheckEntries");
                                var checkEntriesValidProperty = connectionDataType.GetProperty("CheckEntriesValid");
                                if (checkEntriesProperty != null && checkEntriesValidProperty != null) {
                                    bool checkEntriesValid = (bool)checkEntriesValidProperty.GetValue(conData);
                                    if (checkEntriesValid) {
                                        var checkEntries = checkEntriesProperty.GetValue(conData) as Dictionary<string, string>;
                                        if (checkEntries != null) {
                                            player.ExtHandshakeCheckValues = new Dictionary<string, string>(checkEntries);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                // 忽略任何反射错误，确保前端不会因此崩溃
                Logger.Log(LogLevel.WRN, "frontend", $"获取ExtendedHandshake数据时出错: {ex.Message}");
            }

            return player;
        }

        // 安全地获取属性值的辅助方法
        private object? SafeGetProperty<T>(Func<T> getter)
        {
            try {
                return getter();
            } catch (ObjectDisposedException) {
                return null;
            } catch (Exception ex) {
                Logger.Log(LogLevel.VVV, "frontend", $"获取属性时出错: {ex.Message}");
                return null;
            }
        }

        public object UserInfoToFrontend(string uid, BasicUserInfo info, BanInfo? ban = null, KickHistory? kicks = null, bool authExec = false) {

            dynamic user = new ExpandoObject();

            user.UID = uid;
            user.Name = info.Name;
            user.Discrim = info.Discrim;
            user.Tags = info.Tags;
            user.Key = (!authExec && info.Tags.Contains(BasicUserInfo.TAG_AUTH)) || info.Tags.Contains(BasicUserInfo.TAG_AUTH_EXEC) ? null : Server.UserData.GetKey(uid);

            user.Ban = null;
            if (ban != null && !ban.Reason.IsNullOrEmpty()) {
                user.Ban = new {
                    ban.Name,
                    ban.Reason,
                    From = ban.From?.ToUnixTimeMillis() ?? 0,
                    To = ban.To?.ToUnixTimeMillis() ?? 0
                };
            }

            user.Kicks = null;
            if (kicks != null) {
                user.Kicks = kicks.Log.Select(e => new {
                    e.Reason,
                    From = e.From?.ToUnixTimeMillis() ?? 0
                }).ToArray();
            }

            return user;
        }

        private string? GetContentType(string path) {
            return ContentTypeProvider.TryGetContentType(path, out string contentType)
                ? contentType
                : null;
        }

        public Stream? OpenContent(string path, out string pathNew, out DateTime? lastMod, out string? contentType) {
            pathNew = path;

            try {
                string dir = Path.GetFullPath(Settings.ContentRoot);
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS)) {
                    lastMod = File.GetLastWriteTimeUtc(pathFS);
                    contentType = GetContentType(pathFS);
                    return File.OpenRead(pathFS);
                }
            } catch {
            }

#if DEBUG
            try {
                string dir = Path.GetFullPath(Path.Combine("..", "..", "..", "Content"));
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS)) {
                    lastMod = File.GetLastWriteTimeUtc(pathFS);
                    contentType = GetContentType(pathFS);
                    return File.OpenRead(pathFS);
                }
            } catch {
            }

            try {
                string dir = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "CelesteNet.Server.FrontendModule", "Content"));
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS)) {
                    lastMod = File.GetLastWriteTimeUtc(pathFS);
                    contentType = GetContentType(pathFS);
                    return File.OpenRead(pathFS);
                }
            } catch {
            }
#endif

            if (!path.EndsWith("/index.html")) {
                path = path.EndsWith("/") ? path : (path + "/");
                Stream? index = OpenContent(path + "index.html", out _, out lastMod, out contentType);
                if (index != null) {
                    pathNew = path;
                    return index;
                }
            }

            lastMod = null;
            contentType = GetContentType(path);
            return typeof(CelesteNetServer).Assembly.GetManifestResourceStream("Celeste.Mod.CelesteNet.Server.Content." + path.Replace("/", "."));
        }

        private void HandleRequestRaw(object? sender, HttpRequestEventArgs c) {
            try {
                using (c.Request.InputStream)
                using (c.Response) {
                    HandleRequest(c);
                }

            } catch (Exception e) {
                Logger.Log(LogLevel.ERR, "frontend", $"Frontend failed responding: {e}");
            }
        }

        private void HandleRequest(HttpRequestEventArgs c) {
            Logger.Log(LogLevel.VVV, "frontend", $"{c.Request.RemoteEndPoint} requested: {c.Request.RawUrl}");

            string urlRaw = c.Request.RawUrl;
            string url = urlRaw;
            int indexOfSplit = url.IndexOf('?');
            if (indexOfSplit != -1)
                url = url.Substring(0, indexOfSplit);

            if (!url.ToLowerInvariant().StartsWith(Settings.APIPrefix)) {
                RespondContent(c, "frontend/" + url.Substring(1));
                return;
            }

            string urlApiRaw = urlRaw.Substring(Settings.APIPrefix.Length);
            string urlApi = url.Substring(Settings.APIPrefix.Length);

            RCEndpoint? endpoint =
                EndPoints.FirstOrDefault(ep => ep.Path == urlApiRaw) ??
                EndPoints.FirstOrDefault(ep => ep.Path == urlApi) ??
                EndPoints.FirstOrDefault(ep => ep.Path.ToLowerInvariant() == urlApiRaw.ToLowerInvariant()) ??
                EndPoints.FirstOrDefault(ep => ep.Path.ToLowerInvariant() == urlApi.ToLowerInvariant());

            if (endpoint == null) {
                RespondContent(c, "frontend/" + url.Substring(1));
                return;
            }

            c.Response.Headers.Set("Cache-Control", "no-store, max-age=0, s-maxage=0, no-cache, no-transform");

            if (endpoint.Auth) {
                if (TryGetSessionAuthCookie(c) is string session && IsAuthorized(c)) {
                    // refresh this with a new expiry date
                    SetSessionAuthCookie(c, session);
                } else {
                    c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                    RespondJSON(c, new {
                        Error = "Unauthorized."
                    });
                    return;
                }
            }

            endpoint.Handle(this, c);
        }

        public void SetCookie(HttpRequestEventArgs c, string name, string value, string path = "/", DateTime? expires = null) {
            c.Response.SetCookie(new(name, value, path) {
                Expires = expires ?? DateTime.MinValue,
                HttpOnly= true
            });
        }

        public void SetSessionAuthCookie(HttpRequestEventArgs c, string value, DateTime? expires = null) =>
            SetCookie(c, COOKIE_SESSION, value, "/", expires ?? DateTime.Now.AddDays(30));

        public void UnsetSessionAuthCookie(HttpRequestEventArgs c) => SetSessionAuthCookie(c, "");

        public void SetDiscordAuthCookie(HttpRequestEventArgs c, string value, DateTime? expires = null) =>
            SetCookie(c, COOKIE_DISCORDAUTH, value, "/", expires ?? DateTime.Now.AddMonths(6));

        public void UnsetDiscordAuthCookie(HttpRequestEventArgs c) => SetDiscordAuthCookie(c, "");

        public void SetKeyCookie(HttpRequestEventArgs c, string key, DateTime? expires = null) =>
            SetCookie(c, COOKIE_KEY, key, "/", expires ?? DateTime.Now.AddDays(90));

        public void UnsetKeyCookie(HttpRequestEventArgs c) => SetKeyCookie(c, "");

        public void UnsetAllCookies(HttpRequestEventArgs c) {
            UnsetDiscordAuthCookie(c);
            UnsetSessionAuthCookie(c);
            UnsetKeyCookie(c);
        }

        public string? TryGetCookie(HttpRequestEventArgs c, string name) => c.Request.Cookies[name]?.Value;
        public string? TryGetSessionAuthCookie(HttpRequestEventArgs c) => c.Request.Cookies[COOKIE_SESSION]?.Value;
        public string? TryGetDiscordAuthCookie(HttpRequestEventArgs c) => c.Request.Cookies[COOKIE_DISCORDAUTH]?.Value;
        public string? TryGetKeyCookie(HttpRequestEventArgs c) => c.Request.Cookies[COOKIE_KEY]?.Value;

        public bool IsAuthorized(HttpRequestEventArgs c)
            => TryGetSessionAuthCookie(c) is string session && CurrentSessionKeys.Contains(session);

        public bool IsAuthorizedExec(HttpRequestEventArgs c)
            => TryGetSessionAuthCookie(c) is string session && CurrentSessionExecKeys.Contains(session);

        public string GetNewKey(bool execAuth = false) {
            string key;
            do {
                key = Guid.NewGuid().ToString();
            } while (!CurrentSessionKeys.Add(key) || (execAuth && !CurrentSessionExecKeys.Add(key)));
            return key;
        }

        public void TryBroadcastUpdate(string path, bool authOnly = false) {
            WSUpdateCooldowns.TryGetValue(path, out long cd);
            if (cd > DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) {
                Logger.Log(LogLevel.VVV, "frontend", $"Not sending 'cmd update {path}' wscmd because of WSUpdateCooldown");
                return;
            }
            WSUpdateCooldowns[path] = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond + WSUpdateCooldownTime;
            
            BroadcastCMD(authOnly, "update", path);
        }

        public void BroadcastCMD(bool authOnly, string id, object obj) {
            Broadcast(frontendWS => {
                if (!authOnly || frontendWS.IsAuthorized)
                    frontendWS.SendCommand(id, obj);
            });
        }

        public void Broadcast(Action<FrontendWebSocket> callback) {
            if (WSHost == null)
                return;

            foreach (FrontendWebSocket frontendWS in WSHost.Sessions.Sessions) {
                try {
                    callback(frontendWS);
                } catch (Exception e) {
                    Logger.Log(LogLevel.VVV, "frontend", $"Failed broadcasting:\n{frontendWS.CurrentEndPoint}\n{e}");
                }
            }
        }

        #region Read / Parse Helpers

        public NameValueCollection ParseQueryString(string url) {
            NameValueCollection nvc = new();

            int indexOfSplit = url.IndexOf('?');
            if (indexOfSplit == -1)
                return nvc;
            url = url.Substring(indexOfSplit + 1);

            string[] args = url.Split('&');
            foreach (string arg in args) {
                indexOfSplit = arg.IndexOf('=');
                if (indexOfSplit == -1)
                    continue;
                nvc[arg.Substring(0, indexOfSplit)] = arg.Substring(indexOfSplit + 1);
            }

            return nvc;
        }

        #endregion

        #region Write Helpers

        public void RespondContent(HttpRequestEventArgs c, string id) {
            using Stream? s = OpenContent(id, out string pathNew, out DateTime? lastMod, out string? contentType);
            if (s == null) {
                c.Response.StatusCode = (int) HttpStatusCode.NotFound;
                RespondJSON(c, new {
                    Error = "Resource not found."
                });
                return;
            }

            if (id != pathNew && pathNew.StartsWith("frontend/")) {
                // c.Response.Redirect($"http://{c.Request.UserHostName}/{pathNew.Substring(9)}");
                c.Response.StatusCode = (int) HttpStatusCode.Moved;
                c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/{pathNew.Substring(9)}");
                Respond(c, $"Redirecting to /{pathNew.Substring(9)}");
                return;
            }

            if (lastMod != null)
                c.Response.Headers.Set("Last-Modified", lastMod.Value.ToString("r"));

            if (contentType != null)
                c.Response.ContentType = contentType;

            Respond(c, s.ToBytes());
        }

        public void RespondContent(HttpRequestEventArgs c, Stream s) {
            Respond(c, s.ToBytes());
        }

        public void RespondJSON(HttpRequestEventArgs c, object obj) {
            using MemoryStream ms = new();
            using (StreamWriter sw = new(ms, CelesteNetUtils.UTF8NoBOM, 1024, true))
            using (JsonTextWriter jtw = new(sw))
                Serializer.Serialize(jtw, obj);
            
            ms.Seek(0, SeekOrigin.Begin);

            c.Response.ContentType = "application/json";
            Respond(c, ms.ToArray());
        }

        public void Respond(HttpRequestEventArgs c, string str) {
            Respond(c, CelesteNetUtils.UTF8NoBOM.GetBytes(str));
        }

        public void Respond(HttpRequestEventArgs c, byte[] buf) {
            c.Response.ContentLength64 = buf.Length;
            c.Response.Close(buf, true);
        }

        #endregion

    }
}
