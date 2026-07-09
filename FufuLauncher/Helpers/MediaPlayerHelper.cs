/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using Windows.Media.Playback;

namespace FufuLauncher.Helpers;

public static class MediaPlayerHelper
{
    public static MediaPlayer CreateLoopingMutedPlayer()
    {
        var mediaPlayer = new MediaPlayer
        {
            IsLoopingEnabled = true,
            IsMuted = true
        };
        DisableSystemMediaControls(mediaPlayer);
        return mediaPlayer;
    }

    public static void DisableSystemMediaControls(MediaPlayer? mediaPlayer)
    {
        if (mediaPlayer == null)
            return;

        try
        {
            mediaPlayer.CommandManager.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to disable media command manager: {ex.Message}");
        }
    }
}
