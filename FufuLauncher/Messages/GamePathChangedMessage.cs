namespace FufuLauncher.Messages
{
    public class GamePathChangedMessage
    {
        public string GamePath
        {
            get;
        }
        public GamePathChangedMessage(string path) => GamePath = path;
    }
}