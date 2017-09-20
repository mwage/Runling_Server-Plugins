using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DarkRift;
using DarkRift.Server;
using DbConnectorPlugin;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace LoginPlugin
{
    public class Login : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => false;
        public override Command[] Commands => new[]
        {
            new Command ("AllowAddUser", "Allow Users to be added to the Database [AllowAddUser on/off]", "", AllowAddUserCommand),
            new Command("AddUser", "Adds a User to the Database [AddUser name password]", "", AddUserCommand),
            new Command("LPDebug", "Enables Plugin Debug", "", DebugCommand),
            new Command("Online", "Logs number of online users", "", UsersLoggedInCommand),
            new Command("LoggedIn", "Logs number of online users", "", UsersOnlineCommand)
        };

        // Tag
        private const byte LoginTag = 0;

        // Subjects
        private const ushort LoginUser = 0;
        private const ushort LogoutUser = 1;
        private const ushort AddUser = 2;
        private const ushort LoginSuccess = 3;
        private const ushort LoginFailed = 4;
        private const ushort LogoutSucces = 5;
        private const ushort AddUserSuccess = 6;
        private const ushort AddUserFailed = 7;

        // Connects the clients Global ID with his username
        public Dictionary<uint, string> UsersLoggedIn = new Dictionary<uint, string>();

        private string _configPath = @"Plugins\Login.xml";
        private DbConnector _dbConnector;
        private bool _allowAddUser = true;
        private bool _debug = true;

        public Login(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            LoadConfig();
            ClientManager.ClientConnected += OnPlayerConnected;
            ClientManager.ClientDisconnected += OnPlayerDisconnected;
        }

        private void LoadConfig()
        {
            XDocument document;

            if (!File.Exists(_configPath))
            {
                document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                    new XComment("Settings for the Login Plugin"),
                    new XElement("Variables", new XAttribute("Debug", true), new XAttribute("AllowAddUser", true))
                    );

                document.Save(_configPath);
                WriteEvent("Created /Plugins/Login.xml!", LogType.Warning);
            }
            else
            {
                try
                {
                    document = XDocument.Load(_configPath);
                    _debug = document.Element("Variables").Attribute("Debug").Value == "true";
                    _allowAddUser = document.Element("Variables").Attribute("AllowAddUser").Value == "true";
                }
                catch (Exception e)
                {
                    WriteEvent("Failed to load Login.xml.", LogType.Error);
                }
            }
        }

        private void OnPlayerConnected(object sender, ClientConnectedEventArgs e)
        {
            UsersLoggedIn[e.Client.GlobalID] = "";
            e.Client.MessageReceived += OnMessageReceived;

            // Substitute for the Plugin.Loaded method from DR2 Pro
            if (_dbConnector == null)
            {
                _dbConnector = PluginManager.GetPluginByType<DbConnector>();
            }
        }

        private void OnPlayerDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            Logout(e.Client.GlobalID);
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!(e.Message is TagSubjectMessage message) || message.Tag != LoginTag)
                return;

            var client = (Client)sender;
            
            // Login Request
            if (message.Subject == LoginUser)
            {
                // Make sure user isn't already logged in
                if (UsersLoggedIn[client.GlobalID] != "")
                    return;

                var reader = message.GetReader();

                string username;
                string password;

                try
                {
                    username = reader.ReadString();
                    password = reader.ReadString();
                }
                catch (Exception exception)
                {
                    WriteEvent("LoginPlugin: Invalid Login data received! - " + exception, LogType.Warning);

                    // Return Error 0 for Invalid Data Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)0);
                    client.SendMessage(new TagSubjectMessage(LoginTag, LoginFailed, writer), SendMode.Reliable);
                    return;
                }
                
                try
                {
                    var user = _dbConnector.Users.AsQueryable().FirstOrDefault(u => u.Username == username);
                    if (user != null && BCrypt.Net.BCrypt.Verify(password, user.Password))
                    {
                        UsersLoggedIn[client.GlobalID] = username;

                        var writer = new DarkRiftWriter();
                        writer.Write(client.GlobalID);
                        client.SendMessage(new TagSubjectMessage(LoginTag, LoginSuccess, writer), SendMode.Reliable);

                        if (_debug)
                        {
                            WriteEvent("Successful login (" + client.GlobalID + ").", LogType.Info);
                        }
                    }
                    else
                    {
                        if (_debug)
                        {
                            WriteEvent("User " + client.GlobalID + " couldn't log in!", LogType.Info);
                        }

                        // Return Error 1 for "Wrong username/password combination"
                        var writer = new DarkRiftWriter();
                        writer.Write((byte) 1);
                        client.SendMessage(new TagSubjectMessage(LoginTag, LoginFailed, writer), SendMode.Reliable);
                    }
                }
                catch (Exception exception)
                {
                    _dbConnector.LogException(exception, "LoginPlugin: Login");

                    // Return Error 2 for Database error
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(LoginTag, LoginFailed, writer), SendMode.Reliable);
                }
            }

            // Logout Request
            if (message.Subject == LogoutUser)
            {
                Logout(client.GlobalID);
                client.SendMessage(new TagSubjectMessage(LoginTag, LogoutSucces, new DarkRiftWriter()), SendMode.Reliable);
            }

            // Registration Request
            if (message.Subject == AddUser)
            {
                if (!_allowAddUser)
                    return;

                var reader = message.GetReader();

                string username;
                string password;
                
                try
                {
                    username = reader.ReadString();
                    password = BCrypt.Net.BCrypt.HashPassword(reader.ReadString(), 10);
                }
                catch (Exception exception)
                {
                    WriteEvent("LoginPlugin: Invalid AddUser data received! - " + exception, LogType.Warning);

                    // Return Error 0 for Invalid Data Recieved
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)0);
                    client.SendMessage(new TagSubjectMessage(LoginTag, AddUserFailed, writer), SendMode.Reliable);
                    return;
                }

                try
                {
                    if (UsernameAvailable(username))
                    {
                        AddNewUser(username, password);
                        client.SendMessage(new TagSubjectMessage(LoginTag, AddUserSuccess, new DarkRiftWriter()), SendMode.Reliable);
                    }
                    else
                    {
                        if (_debug)
                        {
                            WriteEvent("User " + client.GlobalID + " failed to sign up!", LogType.Info);
                        }

                        // Return Error 1 for "Wrong username/password combination"
                        var writer = new DarkRiftWriter();
                        writer.Write((byte)1);
                        client.SendMessage(new TagSubjectMessage(LoginTag, AddUserFailed, writer), SendMode.Reliable);
                    }
                }
                catch (Exception exception)
                {
                    _dbConnector.LogException(exception, "LoginPlugin: Add User");

                    // Return Error 2 for Database error
                    var writer = new DarkRiftWriter();
                    writer.Write((byte)2);
                    client.SendMessage(new TagSubjectMessage(LoginTag, AddUserFailed, writer), SendMode.Reliable);
                }
            }
        }

        private void Logout(uint id)
        {
            if (UsersLoggedIn.ContainsKey(id))
                UsersLoggedIn.Remove(id);

            if (_debug)
                WriteEvent("User " + id + " logged out!", LogType.Info);
        }

        private bool UsernameAvailable(string username)
        {
            try
            {
                return _dbConnector.Users.AsQueryable().FirstOrDefault(u => u.Username == username) == null;
            }
            catch (Exception e)
            {
                _dbConnector.LogException(e, "LoginPlugin: CheckUsername");
                return false;
            }
        }

        private void AddNewUser(string username, string password)
        {
            try
            {
                _dbConnector.Users.InsertOne(new User(username, password));

                if (_debug)
                {
                    WriteEvent("New User: " + username, LogType.Info);
                }
            }
            catch (Exception e)
            {
                _dbConnector.LogException(e, "LoginPlugin: AddNewUser");
            }
        }
        
        #region Commands

        private void UsersLoggedInCommand(object sender, CommandEventArgs e)
        {
            WriteEvent(UsersLoggedIn.Count + " Users logged in", LogType.Info);
        }

        private void UsersOnlineCommand(object sender, CommandEventArgs e)
        {
            WriteEvent(ClientManager.GetAllClients().Length + " Users logged in", LogType.Info);
        }

        private void DebugCommand(object sender, CommandEventArgs e)
        {
            _debug = !_debug;
            WriteEvent("Debug is: " + _debug, LogType.Info);
        }

        private void AddUserCommand(object sender, CommandEventArgs e)
        {
            if (e.Arguments.Length != 2)
                return;

            var username = e.Arguments[0];
            var password = BCrypt.Net.BCrypt.HashPassword(e.Arguments[1], 10);

            if (UsernameAvailable(username))
                AddNewUser(username, password);
        }

        private void AllowAddUserCommand(object sender, CommandEventArgs e)
        {
            switch (e.Arguments[0])
            {
                case "on":
                    _allowAddUser = true;
                    WriteEvent("Adding users allowed: True!", LogType.Info);
                    break;
                case "off":
                    _allowAddUser = false;
                    WriteEvent("Adding users allowed: False!", LogType.Info);
                    break;
                default:
                    WriteEvent("Please enter [AllowAddUser off] or [AllowAddUser on]", LogType.Info);
                    break;
            }
        }
        #endregion
    }
}
