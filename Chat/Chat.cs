using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DarkRift;
using DarkRift.Server;
using LoginPlugin;
using RoomSystemPlugin;

namespace ChatPlugin
{
    public class Chat : Plugin
    {
        public override Version Version => new Version(1,0,0);
        public override bool ThreadSafe => false;

        // Tag
        private const byte ChatTag = 2;

        // Subjects
        private const ushort PrivateMessage = 0;
        private const ushort SuccessfulPrivateMessage = 1;
        private const ushort RoomMessage = 2;
        private const ushort GroupMessage = 3;
        private const ushort MessageFailed = 4;
        private const ushort JoinGroup = 5;
        private const ushort JoinGroupFailed = 6;
        private const ushort LeaveGroup = 7;
        private const ushort LeaveGroupFailed = 8;

        private const string ConfigPath = @"Plugins\Chat.xml";
        private Login _loginPlugin;
        private RoomSystem _roomSystem;
        private bool _debug = true;

        public Dictionary<ushort, ChatGroup> ChatGroups = new Dictionary<ushort, ChatGroup>();
        public Dictionary<string, List<ChatGroup>> ChatGroupsOfPlayer = new Dictionary<string, List<ChatGroup>>();

        public Chat(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            LoadConfig();
            ClientManager.ClientConnected += OnPlayerConnected;
        }

        private void LoadConfig()
        {
            XDocument document;

            if (!File.Exists(ConfigPath))
            {
                document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                    new XComment("Settings for the Chat Plugin"),
                    new XElement("Variables", new XAttribute("Debug", true))
                );
                try
                {
                    document.Save(ConfigPath);
                    WriteEvent("Created /Plugins/Chat.xml!", LogType.Warning);
                }
                catch (Exception ex)
                {
                    WriteEvent("Failed to create Chat.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
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
                    WriteEvent("Failed to load Chat.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
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
                _loginPlugin.onLogout += RemovePlayerFromChatGroups;
            }

            e.Client.MessageReceived += OnMessageReceived;
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!(e.Message is TagSubjectMessage message) || message.Tag != ChatTag)
                return;

            var client = (Client) sender;

            // Private Message
            if (message.Subject == PrivateMessage)
            {
                var senderName = _loginPlugin.UsersLoggedIn[client];

                // If player isn't logged in -> return error 1
                if (!_loginPlugin.PlayerLoggedIn(client, ChatTag, MessageFailed, "Private Message failed."))
                    return;

                string receiver;
                string content;

                try
                {
                    var reader = message.GetReader();
                    receiver = reader.ReadString();
                    content = reader.ReadString();
                }
                catch (Exception ex)
                {
                    // Return Error 0 for Invalid Data Packages Recieved
                    _loginPlugin.InvalidData(client, ChatTag, MessageFailed, ex, "Send Message failed! ");
                    return;
                }

                if (!_loginPlugin.UsersLoggedIn.ContainsValue(receiver))
                {
                    // If receiver isn't logged in -> return error 3
                    var wr = new DarkRiftWriter();
                    wr.Write((byte)3);
                    client.SendMessage(new TagSubjectMessage(ChatTag, MessageFailed, wr), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent("Send Message failed. Receiver wasn't logged in.", LogType.Info);
                    }
                    return;
                }
                
                var receivingClient = _loginPlugin.UsersLoggedIn.First(u => u.Value == receiver).Key;

                var writer = new DarkRiftWriter();
                writer.Write(senderName);
                writer.Write(receiver);
                writer.Write(content);
                client.SendMessage(new TagSubjectMessage(ChatTag, SuccessfulPrivateMessage, writer), SendMode.Reliable);

                writer = new DarkRiftWriter();
                writer.Write(senderName);
                writer.Write(content);
                receivingClient.SendMessage(new TagSubjectMessage(ChatTag, PrivateMessage, writer), SendMode.Reliable);
            }
            // Room Message
            else if (message.Subject == RoomMessage)
            {
                var senderName = _loginPlugin.UsersLoggedIn[client];

                // If player isn't logged in -> return error 1
                if (!_loginPlugin.PlayerLoggedIn(client, ChatTag, MessageFailed, "Group/Room Message failed."))
                    return;

                ushort roomId;
                string content;

                try
                {
                    var reader = message.GetReader();
                    roomId = reader.ReadUInt16();
                    content = reader.ReadString();
                }
                catch (Exception ex)
                {
                    // Return Error 0 for Invalid Data Packages Recieved
                    _loginPlugin.InvalidData(client, ChatTag, MessageFailed, ex, "Send Message failed! ");
                    return;
                }

                var writer = new DarkRiftWriter();
                writer.Write(senderName);
                writer.Write(content);

                if (!_roomSystem.RoomList[roomId].Clients.Contains(client))
                {
                    // If player isn't actually in the room -> return error 2
                    var wr = new DarkRiftWriter();
                    wr.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(ChatTag, MessageFailed, wr), SendMode.Reliable);

                    WriteEvent("Send Message failed. Player wasn't part of the room.", LogType.Warning);
                    return;
                }

                foreach (var cl in _roomSystem.RoomList[roomId].Clients)
                {
                    cl.SendMessage(new TagSubjectMessage(ChatTag, RoomMessage, writer), SendMode.Reliable);
                }
            }
            // ChatGroup Message
            else if (message.Subject == GroupMessage)
            {
                var senderName = _loginPlugin.UsersLoggedIn[client];

                ushort groupId;
                string content;

                try
                {
                    var reader = message.GetReader();
                    groupId = reader.ReadUInt16();
                    content = reader.ReadString();
                }
                catch (Exception ex)
                {
                    // Return Error 0 for Invalid Data Packages Recieved
                    _loginPlugin.InvalidData(client, ChatTag, MessageFailed, ex, "Send Message failed! ");
                    return;
                }

                if (!ChatGroups[groupId].Users.Values.Contains(client))
                {
                    // If player isn't actually in the chatgroup -> return error 2
                    var wr = new DarkRiftWriter();
                    wr.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(ChatTag, MessageFailed, wr), SendMode.Reliable);

                    WriteEvent("Send Message failed. Player wasn't part of the chat group.", LogType.Warning);
                    return;
                }

                var writer = new DarkRiftWriter();
                writer.Write(groupId);
                writer.Write(senderName);
                writer.Write(content);

                foreach (var cl in ChatGroups[groupId].Users.Values)
                {
                    cl.SendMessage(new TagSubjectMessage(ChatTag, GroupMessage, writer), SendMode.Reliable);
                }
            }
        }

        private void RemovePlayerFromChatGroups(string username)
        {
            if (!ChatGroupsOfPlayer.ContainsKey(username))
                return;

            foreach (var chatGroup in ChatGroupsOfPlayer[username])
            {
                chatGroup.RemovePlayer(username);
            }
        }
    }
}
