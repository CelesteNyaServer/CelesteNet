using System;
using System.IO;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace Celeste.Mod.CelesteNet.Server
{
    public abstract class CelesteNetServerModule : IDisposable {

#pragma warning disable CS8618 // Set on init.
        public CelesteNetServer Server;
        public CelesteNetServerModuleWrapper Wrapper;
#pragma warning restore CS8618

        public virtual CelesteNetServerModuleSettings? GetSettings() => null;

        public virtual void Init(CelesteNetServerModuleWrapper wrapper) {
            Server = wrapper.Server;
            Wrapper = wrapper;

            Server.Data.RegisterHandlersIn(this);

            LoadSettings();
        }

        public virtual void LoadSettings() {
        }

        public virtual void SaveSettings() {
        }

        public virtual void Start() {
        }

        public virtual void Dispose() {
            SaveSettings();

            Server.Data.UnregisterHandlersIn(this);
        }

    }

    public abstract class CelesteNetServerModule<TSettings> : CelesteNetServerModule where TSettings : CelesteNetServerModuleSettings, new() {

        public TSettings Settings = new();
        public override CelesteNetServerModuleSettings? GetSettings() => Settings;

        public override void LoadSettings() {
            (Settings ??= new()).Load(Path.Combine(Path.GetFullPath(Server.Settings.ModuleConfigRoot), $"{Wrapper.ID}.yaml"));
        }

        public override void SaveSettings() {
            (Settings ??= new()).Save(Path.Combine(Path.GetFullPath(Server.Settings.ModuleConfigRoot), $"{Wrapper.ID}.yaml"));
        }

    }

    public abstract class CelesteNetServerModuleSettings {

        [YamlIgnore]
        [JsonIgnore]
        public string FilePath = "";

        public virtual void Load(string path = "") {
            FilePath = path = Path.GetFullPath(path.Nullify() ?? FilePath);

            if (!File.Exists(path)) {
                Save(path);
                return;
            }

            Logger.Log(LogLevel.INF, "settings", $"Loading {GetType().Name} from {path}");

            using Stream stream = File.OpenRead(path);
            using StreamReader reader = new(stream);
            Load(reader);
        }

        public virtual void Load(TextReader reader) {
            YamlHelper.DeserializerUsing(this).Deserialize(reader, GetType());
        }

        public virtual void Save(string path = "") {
            path = Path.GetFullPath(path.Nullify() ?? FilePath);

            Logger.Log(LogLevel.INF, "settings", $"Saving {GetType().Name} to {path}");

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (Stream stream = File.OpenWrite(path + ".tmp"))
            using (StreamWriter writer = new(stream))
                Save(writer);

            if (File.Exists(path))
                File.Delete(path);
            File.Move(path + ".tmp", path);
        }

        public virtual void Save(TextWriter writer) {
            YamlHelper.Serializer.Serialize(writer, this, GetType());
        }

    }
}
