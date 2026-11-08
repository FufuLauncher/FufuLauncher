/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers;

namespace FufuLauncher.Services.Background;

internal readonly record struct EdgeAntialiasProfile(
    float EdgeThreshold,
    float EdgeThresholdMin,
    float SubpixelQuality,
    int SearchSteps);

internal static class BackgroundImageEdgeAntialiaser
{
    private const float InvByte = 1f / 255f;
    private const float HalfInvByte = 0.5f / 255f;
    private const int LinearLutSize = 16384;
    
    private static readonly int[] StepTable = [1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 8];

    private static readonly float[] SrgbToLinear = BuildSrgbToLinear();
    private static readonly byte[] LinearToSrgb = BuildLinearToSrgb();

    public static int MaxSearchSteps => StepTable.Length;
    
    public static int Apply(byte[] pixels, int width, int height, EdgeAntialiasProfile profile)
    {
        if (width < 3 || height < 3)
            return 0;

        var pixelCount = checked(width * height);
        var byteCount = checked(pixelCount * 4);
        if (pixels.Length < byteCount)
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4", nameof(pixels));

        var steps = Math.Clamp(profile.SearchSteps, 1, StepTable.Length);
        var luma = ArrayPool<byte>.Shared.Rent(pixelCount);
        var source = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            Buffer.BlockCopy(pixels, 0, source, 0, byteCount);
            
            Parallel.For(0, height, y =>
            {
                var rowStart = y * width;
                var rowEnd = rowStart + width;
                for (var p = rowStart; p < rowEnd; p++)
                {
                    var i = p << 2;
                    luma[p] = (byte)((source[i + 2] * 54 + source[i + 1] * 183 + source[i] * 19 + 128) >> 8);
                }
            });
            
            var processed = 0;
            Parallel.For(1, height - 1,
                () => 0,
                (y, _, local) =>
                {
                    for (var x = 1; x < width - 1; x++)
                    {
                        if (ProcessPixel(source, pixels, luma, width, height, x, y, in profile, steps))
                            local++;
                    }

                    return local;
                },
                local => Interlocked.Add(ref processed, local));

            return processed;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(luma);
            ArrayPool<byte>.Shared.Return(source);
        }
    }

    private static bool ProcessPixel(byte[] source, byte[] dest, byte[] luma, int width, int height,
        int x, int y, in EdgeAntialiasProfile profile, int steps)
    {
        var idx = y * width + x;

        var lumaM = luma[idx] * InvByte;
        var lumaN = luma[idx - width] * InvByte;
        var lumaS = luma[idx + width] * InvByte;
        var lumaW = luma[idx - 1] * InvByte;
        var lumaE = luma[idx + 1] * InvByte;

        var maxLuma = MathF.Max(MathF.Max(MathF.Max(lumaN, lumaS), MathF.Max(lumaW, lumaE)), lumaM);
        var minLuma = MathF.Min(MathF.Min(MathF.Min(lumaN, lumaS), MathF.Min(lumaW, lumaE)), lumaM);
        var range = maxLuma - minLuma;
        
        if (range < MathF.Max(profile.EdgeThresholdMin, maxLuma * profile.EdgeThreshold))
            return false;

        var lumaNW = luma[idx - width - 1] * InvByte;
        var lumaNE = luma[idx - width + 1] * InvByte;
        var lumaSW = luma[idx + width - 1] * InvByte;
        var lumaSE = luma[idx + width + 1] * InvByte;

        var lumaNS = lumaN + lumaS;
        var lumaWE = lumaW + lumaE;
        
        var subpixA = (2f * (lumaNS + lumaWE) + (lumaNW + lumaNE + lumaSW + lumaSE)) * (1f / 12f);
        var subpixC = Math.Clamp(MathF.Abs(subpixA - lumaM) / range, 0f, 1f);
        var subpixF = (-2f * subpixC + 3f) * subpixC * subpixC;
        var subpixH = subpixF * subpixF * profile.SubpixelQuality;

        var edgeHorz =
            MathF.Abs(lumaNW + lumaSW - 2f * lumaW) +
            MathF.Abs(lumaNS - 2f * lumaM) * 2f +
            MathF.Abs(lumaNE + lumaSE - 2f * lumaE);
        var edgeVert =
            MathF.Abs(lumaNW + lumaNE - 2f * lumaN) +
            MathF.Abs(lumaWE - 2f * lumaM) * 2f +
            MathF.Abs(lumaSW + lumaSE - 2f * lumaS);
        var horzSpan = edgeHorz >= edgeVert;

        var luma1 = horzSpan ? lumaN : lumaW;
        var luma2 = horzSpan ? lumaS : lumaE;
        var gradient1 = luma1 - lumaM;
        var gradient2 = luma2 - lumaM;
        var pairNegative = MathF.Abs(gradient1) >= MathF.Abs(gradient2);
        var gradientScaled = MathF.Max(MathF.Abs(gradient1), MathF.Abs(gradient2)) * 0.25f;
        var localAverage = 0.5f * (lumaM + (pairNegative ? luma1 : luma2));
        var lumaMBelow = lumaM - localAverage < 0f;
        
        var pairOffset = horzSpan ? (pairNegative ? -width : width) : (pairNegative ? -1 : 1);
        var along = horzSpan ? 1 : width;
        var maxNeg = horzSpan ? x : y;
        var maxPos = horzSpan ? width - 1 - x : height - 1 - y;
        
        var distNeg = 0;
        var endNeg = 0f;
        var doneNeg = false;
        for (var i = 0; i < steps && !doneNeg; i++)
        {
            distNeg = Math.Min(distNeg + StepTable[i], maxNeg);
            var si = idx - distNeg * along;
            endNeg = (luma[si] + luma[si + pairOffset]) * HalfInvByte - localAverage;
            doneNeg = MathF.Abs(endNeg) >= gradientScaled || distNeg >= maxNeg;
        }

        var distPos = 0;
        var endPos = 0f;
        var donePos = false;
        for (var i = 0; i < steps && !donePos; i++)
        {
            distPos = Math.Min(distPos + StepTable[i], maxPos);
            var si = idx + distPos * along;
            endPos = (luma[si] + luma[si + pairOffset]) * HalfInvByte - localAverage;
            donePos = MathF.Abs(endPos) >= gradientScaled || distPos >= maxPos;
        }
        
        var spanLength = distNeg + distPos;
        var nearestNeg = distNeg < distPos;
        var nearest = Math.Min(distNeg, distPos);
        var goodSpan = ((nearestNeg ? endNeg : endPos) < 0f) != lumaMBelow;
        var edgeBlend = goodSpan && spanLength > 0 ? 0.5f - nearest / (float)spanLength : 0f;
        var blend = MathF.Max(edgeBlend, subpixH);

        if (blend < 0.5f / 255f)
            return false;

        BlendLinear(source, dest, idx << 2, (idx + pairOffset) << 2, blend);
        return true;
    }

    private static void BlendLinear(byte[] source, byte[] dest, int center, int neighbor, float t)
    {
        const float lutScale = LinearLutSize - 1;
        for (var c = 0; c < 3; c++)
        {
            var a = SrgbToLinear[source[center + c]];
            var b = SrgbToLinear[source[neighbor + c]];
            var v = a + (b - a) * t;
            dest[center + c] = LinearToSrgb[Math.Clamp((int)(v * lutScale + 0.5f), 0, LinearLutSize - 1)];
        }

        var alphaA = source[center + 3];
        var alphaB = source[neighbor + 3];
        if (alphaA != alphaB)
            dest[center + 3] = (byte)Math.Clamp((int)(alphaA + (alphaB - alphaA) * t + 0.5f), 0, 255);
    }

    private static float[] BuildSrgbToLinear()
    {
        var table = new float[256];
        for (var i = 0; i < table.Length; i++)
        {
            var c = i / 255.0;
            table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        return table;
    }

    private static byte[] BuildLinearToSrgb()
    {
        var table = new byte[LinearLutSize];
        for (var i = 0; i < table.Length; i++)
        {
            var v = i / (double)(LinearLutSize - 1);
            var s = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
            table[i] = (byte)Math.Clamp((int)Math.Round(s * 255.0), 0, 255);
        }

        return table;
    }
}
