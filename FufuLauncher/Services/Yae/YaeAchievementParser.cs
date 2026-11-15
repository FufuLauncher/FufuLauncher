/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Google.Protobuf;
using FufuLauncher.Services.Yae.Proto;

namespace FufuLauncher.Services.Yae;

public static class YaeAchievementParser
{
    public static List<YaeUiafItem> ParseAchievement(byte[] bytes, AchievementProtoFieldInfo pb)
    {
        var data = new List<Dictionary<uint, uint>>();
        var errorTimes = 0;

        using var stream = new CodedInputStream(bytes);
        try
        {
            uint tag;
            while ((tag = stream.ReadTag()) != 0)
            {
                if ((tag & 7) != 2)
                {
                    continue;
                }

                Dictionary<uint, uint>? record = [];
                using var entryStream = stream.ReadLengthDelimitedAsStream();
                try
                {
                    while ((tag = entryStream.ReadTag()) != 0)
                    {
                        if ((tag & 7) != 0)
                        {
                            record = null;
                            break;
                        }

                        record![tag >> 3] = entryStream.ReadUInt32();
                    }

                    if (record is { Count: > 2 })
                    {
                        data.Add(record);
                    }
                }
                catch (InvalidProtocolBufferException)
                {
                    if (errorTimes++ > 0)
                    {
                        throw;
                    }
                }
            }
        }
        catch (InvalidProtocolBufferException)
        {
        }

        return data
            .Select(record => new YaeUiafItem
            {
                Id = (int)record.GetValueOrDefault(pb.Id),
                Current = (int)record.GetValueOrDefault(pb.CurrentProgress),
                Status = (int)record.GetValueOrDefault(pb.Status),
                Timestamp = record.GetValueOrDefault(pb.FinishTimestamp),
            })
            .ToList();
    }
}