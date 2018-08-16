using System.Net;
using DarkRift.Server;

namespace RoomSystemPlugin
{
    public class Server
    {
        public IPAddress Ip { get; }
        public ushort Port { get; }
        public bool IsAvailable { get; set; } = false;
        public IClient Client { get; }
        public Room Room { get; set; }

        public Server(IPAddress ip, ushort port, IClient client)
        {
            Ip = ip;
            Port = port;
            Client = client;
        }
    }
}
