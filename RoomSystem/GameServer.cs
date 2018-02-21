using DarkRift;
using DarkRift.Server;
using LoginPlugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace RoomSystemPlugin
{
    public class GameServer : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => false;
        public override Command[] Commands => new[]
        {
            new Command("Server", "Shows all game servers", "", GetGameServer)
        };

        // Tag
        private const byte GameServerTag = 4;
        private const ushort Shift = GameServerTag * Login.TagsPerPlugin;

        // Subjects
        private const ushort RegisterServer = 0 + Shift;
        private const ushort ServerAvailable = 1 + Shift;
        private const ushort InitializeGame = 2 + Shift;
        private const ushort ServerReady = 3 + Shift;

        public Dictionary<IClient, Server> GameServers { get; } = new Dictionary<IClient, Server>();
        private readonly List<ushort> _portsInUse = new List<ushort>();
        private const string ConfigPath = @"Plugins\GameServer.xml";
        private Login _loginPlugin;
        private RoomSystem _roomSystem;
        private bool _debug = true;

        public GameServer(PluginLoadData pluginLoadData) : base(pluginLoadData)
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
                    new XComment("Settings for the GameServer Plugin"),
                    new XElement("Variables", new XAttribute("Debug", true))
                );
                try
                {
                    document.Save(ConfigPath);
                    WriteEvent("Created /Plugins/GameServer.xml!", LogType.Warning);
                }
                catch (Exception ex)
                {
                    WriteEvent("Failed to create GameServer.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
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
                    WriteEvent("Failed to load GameServer.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
                }
            }
        }

        private void OnPlayerConnected(object sender, ClientConnectedEventArgs e)
        {
            // If you have DR2 Pro, use the Plugin.Loaded() method instead
            if (_loginPlugin == null)
            {
                _loginPlugin = PluginManager.GetPluginByType<Login>();
                _roomSystem = PluginManager.GetPluginByType<RoomSystem>();
            }

            e.Client.MessageReceived += OnMessageReceived;
        }

        private void OnPlayerDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            RemoveServer(e.Client);
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (var message = e.GetMessage())
            {
                // Check if message is meant for this plugin
                if (message.Tag < Login.TagsPerPlugin * GameServerTag || message.Tag >= Login.TagsPerPlugin * (GameServerTag + 1))
                    return;

                var client = e.Client;
                
                switch (message.Tag)
                {
                    case RegisterServer:
                    {
                        // Find an available port
                        ushort port = 4297;
                        while (_portsInUse.Contains(port))
                        {
                            port++;
                        }
                        _portsInUse.Add(port);
                        GameServers[client] = new Server(port, client);
                        _loginPlugin.Users.Remove(client);

                        using (var writer = DarkRiftWriter.Create())
                        {
                            writer.Write(port);

                            using (var msg = Message.Create(RegisterServer, writer))
                            {
                                client.SendMessage(msg, SendMode.Reliable);
                            }
                        }

                        if (_debug)
                        {
                            WriteEvent("New Server registered at port: " + port, LogType.Info);
                        }
                        break;
                    }
                    case ServerAvailable:
                        GameServers[client].IsAvailable = true;
                        break;
                    case ServerReady:
                        _roomSystem.LoadGame(GameServers[client].Room);
                        break;
                }
            }
        }

        internal void StartGame(Room room, Server server)
        {
            // Set up server for a game
            server.Room = room;
            server.IsAvailable = false;
            using (var writer = DarkRiftWriter.Create())
            {
                writer.Write((byte)room.GameType);

                foreach (var player in room.PlayerList)
                {
                    writer.Write(player);
                }

                using (var msg = Message.Create(InitializeGame, writer))
                {
                    server.Client.SendMessage(msg, SendMode.Reliable);
                }
            }
        }

        private void RemoveServer(IClient client)
        {
            if (!GameServers.ContainsKey(client))
                return;

            _portsInUse.Remove(GameServers[client].Port);
            GameServers.Remove(client);
        }

        private void GetGameServer(object sender, CommandEventArgs e)
        {
            foreach (var server in GameServers.Values)
            {
                WriteEvent("Port: " + server.Port + " - Available: " + server.IsAvailable, LogType.Info);
            }
        }
    }
}
