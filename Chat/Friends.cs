using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DarkRift;
using DarkRift.Server;
using DbConnectorPlugin;
using LoginPlugin;
using MongoDB.Driver;

namespace ChatPlugin
{
    public class Friends : Plugin
    {

        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => false;

        public override Command[] Commands => new[]
        {
            new Command("AddFriend", "Adds a User to the Database [AddFriend name friend]", "", AddFriendCommand),
            new Command("DelFriend", "Deletes a User from the Database [DelFriend name friend]", "", DelFriendCommand)
        };

        // Tag
        private const byte FriendsTag = 2;

        // Subjects
        private const ushort FriendRequest = 0;
        private const ushort RequestFailed = 1;
        private const ushort RequestSuccess = 2;
        private const ushort AcceptRequest = 3;
        private const ushort AcceptRequestSuccess = 4;
        private const ushort AcceptRequestFailed = 5;
        private const ushort DeclineRequest = 6;
        private const ushort DeclineRequestSuccess = 7;
        private const ushort DeclineRequestFailed = 8;
        private const ushort RemoveFriend = 9;
        private const ushort RemoveFriendSuccess = 10;
        private const ushort RemoveFriendFailed = 11;


        private const string ConfigPath = @"Plugins\Friends.xml";
        private DbConnector _dbConnector;
        private Login _loginPlugin;
        private bool _debug = true;

        public Friends(PluginLoadData pluginLoadData) : base(pluginLoadData)
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
                    new XComment("Settings for the Friends Plugin"),
                    new XElement("Variables", new XAttribute("Debug", true))
                );
                try
                {
                    document.Save(ConfigPath);
                    WriteEvent("Created /Plugins/Friends.xml!", LogType.Warning);
                }
                catch (Exception ex)
                {
                    WriteEvent("Failed to create Friends.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
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
                    WriteEvent("Failed to load Friends.xml: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
                }
            }
        }

        private void OnPlayerConnected(object sender, ClientConnectedEventArgs e)
        {
            e.Client.MessageReceived += OnMessageReceived;

            // If you have DR2 Pro, use the Plugin.Loaded() method to get the DbConnector Plugin instead
            if (_dbConnector == null)
            {
                _dbConnector = PluginManager.GetPluginByType<DbConnector>();
                _loginPlugin = PluginManager.GetPluginByType<Login>();
            }
        }

