namespace DbConnectorPlugin
{
    public class Message
    {
        public string Content { get; set; }

        public Message(string content)
        {
            Content = content;
        }
    }
}