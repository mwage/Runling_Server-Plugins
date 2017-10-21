using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace DbConnectorPlugin
{
    public class User
    {
        [BsonId]
        public string Username { get; }
        public string Password { get; }
        public List<string> Friends { get; }
        public List<string> OpenFriendRequests { get; }
        public List<string> UnansweredFriendRequests { get; }

        public User(string username, string password)
        {
            Username = username;
            Password = password;
            Friends = new List<string>();
            OpenFriendRequests = new List<string>();
            UnansweredFriendRequests = new List<string>();
        }
    }
}
