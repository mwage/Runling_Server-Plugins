using System;
using DarkRift.Server;
using DbConnectorPlugin;

namespace Test
{
    public class Test : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => false;

        private DbConnector _dbConnector;

        public override Command[] Commands => new[]
        {
            new Command ("AddMessage", "Adds a Message to the Database [AddMessage message]", "AddMessage", TestMethod)
        };


        public Test(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
        }

        public async void TestMethod(object sender, CommandEventArgs commandEventArgs)
        {
            if (_dbConnector == null)
            {
                _dbConnector = PluginManager.GetPluginByType<DbConnector>();
            }

            var message = commandEventArgs.Arguments[0];

            try
            {
                await _dbConnector.Messages.InsertOneAsync(new Message(message));
            }
            catch (Exception e)
            {
                _dbConnector.LogException(e, "Add Message failed");
                throw;
            }
        }
    }
}

