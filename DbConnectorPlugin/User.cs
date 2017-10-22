using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace DbConnectorPlugin
{
    public class User
    {
        [BsonId]
        public string Username { get; set; }
        public string Password { get; set; }
        public List<string> Friends { get; set; }
        public List<string> OpenFriendRequests { get; set; }
        public List<string> UnansweredFriendRequests { get; set; }

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
