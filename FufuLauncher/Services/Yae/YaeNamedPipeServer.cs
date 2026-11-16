/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace FufuLauncher.Services.Yae;

internal sealed class YaeNamedPipeServer
{
    public const string PipeName = "YaeAchievementPipe";

    private readonly NamedPipeServerStream _serverStream;
    private readonly YaeNativeConfiguration _config;
    private YaeGameProcess? _game;


    public YaeNamedPipeServer(YaeNativeConfiguration config)
    {
        _config = config;
        _serverStream = new NamedPipeServerStream(PipeName);
    }


    public void AttachGame(YaeGameProcess game) => _game = game;


    public List<YaeData> Collect()
    {
        _serverStream.WaitForConnection();

        var list = new List<YaeData>();
        using var reader = new BinaryReader(_serverStream, Encoding.UTF8, true);
        using var writer = new BinaryWriter(_serverStream, Encoding.UTF8, true);

        while (_game is not null && _game.IsRunning && _serverStream.IsConnected)
        {
            int rawKind;
            try
            {
                rawKind = reader.ReadByte();
            }
            catch (IOException)
            {
                break;
            }

            if (rawKind < 0)
            {
                break;
            }

            switch ((YaeCommandKind)rawKind)
            {
                case YaeCommandKind.RequestCmdId:
                    writer.Write(_config.AchievementCmdId);
                    writer.Write(_config.StoreCmdId);
                    break;

                case YaeCommandKind.RequestRva:
                    writer.Write(_config.DoCmd);
                    writer.Write(_config.UpdateNormalProperty);
                    writer.Write(_config.NewString);
                    writer.Write(_config.FindGameObject);
                    writer.Write(_config.EventSystemUpdate);
                    writer.Write(_config.SimulatePointerClick);
                    writer.Write(_config.ToInt32);
                    writer.Write(_config.TcpStatePtr);
                    writer.Write(_config.SharedInfoPtr);
                    writer.Write(_config.Decompress);
                    break;

                case YaeCommandKind.RequestResumeThread:
                    _game.ResumeMainThread();
                    break;

                case YaeCommandKind.ResponseAchievement:
                case YaeCommandKind.ResponsePlayerStore:
                {
                    int contentLength = reader.ReadInt32();
                    var payload = new byte[contentLength];
                    reader.BaseStream.ReadExactly(payload);
                    list.Add(new YaeData((YaeCommandKind)rawKind, payload));
                    break;
                }

                case YaeCommandKind.ResponsePlayerProp:
                {
                    var payload = new byte[Marshal.SizeOf<YaePropertyTypeValue>()];
                    reader.BaseStream.ReadExactly(payload);
                    list.Add(new YaeData(YaeCommandKind.ResponsePlayerProp, payload));
                    break;
                }

                case YaeCommandKind.SessionEnd:
                {
                    writer.Write(true);
                    writer.Flush();
                    _game.Kill();
                    list.Add(YaeData.SessionEnd);
                    return list;
                }
            }
        }

        return list;
    }
}