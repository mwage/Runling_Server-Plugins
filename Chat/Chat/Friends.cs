using System;
using System.IO;
using System.Xml.Linq;
using DarkRift;
using DarkRift.Server;
using DbConnectorPlugin;
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
        private const ushort Keys = 0;

        private const string ConfigPath = @"Plugins\Friends.xml";
        private DbConnector _dbConnector;
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

            // Login Request
            if (message.Subject == Keys)
            {
            }
        }

        private void AddFriend(string username, string friend)
        {
            var update = Builders<User>.Update.AddToSet(u => u.Friends, friend);
            _dbConnector.Users.UpdateOne(u => u.Username == username, update);

            if (_debug)
            {
                WriteEvent("Added " + friend + " as a friend of " + username, LogType.Info);
            }
        }


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
                AddFriend(username, friend);
            }
            catch (Exception ex)
            {
                WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
            }
        }

        #region Commands
        private void DelFriendCommand(object sender, CommandEventArgs e)
        {
            if (_dbConnector == null)
            {
                _dbConnector = PluginManager.GetPluginByType<DbConnector>();
            }

            var username = e.Arguments[0];
            var friend = e.Arguments[1];

            try
            {
                var update = Builders<User>.Update.Pull(u => u.Friends, friend);
                _dbConnector.Users.UpdateOne(u => u.Username == username, update);

                WriteEvent("Removed friend " + friend + " from " + username, LogType.Info);
            }
            catch (Exception ex)
            {
                WriteEvent("Database Error: " + ex.Message + " - " + ex.StackTrace, LogType.Error);
            }
        }
        #endregion
    }
}
