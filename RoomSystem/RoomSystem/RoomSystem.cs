using System;
using System.Collections.Generic;
using System.Linq;
using DarkRift;
using DarkRift.Server;
using LoginPlugin;

namespace RoomSystemPlugin
{
    public class RoomSystem : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => false;

        // Tag
        private const byte RoomTag = 3;

        // Subjects
        private const ushort Create = 0;
        private const ushort Join = 1;
        private const ushort Leave = 2;
        private const ushort GetOpenRooms = 3;
        private const ushort GetOpenRoomsFailed = 4;
        private const ushort CreateFailed = 5;
        private const ushort CreateSuccess = 6;
        private const ushort JoinFailed = 7;
        private const ushort JoinSuccess = 8;
        private const ushort PlayerJoined = 9;
        private const ushort LeaveSuccess = 10;
        private const ushort ChangeColor = 11;
        private const ushort ChangeColorSuccess = 12;
        private const ushort ChangeColorFailed = 13;

        private Login _loginPlugin;
        private readonly Dictionary<ushort, Room> _roomList = new Dictionary<ushort, Room>();
        private readonly Dictionary<uint, Room> _playersInRooms = new Dictionary<uint, Room>();

        public RoomSystem(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            ClientManager.ClientConnected += OnPlayerConnected;
            ClientManager.ClientDisconnected += OnPlayerDisconnected;
        }

        private void OnPlayerConnected(object sender, ClientConnectedEventArgs e)
        {
            e.Client.MessageReceived += OnMessageReceived;

            if (_loginPlugin == null)
            {
                _loginPlugin = PluginManager.GetPluginByType<Login>();
            }
        }

