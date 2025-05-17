using System;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataPlayerInfo : DataType<DataPlayerInfo> {

        static DataPlayerInfo() {
            DataID = "playerInfo";
        }

        public uint ID;
        public string Name = ""; // 论坛名
        public string FullName = ""; // 带名称冲突后缀名 (可能在单客户端多连接时发生)
        public string AvatarID = ""; // 头像 emote ID
        public string AvatarURL = ""; // 头像 URL
        public string Prefix = ""; // 头衔
        public Color NameColor = Color.White;

        public string DisplayName = "";

        public DataPlayerInfo() { }

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRef(ID, !FullName.IsNullOrEmpty())
            };

        public override void FixupMeta(DataContext ctx) {
            ID = Get<MetaRef>(ctx);
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Name = reader.ReadNetString();
            FullName = reader.ReadNetString();
            AvatarID = reader.ReadNetString();
            AvatarURL = reader.ReadNetString();
            Prefix = reader.ReadNetString();
            NameColor = reader.ReadColorNoA();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Name);
            writer.WriteNetString(FullName);
            writer.WriteNetString(AvatarID);
            writer.WriteNetString(AvatarURL);
            writer.WriteNetString(Prefix);
            writer.WriteNoA(NameColor);
        }

        public override string ToString()
            => $"#{ID}: {FullName} ({Name})";

        public void UpdateDisplayName(bool avatarEnabled) {
            if (!string.IsNullOrEmpty(Prefix))
                DisplayName = $"[{Prefix}] {FullName}";
            else
                DisplayName = $"{FullName}";
            if (avatarEnabled)
                DisplayName = $":{AvatarID}: {DisplayName}";
        }
    }


    public abstract class MetaPlayerBaseType<T> : MetaType<T> where T : MetaPlayerBaseType<T> {

        public MetaPlayerBaseType() {
        }
        public MetaPlayerBaseType(DataPlayerInfo? player) {
            Player = player;
        }

        public DataPlayerInfo? Player;
        public DataPlayerInfo ForcePlayer {
            get => Player ?? throw new Exception($"{GetType().Name} with actual player expected.");
            set => Player = value ?? throw new Exception($"{GetType().Name} with actual player expected.");
        }

        public override void Read(CelesteNetBinaryReader reader) {
            reader.Data.TryGetRef(reader.Read7BitEncodedUInt(), out Player);
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedUInt(Player?.ID ?? uint.MaxValue);
        }

        public static implicit operator DataPlayerInfo?(MetaPlayerBaseType<T> meta)
            => meta.Player;

        public static implicit operator uint(MetaPlayerBaseType<T> meta)
            => meta.Player?.ID ?? uint.MaxValue;

    }


    public class MetaPlayerUpdate : MetaPlayerBaseType<MetaPlayerUpdate> {

        static MetaPlayerUpdate() {
            MetaID = "playerUpd";
        }

        public MetaPlayerUpdate()
            : base() {
        }
        public MetaPlayerUpdate(DataPlayerInfo? player)
            : base(player) {
        }

    }

    public class MetaPlayerPrivateState : MetaPlayerBaseType<MetaPlayerPrivateState> {

        static MetaPlayerPrivateState() {
            MetaID = "playerPrivSt";
        }

        public MetaPlayerPrivateState()
            : base() {
        }
        public MetaPlayerPrivateState(DataPlayerInfo? player)
            : base(player) {
        }

    }

    public class MetaPlayerPublicState : MetaPlayerBaseType<MetaPlayerPublicState> {

        static MetaPlayerPublicState() {
            MetaID = "playerPubSt";
        }

        public MetaPlayerPublicState()
            : base() {
        }
        public MetaPlayerPublicState(DataPlayerInfo? player)
            : base(player) {
        }

    }
}
