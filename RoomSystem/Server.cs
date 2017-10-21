using DarkRift.Server;

namespace RoomSystemPlugin
{
    public class Server
    {
        public ushort Port { get; }
        public bool IsAvailable { get; set; } = false;
        public Client Client { get; }

        public Server(ushort port, Client client)
        {
            Port = port;
            Client = client;
        }
    }
}
