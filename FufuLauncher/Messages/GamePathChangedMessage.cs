/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
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
