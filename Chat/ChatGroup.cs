using System.Collections.Generic;
using DarkRift;
using DarkRift.Server;

namespace ChatPlugin
{
    public class ChatGroup : IDarkRiftSerializable
    {
        public ushort Id { get; }
        public string Name { get; }
        public List<Client> Clients = new List<Client>();
        

        public ChatGroup(ushort id, string name)
        {
            Id = id;
            Name = name;
        }

        internal bool AddPlayer(Client client)
        {
            if (Clients.Contains(client))
                return false;

            Clients.Add(client);
            return true;
        }

        internal bool RemovePlayer(Client client)
        {
            if (!Clients.Contains(client))
                return false;

            Clients.Remove(client);
            return true;
        }

        public void Serialize(SerializeEvent e)
        {
            e.Writer.Write(Id);
            e.Writer.Write(Name);
        }

        public void Deserialize(DeserializeEvent e)
        {
        }
    }
}