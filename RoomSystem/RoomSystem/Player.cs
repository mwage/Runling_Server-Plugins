using DarkRift;

namespace RoomSystemPlugin
{
    internal class Player : IDarkRiftSerializable
    {
        public uint Id { get; }
        public string Name { get; }
        public bool IsHost { get; }
        public Color Color { get; private set; }

        public Player(uint id, string name, bool isHost, Color color)
        {
            Id = id;
            Name = name;
            IsHost = isHost;
            Color = color;
        }

        public void SetNewColor(Color color)
        {
            Color = color;
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(Id);
            e.Writer.Write(Name);
            e.Writer.Write(IsHost);
            e.Writer.Write((byte)Color);
        }

        public void Deserialize(DeserializeEvent e)
        {
        }
    }
}