using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace DbConnectorPlugin
{
    public class FriendList
    {
        [BsonId]
        public string Username { get; set; }
        public List<string> Friends { get; set; }
        public List<string> OpenFriendRequests { get; set; }
        public List<string> UnansweredFriendRequests { get; set; }

        public FriendList(string username)
        {
            Username = username;
            Friends = new List<string>();
            OpenFriendRequests = new List<string>();
            UnansweredFriendRequests = new List<string>();
        }
    }
}
