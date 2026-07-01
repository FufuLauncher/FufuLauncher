/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace FufuLauncher.Messages
{



    public class AcrylicSettingChangedMessage
    {
        public bool IsEnabled
        {
            get;
        }

        public AcrylicSettingChangedMessage(bool isEnabled)
        {
            IsEnabled = isEnabled;
        }
    }
}
