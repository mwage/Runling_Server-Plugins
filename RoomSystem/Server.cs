using DarkRift.Server;

namespace RoomSystemPlugin
{
    public class Server
    {
        public ushort Port { get; }
        public bool IsAvailable { get; set; } = false;
        public IClient Client { get; }
        public Room Room { get; set; }

        public Server(ushort port, IClient client)
        {
            Port = port;
            Client = client;
        }
    }
}