        private void OnPlayerDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!(e.Message is TagSubjectMessage message) || message.Tag != FriendsTag)
                return;

            var client = (Client) sender;

            // Friend Request
            if (message.Subject == FriendRequest)
            {
                if (!_loginPlugin.UsersLoggedIn.ContainsKey(client))
                {
                    // If player isn't logged in -> return error 1
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)1);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, RequestFailed, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent("FriendRequest failed. Player wasn't logged in.", LogType.Warning);
                    }
                    return;
                }

                var senderName = _loginPlugin.UsersLoggedIn[client];
                string receiver;

                try
                {
                    var reader = message.GetReader();
                    receiver = reader.ReadString();
                }
                catch (Exception ex)
                {
                    WriteEvent("Invalid Friend Request received: " + ex.Message + " - " + ex.StackTrace, LogType.Warning);

                    // Return Error 0 for Invalid Data Packages Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)0);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, RequestFailed, writer), SendMode.Reliable);
                    return;
                }

                try
                {
                    // Save the request in the database to both users
                    AddRequests(senderName, receiver);
                    
                    var writer = new DarkRiftWriter();
                    writer.Write(receiver);

                    client.SendMessage(new TagSubjectMessage(FriendsTag, RequestSuccess, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent(senderName + " wants to add " + " as a friend!", LogType.Info);
                    }

                    // If Receiver is currently logged in, let him know right away
                    if (_loginPlugin.UsersLoggedIn.ContainsValue(receiver))
                    {
                        var receivingClient = _loginPlugin.UsersLoggedIn.FirstOrDefault(u => u.Value == receiver).Key;
                        var wr = new DarkRiftWriter();
                        wr.Write(senderName);

                        receivingClient.SendMessage(new TagSubjectMessage(FriendsTag, FriendRequest, wr), SendMode.Reliable);
                    }
                }
                catch (Exception ex)
                {
                    WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);

                    // Return Error 2 for Database error
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, RequestFailed, writer), SendMode.Reliable);
                }
            }

            // Friend Request Declined
            if (message.Subject == DeclineRequest)
            {
                if (!_loginPlugin.UsersLoggedIn.ContainsKey(client))
                {
                    // If player isn't logged in -> return error 1
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)1);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, DeclineRequestFailed, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent("DeclineFriendRequest failed. Player wasn't logged in.", LogType.Warning);
                    }
                    return;
                }

                var senderName = _loginPlugin.UsersLoggedIn[client];
                string receiver;

                try
                {
                    var reader = message.GetReader();
                    receiver = reader.ReadString();
                }
                catch (Exception ex)
                {
                    WriteEvent("Invalid Decline Friend Request data received: " + ex.Message + " - " + ex.StackTrace, LogType.Warning);

                    // Return Error 0 for Invalid Data Packages Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)0);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, DeclineRequestFailed, writer), SendMode.Reliable);
                    return;
                }

                try
                {
                    // Delete the request from the database for both users
                    RemoveRequests(senderName, receiver);

                    var writer = new DarkRiftWriter();
                    writer.Write(receiver);

                    client.SendMessage(new TagSubjectMessage(FriendsTag, DeclineRequestSuccess, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent(senderName + " declined " + receiver + "'s friend request.", LogType.Info);
                    }

                    // If Receiver is currently logged in, let him know right away
                    if (_loginPlugin.UsersLoggedIn.ContainsValue(receiver))
                    {
                        var receivingClient = _loginPlugin.UsersLoggedIn.FirstOrDefault(u => u.Value == receiver).Key;
                        var wr = new DarkRiftWriter();
                        wr.Write(senderName);

                        receivingClient.SendMessage(new TagSubjectMessage(FriendsTag, DeclineRequest, wr), SendMode.Reliable);
                    }
                }
                catch (Exception ex)
                {
                    WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);

                    // Return Error 2 for Database error
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, DeclineRequestFailed, writer), SendMode.Reliable);
                }
            }

            // Friend Request Accepted
            if (message.Subject == AcceptRequest)
            {
                if (!_loginPlugin.UsersLoggedIn.ContainsKey(client))
                {
                    // If player isn't logged in -> return error 1
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)1);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, AcceptRequestFailed, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent("AcceptFriendRequest failed. Player wasn't logged in.", LogType.Warning);
                    }
                    return;
                }

                var senderName = _loginPlugin.UsersLoggedIn[client];
                string receiver;

                try
                {
                    var reader = message.GetReader();
                    receiver = reader.ReadString();
                }
                catch (Exception ex)
                {
                    WriteEvent("Invalid Accept Friend Request data received: " + ex.Message + " - " + ex.StackTrace, LogType.Warning);

                    // Return Error 0 for Invalid Data Packages Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)0);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, AcceptRequestFailed, writer), SendMode.Reliable);
                    return;
                }

                try
                {
                    // Delete the request from the database for both users and add their names to their friend list
                    RemoveRequests(senderName, receiver);
                    AddFriends(senderName, receiver);
                    
                    var writer = new DarkRiftWriter();
                    writer.Write(receiver);

                    client.SendMessage(new TagSubjectMessage(FriendsTag, AcceptRequestSuccess, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent(senderName + " accepted " + receiver + "'s friend request.", LogType.Info);
                    }

                    // If Receiver is currently logged in, let him know right away
                    if (_loginPlugin.UsersLoggedIn.ContainsValue(receiver))
                    {
                        var receivingClient = _loginPlugin.UsersLoggedIn.FirstOrDefault(u => u.Value == receiver).Key;
                        var wr = new DarkRiftWriter();
                        wr.Write(senderName);

                        receivingClient.SendMessage(new TagSubjectMessage(FriendsTag, AcceptRequest, wr), SendMode.Reliable);
                    }
                }
                catch (Exception ex)
                {
                    WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);

                    // Return Error 2 for Database error
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, AcceptRequestFailed, writer), SendMode.Reliable);
                }
            }

            // Remove Friend
            if (message.Subject == RemoveFriend)
            {
                if (!_loginPlugin.UsersLoggedIn.ContainsKey(client))
                {
                    // If player isn't logged in -> return error 1
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)1);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, RemoveFriendFailed, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent("RemoveFriend failed. Player wasn't logged in.", LogType.Warning);
                    }
                    return;
                }


                var senderName = _loginPlugin.UsersLoggedIn[client];
                string receiver;

                try
                {
                    var reader = message.GetReader();
                    receiver = reader.ReadString();
                }
                catch (Exception ex)
                {
                    WriteEvent("Invalid Remove Friend data received: " + ex.Message + " - " + ex.StackTrace, LogType.Warning);

                    // Return Error 0 for Invalid Data Packages Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)0);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, RemoveFriendFailed, writer), SendMode.Reliable);
                    return;
                }

                try
                {
                    // Delete the names from the friendlist in the database for both users
                    RemoveFriends(senderName, receiver);

                    var writer = new DarkRiftWriter();
                    writer.Write(receiver);

                    client.SendMessage(new TagSubjectMessage(FriendsTag, RemoveFriendSuccess, writer), SendMode.Reliable);

                    if (_debug)
                    {
                        WriteEvent(senderName + " removed " + receiver + " as a friend.", LogType.Info);
                    }

                    // If Receiver is currently logged in, let him know right away
                    if (_loginPlugin.UsersLoggedIn.ContainsValue(receiver))
                    {
                        var receivingClient = _loginPlugin.UsersLoggedIn.FirstOrDefault(u => u.Value == receiver).Key;
                        var wr = new DarkRiftWriter();
                        wr.Write(senderName);

                        receivingClient.SendMessage(new TagSubjectMessage(FriendsTag, RemoveFriend, wr), SendMode.Reliable);
                    }
                }
                catch (Exception ex)
                {
                    WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);

                    // Return Error 2 for Database error
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(FriendsTag, RemoveFriendFailed, writer), SendMode.Reliable);
                }
            }
        }

        #region DbHelpers

        private void AddRequests(string sender, string receiver)
        {
            var updateReceiving = Builders<User>.Update.AddToSet(u => u.OpenFriendRequests, sender);
            _dbConnector.Users.UpdateOne(u => u.Username == receiver, updateReceiving);
            var updateSending = Builders<User>.Update.AddToSet(u => u.OpenFriendRequests, receiver);
            _dbConnector.Users.UpdateOne(u => u.Username == sender, updateSending);
        }

        private void RemoveRequests(string sender, string receiver)
        {
            var updateSender = Builders<User>.Update.Pull(u => u.OpenFriendRequests, receiver);
            _dbConnector.Users.UpdateOne(u => u.Username == sender, updateSender);
            var updateReceiver = Builders<User>.Update.Pull(u => u.OpenFriendRequests, receiver);
            _dbConnector.Users.UpdateOne(u => u.Username == receiver, updateReceiver);
        }

        private void AddFriends(string sender, string receiver)
        {
            var updateReceiving = Builders<User>.Update.AddToSet(u => u.Friends, sender);
            _dbConnector.Users.UpdateOne(u => u.Username == receiver, updateReceiving);
            var updateSending = Builders<User>.Update.AddToSet(u => u.Friends, receiver);
            _dbConnector.Users.UpdateOne(u => u.Username == sender, updateSending);
        }

        private void RemoveFriends(string sender, string receiver)
        {
            var updateSender = Builders<User>.Update.Pull(u => u.Friends, receiver);
            _dbConnector.Users.UpdateOne(u => u.Username == sender, updateSender);
            var updateReceiver = Builders<User>.Update.Pull(u => u.Friends, sender);
            _dbConnector.Users.UpdateOne(u => u.Username == receiver, updateReceiver);
        }

        #endregion
        
        #region Commands

        private void AddFriendCommand(object sender, CommandEventArgs e)
        {
            if (_dbConnector == null)
            {
                _dbConnector = PluginManager.GetPluginByType<DbConnector>();
            }

            if (e.Arguments.Length != 2)
            {
                WriteEvent("Invalid arguments. Enter [AddFríend name friend].", LogType.Warning);
                return;
            }

            var username = e.Arguments[0];
            var friend = e.Arguments[1];

            try
            {
                AddFriends(username, friend);

                if (_debug)
                {
                    WriteEvent("Added " + friend + " as a friend of " + username, LogType.Info);
                }
            }
            catch (Exception ex)
            {
                WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
            }
        }

        private void DelFriendCommand(object sender, CommandEventArgs e)
        {
            if (_dbConnector == null)
            {
                _dbConnector = PluginManager.GetPluginByType<DbConnector>();
            }

            if (e.Arguments.Length != 2)
            {
                WriteEvent("Invalid arguments. Enter [AddFríend name friend].", LogType.Warning);
                return;
            }

            var username = e.Arguments[0];
            var friend = e.Arguments[1];

            try
            {
                RemoveFriends(username, friend);

                if (_debug)
                {
                    WriteEvent("Removed " + friend + " as a friend of " + username, LogType.Info);
                }
            }
            catch (Exception ex)
            {
                WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
            }
        }
        #endregion
    }
}
