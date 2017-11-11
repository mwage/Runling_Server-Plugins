using MongoDB.Bson.Serialization.Attributes;

namespace DbConnectorPlugin
{
    public class User
    {
        [BsonId]
        public string Username { get; set; }
        public string Password { get; set; }

        public User(string username, string password, DbConnector dbConnector)
        {
            Username = username;
            Password = password;
            dbConnector.FriendLists.InsertOne(new FriendList(username));
        }
    }
}
