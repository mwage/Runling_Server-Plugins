using DarkRift;
using DarkRift.Server;
using LoginPlugin;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RoomSystemPlugin
{
    public class RoomSystem : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => false;
        public override Command[] Commands => new[]
        {
            new Command("Rooms", "Shows all rooms", "", GetRoomsCommand)
        };


        public ConcurrentDictionary<ushort, Room> RoomList { get; } = new ConcurrentDictionary<ushort, Room>();

        // Tag
        private const byte RoomTag = 3;
        private const ushort Shift = RoomTag * Login.TagsPerPlugin;

        // Subjects
        private const ushort Create = 0 + Shift;
        private const ushort Join = 1 + Shift;
        private const ushort Leave = 2 + Shift;
        private const ushort GetOpenRooms = 3 + Shift;
        private const ushort GetOpenRoomsFailed = 4 + Shift;
        private const ushort CreateFailed = 5 + Shift;
        private const ushort CreateSuccess = 6 + Shift;
        private const ushort JoinFailed = 7 + Shift;
        private const ushort JoinSuccess = 8 + Shift;
        private const ushort PlayerJoined = 9 + Shift;
        private const ushort LeaveSuccess = 10 + Shift;
        private const ushort PlayerLeft = 11 + Shift;
        private const ushort ChangeColor = 12 + Shift;
        private const ushort ChangeColorSuccess = 13 + Shift;
        private const ushort ChangeColorFailed = 14 + Shift;
        private const ushort StartGame = 15 + Shift;
        private const ushort StartGameSuccess = 16 + Shift;
        private const ushort StartGameFailed = 17 + Shift;
        private const ushort ServerReady = 18 + Shift;
        
        private const string ConfigPath = @"Plugins/RoomSystem.xml";
        private static readonly object InitializeLock = new object();
        private readonly ConcurrentDictionary<ushort, Room> _playersInRooms = new ConcurrentDictionary<ushort, Room>();
        private bool _debug = true;
        private Login _loginPlugin;
        private GameServer _gameServerPlugin;

        public RoomSystem(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            LoadConfig();
            ClientManager.ClientConnected += OnPlayerConnected;
            ClientManager.ClientDisconnected += OnPlayerDisconnected;
        }

        private void LoadConfig()
        {
            XDocument document;

            if (!File.Exists(ConfigPath))
            {
                document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                    new XComment("Settings for the RoomSystem Plugin"),
                    new XElement("Variables", new XAttribute("Debug", true))
                );
                try
                {
                    document.Save(ConfigPath);
                    WriteEvent("Created /Plugins/RoomSystem.xml!", LogType.Info);
                }
                catch (Exception ex)
                {
                    WriteEvent("Failed to create RoomSystem.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
                }
            }
            else
            {
                try
                {
                    document = XDocument.Load(ConfigPath);
                    _debug = document.Element("Variables").Attribute("Debug").Value == "true";
                }
                catch (Exception ex)
                {
                    WriteEvent("Failed to load Login.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
                }
            }
        }

        private void OnPlayerConnected(object sender, ClientConnectedEventArgs e)
        {
            // If you have DR2 Pro, use the Loaded() method instead and spare yourself the locks
            if (_loginPlugin == null)
            {
                lock (InitializeLock)
                {
                    if (_loginPlugin == null)
                    {
                        _loginPlugin = PluginManager.GetPluginByType<Login>();
                        _gameServerPlugin = PluginManager.GetPluginByType<GameServer>();
                    }
                }
            }

            e.Client.MessageReceived += OnMessageReceived;
        }

        private void OnPlayerDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            LeaveRoom(e.Client);
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (var message = e.GetMessage())
            {
                // Check if message is meant for this plugin
                if (message.Tag < Login.TagsPerPlugin * RoomTag || message.Tag >= Login.TagsPerPlugin * (RoomTag + 1))
                    return;

                var client = e.Client;
                switch (message.Tag)
                {
                    case Create:
                    {
                        // If player isn't logged in -> return error 1
                        if (!_loginPlugin.PlayerLoggedIn(client, CreateFailed, "Create Room failed."))
                            return;

                        string roomName;
                        GameType gameMode;
                        PlayerColor color;
                        bool isVisible;

                        try
                        {
                            using (var reader = message.GetReader())
                            {
                                roomName = reader.ReadString();
                                gameMode = (GameType) reader.ReadByte();
                                isVisible = reader.ReadBoolean();
                                color = (PlayerColor) reader.ReadByte();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Return Error 0 for Invalid Data Packages Recieved
                            _loginPlugin.InvalidData(client, CreateFailed, ex, "Room Create Failed!");
                            return;
                        }

                        roomName = AdjustRoomName(roomName, _loginPlugin.UsersLoggedIn[client]);
                        var roomId = GenerateRoomId();

                        var room = new Room(roomId, roomName, gameMode, isVisible);
                        var player = new Player(client.ID, _loginPlugin.UsersLoggedIn[client], true, color);
                        room.AddPlayer(player, client);
                        RoomList[roomId] = room;
                        _playersInRooms[client.ID] = room;

                        using (var writer = DarkRiftWriter.Create())
                        {
                            writer.Write(room);
                            writer.Write(player);

                            using (var msg = Message.Create(CreateSuccess, writer))
                            {
                                client.SendMessage(msg, SendMode.Reliable);
                            }
                        }

                        if (_debug)
                        {
                            WriteEvent("Creating Room " + roomId + ": " + room.Name, LogType.Info);
                        }
                        break;
                    }
                    case Join:
                    {
                        // If player isn't logged in -> return error 1
                        if (!_loginPlugin.PlayerLoggedIn(client, JoinFailed, "Join Room failed."))
                            return;

                        ushort roomId;
                        PlayerColor color;

                        try
                        {
                            using (var reader = message.GetReader())
                            {
                                roomId = reader.ReadUInt16();
                                color = (PlayerColor) reader.ReadByte();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Return Error 0 for Invalid Data Packages Recieved
                            _loginPlugin.InvalidData(client, JoinFailed, ex, "Room Join Failed! ");
                            return;
                        }

                        if (!RoomList.ContainsKey(roomId))
                        {
                            // Return Error 3 for Room doesn't exist anymore
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write((byte)3);

                                using (var msg = Message.Create(JoinFailed, writer))
                                {
                                    client.SendMessage(msg, SendMode.Reliable);
                                }
                            }

                            if (_debug)
                            {
                                WriteEvent("Room Join Failed! Room " + roomId + " doesn't exist anymore", LogType.Info);
                            }

                            return;
                        }
                        var room = RoomList[roomId];
                        var newPlayer = new Player(client.ID, _loginPlugin.UsersLoggedIn[client], false, color);

                        // Check if player already is in an active room -> Send error 2
                        if (_playersInRooms.ContainsKey(client.ID))
                        {
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write((byte)2);

                                using (var msg = Message.Create(JoinFailed, writer))
                                {
                                    client.SendMessage(msg, SendMode.Reliable);
                                }
                            }

                            if (_debug)
                            {
                                WriteEvent("User " + client.ID + " couldn't join Room " + room.Id +
                                           ", since he already is in Room: " + _playersInRooms[client.ID], LogType.Info);
                            }
                            return;
                        }

                        // Try to join room
                        if (room.AddPlayer(newPlayer, client))
                        {
                            // Generate new color if requested one is taken
                            if (room.PlayerList.Exists(p => p.Color == color))
                            {
                                byte i = 0;
                                while (room.PlayerList.Exists(p => p.Color == (PlayerColor) i))
                                {
                                    i++;
                                }
                                newPlayer.SetNewColor((PlayerColor) i);
                            }

                            _playersInRooms[client.ID] = room;

                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write(room);

                                foreach (var player in room.PlayerList)
                                {
                                    writer.Write(player);
                                }

                                using (var msg = Message.Create(JoinSuccess, writer))
                                {
                                    client.SendMessage(msg, SendMode.Reliable);
                                }
                            }

                            // Let the other clients know
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write(newPlayer);

                                using (var msg = Message.Create(PlayerJoined, writer))
                                {
                                    foreach (var cl in room.Clients.Where(c => c.ID != client.ID))
                                    {
                                        cl.SendMessage(msg, SendMode.Reliable);
                                    }
                                }
                            }


                            if (_debug)
                            {
                                WriteEvent("User " + client.ID + " joined Room " + room.Id, LogType.Info);
                            }
                        }
                        // Room full or has started -> Send error 2
                        else
                        {
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write((byte) 2);

                                using (var msg = Message.Create(JoinFailed, writer))
                                {
                                    client.SendMessage(msg, SendMode.Reliable);
                                }
                            }

                            if (_debug)
                            {
                                WriteEvent(
                                    "User " + client.ID + " couldn't join, since Room " + room.Id +
                                    " was either full or had started!", LogType.Info);
                            }
                        }
                        break;
                    }
                    case Leave:
                    {
                        LeaveRoom(client);
                        break;
                    }
                    case ChangeColor:
                    {
                        ushort roomId;
                        PlayerColor color;

                        try
                        {
                            using (var reader = message.GetReader())
                            {
                                roomId = reader.ReadUInt16();
                                color = (PlayerColor) reader.ReadByte();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Return Error 0 for Invalid Data Packages Recieved
                            _loginPlugin.InvalidData(client, ChangeColorFailed, ex, "Change Color Failed! ");
                            return;
                        }

                        var room = RoomList[roomId];
                        if (room.PlayerList.Any(p => p.Color == color))
                        {
                            // Color already taken -> Send error 1
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write((byte)1);

                                using (var msg = Message.Create(ChangeColorFailed, writer))
                                {
                                    client.SendMessage(msg, SendMode.Reliable);
                                }
                            }

                            if (_debug)
                            {
                                WriteEvent("User " + client.ID + " couldn't change color because it was already taken.", LogType.Info);
                            }
                        }
                        else
                        {
                            room.PlayerList.Find(p => p.Id == client.ID).SetNewColor(color);

                            // Let every other clients know
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write(client.ID);
                                writer.Write((byte) color);

                                using (var msg = Message.Create(ChangeColorSuccess, writer))
                                {
                                    foreach (var cl in room.Clients)
                                    {
                                        cl.SendMessage(msg, SendMode.Reliable);
                                    }
                                }
                            }

                            if (_debug)
                            {
                                WriteEvent("User " + client.ID + " successfully changed his color!", LogType.Info);
                            }
                        }
                        break;
                    }
                    case GetOpenRooms:
                    {
                        // If player isn't logged in -> return error 1
                        if (!_loginPlugin.PlayerLoggedIn(client, GetOpenRoomsFailed, "GetRoomRequest failed."))
                            return;

                        // If he is, send back all available rooms
                        var availableRooms = RoomList.Values.Where(r => r.IsVisible && !r.HasStarted).ToList();
                        using (var writer = DarkRiftWriter.Create())
                        {
                            foreach (var room in availableRooms)
                            {
                                writer.Write(room);
                            }

                            using (var msg = Message.Create(GetOpenRooms, writer))
                            {
                                client.SendMessage(msg, SendMode.Reliable);
                            }
                        }
                        break;
                    }
                    case StartGame:
                    {
                        // If player isn't logged in -> return error 1
                        if (!_loginPlugin.PlayerLoggedIn(client, GetOpenRoomsFailed, "Start Game request failed."))
                            return;

                        ushort roomId;

                        try
                        {
                            using (var reader = message.GetReader())
                            {
                                roomId = reader.ReadUInt16();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Return Error 0 for Invalid Data Packages Recieved
                            _loginPlugin.InvalidData(client, StartGameFailed, ex, "Room Join Failed! ");
                            return;
                        }

                        var username = _loginPlugin.UsersLoggedIn[client];
                        var player = RoomList[roomId].PlayerList.FirstOrDefault(p => p.Name == username);
                        if (player == null || !player.IsHost)
                        {
                            // Player isn't host of this room -> return error 2
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write((byte)2);

                                using (var msg = Message.Create(StartGameFailed, writer))
                                {
                                    client.SendMessage(msg, SendMode.Reliable);
                                }
                            }

                            if (_debug)
                            {
                                WriteEvent("User " + client.ID + " couldn't start the game, since he wasn't a host!",
                                    LogType.Warning);
                            }
                            return;
                        }

                        // Prepare Gameserver
                        var gameServer = _gameServerPlugin.GameServers.Values.FirstOrDefault(s => s.IsAvailable);
                        if (gameServer == null)
                        {
                            // No GameServer available -> return error 3
                            using (var writer = DarkRiftWriter.Create())
                            {
                                writer.Write((byte)3);

                                using (var msg = Message.Create(StartGameFailed, writer))
                                {
                                    client.SendMessage(msg, SendMode.Reliable);
                                }
                            }

                            if (_debug)
                            {
                                WriteEvent("Failed to start game, no game-server available!", LogType.Warning);
                            }
                            return;
                        }

                        RoomList[roomId].HasStarted = true;
                        _gameServerPlugin.StartGame(RoomList[roomId], gameServer);


                        using (var writer = DarkRiftWriter.Create())
                        {
                            writer.Write(gameServer.Port);

                            using (var msg = Message.Create(StartGameSuccess, writer))
                            {
                                foreach (var cl in RoomList[roomId].Clients)
                                {
                                    cl.SendMessage(msg, SendMode.Reliable);
                                }
                            }
                        }
                        break;
                    }
                }

            }
        }

        public void LoadGame(Room room)
        {
            using (var msg = Message.CreateEmpty(ServerReady))
            {
                foreach (var client in room.Clients)
                {
                    client.SendMessage(msg, SendMode.Reliable);
                }
            }
        }

        private ushort GenerateRoomId()
        {
            ushort i = 0;
            while (true)
            {
                if (!RoomList.ContainsKey(i))
                {
                    return i;
                }

                i++;
            }
        }

        private static string AdjustRoomName(string roomName, string playerName)
        {
            if (roomName == "")
            {
                return  playerName + "'s Lobby";
            }

            return roomName;
        }

        private void LeaveRoom(IClient client)
        {
            var id = client.ID;
            if (!_playersInRooms.ContainsKey(id))
                return;

            var room = _playersInRooms[id];
            var leaverName = room.PlayerList.FirstOrDefault(p => p.Id == client.ID)?.Name;
            _playersInRooms.TryRemove(id, out _);

            if (room.RemovePlayer(client))
            {
                // Only message user if he's still connected (would cause error if LeaveRoom is called from Disconnect otherwise)
                if (client.IsConnected)
                {
                    using (var msg = Message.CreateEmpty(LeaveSuccess))
                    {
                        client.SendMessage(msg, SendMode.Reliable);
                    }
                }

                // Remove room if it's empty
                if (room.PlayerList.Count == 0)
                {
                    RoomList.TryRemove(RoomList.FirstOrDefault(r => r.Value == room).Key, out _);
                    if (_debug)
                    {
                        WriteEvent("Room " + room.Id + " deleted!", LogType.Info);
                    }
                }
                // otherwise set a new host and let other players know
                else
                {
                    var newHost = room.PlayerList.First();
                    newHost.SetHost(true);

                    using (var writer = DarkRiftWriter.Create())
                    {
                        writer.Write(id);
                        writer.Write(newHost.Id);
                        writer.Write(leaverName);

                        using (var msg = Message.Create(PlayerLeft, writer))
                        {
                            foreach (var cl in room.Clients)
                            {
                                cl.SendMessage(msg, SendMode.Reliable);
                            }
                        }
                    }
                }

                if (_debug)
                {
                    WriteEvent("User " + client.ID + " left Room: " + room.Name, LogType.Info);
                }
            }
            else
            {
                WriteEvent("Tried to remove player who wasn't in the room anymore.", LogType.Warning);
            }
        }

        private void GetRoomsCommand(object sender, CommandEventArgs e)
        {
            WriteEvent("Active Rooms:", LogType.Info);
            var rooms = RoomList.Values.ToList();
            foreach (var room in rooms)
            {
                WriteEvent(room.Name + " [" + room.Id + "] - " + room.PlayerList.Count + "/" + room.MaxPlayers, LogType.Info);
            }
        }
    }

    public enum GameType : byte
    {
        Arena,
        Runling
    }

    public enum PlayerColor : byte
    {
        Green,
        Red,
        Blue
    }
}