        private void OnPlayerDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            var id = e.Client.GlobalID;
            if (_playersInRooms.ContainsKey(id))
            {
                var room = _playersInRooms[id];
                room.RemovePlayer(id);

                // Let all clients know that the player dropped
                var writer = new DarkRiftWriter();
                writer.Write(id);

                foreach (var cl in room.Clients)
                {
                    cl.SendMessage(new TagSubjectMessage(RoomTag, LeaveSuccess, writer), SendMode.Reliable);
                }

                _playersInRooms.Remove(id);
            }
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!(e.Message is TagSubjectMessage message) || message.Tag != RoomTag)
                return;

            var client = (Client) sender;

            // Create Room Request
            if (message.Subject == Create)
            {
                if (!_loginPlugin.UsersLoggedIn.ContainsKey(client.GlobalID))
                {
                    // If player isn't logged in -> return error 2
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(RoomTag, CreateFailed, writer), SendMode.Reliable);
                    return;
                }

                string name;
                GameType gameMode;
                PlayerColor color;
                bool isVisible;

                try
                {
                    var reader = message.GetReader();
                    name = reader.ReadString();
                    gameMode = (GameType) reader.ReadByte();
                    isVisible = reader.ReadBoolean();
                    color = (PlayerColor)reader.ReadByte();
                }
                catch (Exception ex)
                {
                    WriteEvent("Room Create Failed! Invalid Data received: " + ex.Message + " - " + ex.StackTrace,
                        LogType.Warning);

                    // Return Error 0 for Invalid Data Packages Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte) 0);
                    client.SendMessage(new TagSubjectMessage(RoomTag, CreateFailed, writer), SendMode.Reliable);
                    return;
                }

                var roomId = GenerateRoomId();
                var room = new Room(name, gameMode, isVisible);
                room.AddPlayer(new Player(client.GlobalID, _loginPlugin.UsersLoggedIn[client.GlobalID], true, color),
                    client);
                _roomList.Add(roomId, room);
                _playersInRooms.Add(client.GlobalID, room);

                var wr = new DarkRiftWriter();
                wr.Write(roomId);
                wr.Write(room);
                wr.Write((byte) color);
                client.SendMessage(new TagSubjectMessage(RoomTag, CreateSuccess, wr), SendMode.Reliable);
            }

            // Join Room Request
            if (message.Subject == Join)
            {
                if (!_loginPlugin.UsersLoggedIn.ContainsKey(client.GlobalID))
                {
                    // If player isn't logged in -> return error 2
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(RoomTag, JoinFailed, writer), SendMode.Reliable);
                    return;
                }

                ushort roomId;
                string playerName;
                PlayerColor color;

                try
                {
                    var reader = message.GetReader();
                    roomId = reader.ReadUInt16();
                    playerName = reader.ReadString();
                    color = (PlayerColor) reader.ReadByte();
                }
                catch (Exception ex)
                {
                    WriteEvent("Room Join Failed! Invalid Data received: " + ex.Message + " - " + ex.StackTrace,
                        LogType.Warning);

                    // Return Error 0 for Invalid Data Packages Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte) 0);
                    client.SendMessage(new TagSubjectMessage(RoomTag, JoinFailed, writer), SendMode.Reliable);
                    return;
                }

                var room = _roomList[roomId];
                var newPlayer = new Player(client.GlobalID, playerName, false, color);

                // Check if player already is in an active room -> Send error 1
                if (_playersInRooms.ContainsKey(client.GlobalID))
                {
                    var writer = new DarkRiftWriter();
                    writer.Write((byte) 1);

                    client.SendMessage(new TagSubjectMessage(RoomTag, JoinFailed, writer), SendMode.Reliable);
                }

                if (room.AddPlayer(newPlayer, client))
                {
                    // Generate new color if requested one is taken
                    if (room.PlayerList.Any(p => p.Color == color))
                    {
                        byte i = 0;
                        while (true)
                        {
                            if (room.PlayerList.All(p => p.Color != (PlayerColor) i))
                            {
                                newPlayer.SetNewColor((PlayerColor) i);
                                break;
                            }
                        }
                    }

                    var writer = new DarkRiftWriter();
                    foreach (var player in room.PlayerList)
                    {
                        writer.Write(player);
                    }
                    client.SendMessage(new TagSubjectMessage(RoomTag, JoinSuccess, writer), SendMode.Reliable);

                    // Let the other clients know
                    writer = new DarkRiftWriter();
                    writer.Write(newPlayer);

                    foreach (var cl in room.Clients.Where(c => c.GlobalID != client.GlobalID))
                    {
                        cl.SendMessage(new TagSubjectMessage(RoomTag, PlayerJoined, writer), SendMode.Reliable);
                    }
                }
                // Room full or has started -> Send error 2
                else
                {
                    var writer = new DarkRiftWriter();
                    writer.Write((byte) 2);

                    client.SendMessage(new TagSubjectMessage(RoomTag, JoinFailed, writer), SendMode.Reliable);
                }
                // Try to join room
            }
            
            // Leave Room Request
            if (message.Subject == Leave)
            {
                var id = client.GlobalID;
                var room = _playersInRooms[id]; 
                room.RemovePlayer(id);
                _playersInRooms.Remove(id);

                // Let all clients know
                var writer = new DarkRiftWriter();
                writer.Write(id);
                foreach (var cl in room.Clients)
                {
                    cl.SendMessage(new TagSubjectMessage(RoomTag, LeaveSuccess, writer), SendMode.Reliable);
                }
            }

            // Change Color Request
            if (message.Subject == ChangeColor)
            {
                ushort roomId;
                PlayerColor color;

                try
                {
                    var reader = message.GetReader();
                    roomId = reader.ReadUInt16();
                    color = (PlayerColor) reader.ReadByte();
                }
                catch (Exception ex)
                {
                    WriteEvent("Change Color Failed! Invalid Data received: " + ex.Message + " - " + ex.StackTrace,
                        LogType.Warning);

                    // Return Error 0 for Invalid Data Packages Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte) 0);
                    client.SendMessage(new TagSubjectMessage(RoomTag, ChangeColorFailed, writer), SendMode.Reliable);
                    return;
                }

                var room = _roomList[roomId];
                if (room.PlayerList.Any(p => p.Color == color))
                {
                    // Color already taken -> Send error 1
                    var writer = new DarkRiftWriter();
                    writer.Write((byte) 1);
                    client.SendMessage(new TagSubjectMessage(RoomTag, ChangeColorFailed, writer), SendMode.Reliable);
                }
                else
                {
                    room.PlayerList.Find(p => p.Id == client.GlobalID).SetNewColor(color);

                    // Let every other clients know
                    var writer = new DarkRiftWriter();
                    writer.Write(client.GlobalID);
                    writer.Write((byte) color);

                    foreach (var cl in room.Clients)
                    {
                        cl.SendMessage(new TagSubjectMessage(RoomTag, ChangeColorSuccess, writer), SendMode.Reliable);
                    }
                }
            }

            if (message.Subject == GetOpenRooms)
            {
                if (!_loginPlugin.UsersLoggedIn.ContainsKey(client.GlobalID))
                {
                    client.SendMessage(new TagSubjectMessage(RoomTag, GetOpenRoomsFailed, new DarkRiftWriter()), SendMode.Reliable);
                    return;
                }

                var availableRooms = _roomList.Values.Where(r => r.IsVisible && !r.HasStarted).ToList();
                var writer = new DarkRiftWriter();
                foreach (var room in availableRooms)
                {
                    writer.Write(room);
                }
                client.SendMessage(new TagSubjectMessage(RoomTag, GetOpenRooms, writer), SendMode.Reliable);
            }
        }

        private ushort GenerateRoomId()
        {
            ushort i = 0;
            while (true)
            {
                if (!_roomList.ContainsKey(i))
                {
                    return i;
                }

                i++;
            }
        }
    }

    internal enum GameType : byte
    {
        Arena,
        Runling
    }

    internal enum PlayerColor : byte
    {
        Green,
        Red,
        Blue
    }
}