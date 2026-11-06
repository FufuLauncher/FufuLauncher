/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Models;

public sealed class QuickFpsSettings
{
    public const int SlotCount = 5;
    public const int MinHotkeyFps = 60;
    public const int MaxFps = 100000;

    private static readonly int[] DefaultSlotFps = [30, 60, 90, 120, 144];

    public bool Enabled
    {
        get;
        set;
    }

    public bool SwitchPresetsEnabled
    {
        get;
        set;
    } = true;

    public bool AdjustEnabled
    {
        get;
        set;
    } = true;

    public bool ListenOnlyWhileGameRunning
    {
        get;
        set;
    } = true;

    public int AdjustStep
    {
        get;
        set;
    } = 10;

    public int IncreaseKey
    {
        get;
        set;
    } = 0x26;

    public int DecreaseKey
    {
        get;
        set;
    } = 0x28;

    public int[] SlotFps
    {
        get;
        set;
    } = [30, 60, 90, 120, 144];

    public int[] SlotKeys
    {
        get;
        set;
    } = new int[SlotCount];

    public void Normalize()
    {
        AdjustStep = Math.Clamp(AdjustStep, 1, MaxFps);
        IncreaseKey = NormalizeKey(IncreaseKey, 0x26);
        DecreaseKey = NormalizeKey(DecreaseKey, 0x28);

        var fps = new int[SlotCount];
        var keys = new int[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            fps[i] = SlotFps != null && i < SlotFps.Length
                ? Math.Clamp(SlotFps[i], 1, MaxFps)
                : DefaultSlotFps[i];
            keys[i] = SlotKeys != null && i < SlotKeys.Length
                ? NormalizeKey(SlotKeys[i], 0)
                : 0;
        }

        SlotFps = fps;
        SlotKeys = keys;
    }

    public QuickFpsSettings Clone() => new()
    {
        Enabled = Enabled,
        SwitchPresetsEnabled = SwitchPresetsEnabled,
        AdjustEnabled = AdjustEnabled,
        ListenOnlyWhileGameRunning = ListenOnlyWhileGameRunning,
        AdjustStep = AdjustStep,
        IncreaseKey = IncreaseKey,
        DecreaseKey = DecreaseKey,
        SlotFps = (int[])SlotFps.Clone(),
        SlotKeys = (int[])SlotKeys.Clone()
    };

    private static int NormalizeKey(int key, int fallback) => key is >= 0 and < 256 ? key : fallback;
}
