using System;
using System.Configuration;
using System.IO;
using System.Xml.Linq;
using DarkRift;
using DarkRift.Server;
using MongoDB.Driver;

namespace DbConnectorPlugin
{
    public class DbConnector : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => false;

        public IMongoCollection<Message> Messages;
        public IMongoCollection<User> Users;
        
        private string _configPath = @"Plugins\DbConnector.xml";
        private readonly IMongoDatabase _database;

        public DbConnector(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            var connectionString = LoadConfig();
            WriteEvent(connectionString, LogType.Trace);
            try
            {
                var client = new MongoClient(connectionString);
                _database = client.GetDatabase("test");
                GetCollections();
            }
            catch (Exception e)
            {
                LogException(e, "Database Setup");
                throw;
            }
        }

        // Get Connection String
        private string LoadConfig()
        {
            XDocument document;

            if (!File.Exists(_configPath))
            {
                document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"),
                    new XComment("Insert your ConnectionString below!"),
                    new XElement("ConnectionString", "mongodb://localhost:27017"));
                document.Save(_configPath);
                WriteEvent(
                    "Created /Plugins/DbConnector.xml. Please adjust your connection string and restart the server!",
                    LogType.Warning);
                return "mongodb://localhost:27017";
            }

            try
            {
                document = XDocument.Load(_configPath);

                if (document.Element("ConnectionString") == null || document.Element("ConnectionString").Value == null)
                {
                    WriteEvent("Couldn't load connection string from Plugins/DbConnector.xml.", LogType.Fatal);
                    return null;
                }

                return document.Element("ConnectionString").Value;
            }
            catch (Exception e)
            {
                WriteEvent("Failed to load DbConnector.xml.", LogType.Error);
                throw;
            }

        }

        private void GetCollections()
        {
            Messages = _database.GetCollection<Message>("messages");
            Users = _database.GetCollection<User>("users");
        }

        public void LogException(Exception e, string context)
        {
            WriteEvent("Mongo DB exception (" + context + "): ", LogType.Error, e);
        }
    }
}
