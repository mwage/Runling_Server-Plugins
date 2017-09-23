using System;
using DarkRift;
using DarkRift.Server;
using DbConnectorPlugin;

namespace ChatPlugin
{
    public class Chat : Plugin
    {

        public override Version Version => new Version(1,0,0);
        public override bool ThreadSafe => false;

        // Tag
        private const byte ChatTag = 1;

        // Subjects
        private const ushort Keys = 0;

        private DbConnector _dbConnector;

        public Chat(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            ClientManager.ClientConnected += OnPlayerConnected;
            ClientManager.ClientDisconnected += OnPlayerDisconnected;
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
            if (!(e.Message is TagSubjectMessage message) || message.Tag != ChatTag)
                return;

            var client = (Client)sender;

            // Login Request
            if (message.Subject == Keys)
            {
            }
        }
    }
}
