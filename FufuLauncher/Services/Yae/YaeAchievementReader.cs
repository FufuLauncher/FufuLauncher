/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Yae;

public static class YaeAchievementReader
{
    private static class ExitCode
    {
        public const int Success = 0;
        public const int InvalidConfig = 1;
        public const int GameCreateFailed = 2;
        public const int InjectionFailed = 3;
        public const int NoAchievementData = 4;
        public const int Unexpected = 5;
    }


    public static async Task<YaeUiafResult?> ReadAchievementsAsync(string gameExePath,
        CancellationToken cancellationToken = default)
    {
        if (IsGameRunning())
        {
            throw new ApplicationException("Yae_ErrGameRunning".GetLocalized());
        }

        if (!File.Exists(gameExePath))
        {
            throw new ApplicationException("Yae_ErrGameNotFound".GetLocalized());
        }


        var dllPath = ResolveYaeDll()
                      ?? throw new ApplicationException("Yae_ErrDllNotFound".GetLocalized());

        var metadata = await YaeMetadataService.GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var gameHash = YaeMetadataService.ComputeGameHash(gameExePath);
        var nativeConfig = YaeMetadataService.Resolve(metadata, gameHash);

        var cfgFile = Path.Combine(Path.GetTempPath(), $"YaeRead_{Guid.NewGuid():N}.tmp");
        var resultFile = Path.Combine(Path.GetTempPath(), $"YaeResult_{Guid.NewGuid():N}.json");
        var errorFile = Path.Combine(Path.GetTempPath(), $"YaeError_{Guid.NewGuid():N}.err");

        try
        {
            YaeReadConfig.Write(cfgFile, new YaeReadConfig
            {
                GameExePath = gameExePath,
                DllPath = dllPath,
                GameDir = Path.GetDirectoryName(gameExePath) ?? AppContext.BaseDirectory,
                ResultFilePath = resultFile,
                ErrorFilePath = errorFile,
                NativeConfig = nativeConfig,
                PbInfo = metadata.PbInfo,
            });

            var currentExe = Environment.ProcessPath
                             ?? throw new InvalidOperationException("Yae_ErrExePathUnavailable".GetLocalized());
            var psi = new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = $"--yae-inject \"{cfgFile}\" --lang \"{ResourceExtensions.CurrentCulture}\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(currentExe),
            };

            Process? helper;
            try
            {
                helper = Process.Start(psi);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new InvalidOperationException("Yae_ErrElevationCancelled".GetLocalized());
            }

            if (helper is null)
            {
                throw new InvalidOperationException("Yae_ErrElevationStartFailed".GetLocalized());
            }

            using (helper)
            {
                helper.WaitForExit();
                var exitCode = helper.ExitCode;
                if (exitCode != ExitCode.Success)
                {
                    var detail = File.Exists(errorFile)
                        ? await File.ReadAllTextAsync(errorFile, cancellationToken).ConfigureAwait(false)
                        : string.Empty;
                    throw new InvalidOperationException(exitCode switch
                    {
                        ExitCode.InvalidConfig => "Yae_ErrInvalidConfig".GetLocalized(),
                        ExitCode.GameCreateFailed => "Yae_ErrGameCreateFailed".GetLocalized(),
                        ExitCode.InjectionFailed => "Yae_ErrInjectionFailed".GetLocalized(),
                        ExitCode.NoAchievementData => "Yae_ErrNoData".GetLocalized(),
                        _ => string.Format("Yae_ErrExitCode_Format".GetLocalized(), exitCode),
                    } + (string.IsNullOrEmpty(detail) ? string.Empty : $"\n{detail}"));
                }
            }

            if (!File.Exists(resultFile))
            {
                throw new InvalidOperationException("Yae_ErrNoResultFile".GetLocalized());
            }

            var json = await File.ReadAllTextAsync(resultFile, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<YaeUiafResult>(json);
        }
        finally
        {
            TryDelete(cfgFile);
            TryDelete(resultFile);
            TryDelete(errorFile);
        }
    }


    public static int RunElevatedInjection(string configFile)
    {
        string? errorFilePath = null;
        try
        {
            if (!File.Exists(configFile))
            {
                return ExitCode.InvalidConfig;
            }

            var cfg = YaeReadConfig.Read(File.ReadAllLines(configFile));
            if (cfg is null)
            {
                return ExitCode.InvalidConfig;
            }

            errorFilePath = cfg.ErrorFilePath;

            var pipe = new YaeNamedPipeServer(cfg.NativeConfig);
            using var game = new YaeGameProcess(cfg.GameExePath, cfg.DllPath);
            pipe.AttachGame(game);

            var data = pipe.Collect();
            var achievement = data.FirstOrDefault(d => d.Kind == YaeCommandKind.ResponseAchievement);
            if (achievement is null)
            {
                WriteError(errorFilePath, "Yae_ErrNoAchievementData".GetLocalized());
                return ExitCode.NoAchievementData;
            }

            var items = YaeAchievementParser.ParseAchievement(achievement.Payload, cfg.PbInfo);
            var result = new YaeUiafResult
            {
                Info = new YaeUiafInfo
                {
                    ExportApp = "FufuLauncher",
                    ExportAppVersion = typeof(YaeAchievementReader).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                    UiafVersion = "v1.1",
                    ExportTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                },
                List = items,
            };

            File.WriteAllText(cfg.ResultFilePath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return ExitCode.Success;
        }
        catch (YaeGameCreateException ex)
        {
            WriteError(errorFilePath, ex.Message);
            return ExitCode.GameCreateFailed;
        }
        catch (YaeInjectionException ex)
        {
            WriteError(errorFilePath, ex.Message);
            return ExitCode.InjectionFailed;
        }
        catch (Exception ex)
        {
            WriteError(errorFilePath, ex.Message);
            return ExitCode.Unexpected;
        }
    }

    private static bool IsGameRunning()
        => Process.GetProcessesByName("YuanShen").Length > 0 || Process.GetProcessesByName("GenshinImpact").Length > 0;

    private static string? ResolveYaeDll()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "YaeAchievementLib.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "YaeAchievementLib.dll"),
            Path.Combine(AppContext.BaseDirectory, "modules", "YaeAchievementLib.dll"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void WriteError(string? errorFilePath, string message)
    {
        if (string.IsNullOrEmpty(errorFilePath)) return;
        try
        {
            File.WriteAllText(errorFilePath, message);
        }
        catch
        {
            /* ignore */
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }
}