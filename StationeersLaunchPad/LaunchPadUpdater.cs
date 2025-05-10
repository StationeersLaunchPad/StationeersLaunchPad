using Assets.Scripts;
using BepInEx;
using BepInEx.Bootstrap;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEngine.Networking;

namespace StationeersLaunchPad {
    // similar to jixxed's updater
    public static class LaunchPadUpdater {
        public static List<string> Assemblies = new List<string>()
        {
            "RG.ImGui",
            "StationeersMods.Interface",
            "StationeersMods.Shared",
            "StationeersLaunchPad",
        };

        private static Regex versionRegex = new Regex(@"""tag_name""\:\s""([V\d.]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex downloadRegexClient = new Regex(@"""browser_download_url""\:\s""([^""]*StationeersLaunchPad-v.+\.zip)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex downloadRegexServer = new Regex(@"""browser_download_url""\:\s""([^""]*StationeersLaunchPad-server-v.+\.zip)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static async UniTask CheckVersion() {
            using (var request = UnityWebRequest.Get("https://api.github.com/repos/StationeersLaunchPad/StationeersLaunchPad/releases/latest")) {
                request.timeout = 10; // 10 seconds because we are only fetching a json file

                Logger.Global.Log($"Requesting version...");
                var result = await request.SendWebRequest();

                if (result.result != UnityWebRequest.Result.Success) {
                    Logger.Global.LogError($"Failed to send web request! result: {result.result}, error: {result.error}");
                    return;
                }

                var text = result.downloadHandler.text;
                var matches = versionRegex.Matches(text);
                if (matches.Count == 0) {
                    Logger.Global.LogError($"Failed to find version regex matches.");
                    return;
                }

                var latestVersion = new Version(matches[0].Groups[1].Value.TrimStart('V', 'v'));
                var currentVersion = new Version(LaunchPadPlugin.pluginVersion.TrimStart('V', 'v'));

                if (latestVersion <= currentVersion) {
                    Logger.Global.Log($"Plugin is up-to-date.");
                    return;
                }

                Logger.Global.LogWarning($"Plugin is NOT up-to-date.");
                var downloadMatches = GameManager.IsBatchMode ? downloadRegexServer.Matches(text) : downloadRegexClient.Matches(text);
                if (downloadMatches.Count == 0) {
                    Logger.Global.LogError($"Failed to find download regex matches.");
                    return;
                }

                using (var downloadRequest = UnityWebRequest.Get(downloadMatches[0].Groups[1].Value)) {
                    downloadRequest.timeout = 45; // max of 45 seconds to download the zip file

                    Logger.Global.Log($"Requesting download file...");
                    var downloadResult = await downloadRequest.SendWebRequest();

                    if (downloadResult.result != UnityWebRequest.Result.Success) {
                        Logger.Global.LogError($"Failed to send web request to download! result: {result.result}, error: {result.error}");
                        return;
                    }

                    var tempPath = Path.GetTempPath();
                    var extractionPath = Path.Combine(tempPath, "StationeersLaunchPad");
                    if (Directory.Exists(extractionPath)) {
                        foreach (var file in Directory.GetFiles(extractionPath)) {
                            var path = Path.Combine(extractionPath, file);

                            if (File.Exists(path)) {
                                File.Delete(path);
                            }
                        }
                        Directory.Delete(extractionPath);
                    }

                    var zipFilePath = Path.Combine(tempPath, "SLP.zip");
                    if (File.Exists(zipFilePath)) {
                        File.Delete(zipFilePath);
                    }

                    Logger.Global.Log($"Writing file to {zipFilePath}...");
                    File.WriteAllBytes(zipFilePath, downloadResult.downloadHandler.data);

                    using (var zipFile = ZipFile.Open(zipFilePath, ZipArchiveMode.Read)) {
                        Logger.Global.Log($"Extracting file contents to {extractionPath}...");
                        zipFile.ExtractToDirectory(tempPath);
                    }
                    File.Delete(zipFilePath);

                    Logger.Global.Log($"Extracted file contents to {extractionPath}!");
                    if (!Directory.Exists(extractionPath)) {
                        Logger.Global.LogError($"Failed to exteract zip file");
                        return;
                    }

                    var pluginPath = Path.Combine(Paths.PluginPath, "StationeersLaunchPad");
                    foreach (var file in LaunchPadUpdater.Assemblies) {
                        var fileName = $"{file}.dll";
                        var backupFileName = $"{file}.dll.bak";
                        var newPath = Path.Combine(extractionPath, fileName);
                        if (!File.Exists(newPath))
                            continue;

                        var path = Path.Combine(pluginPath, fileName);
                        if (!File.Exists(path))
                            continue;

                        var backupPath = Path.Combine(pluginPath, backupFileName);
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);

                        Logger.Global.Log($"Backing up DLL to {backupPath}!");
                        File.Move(path, backupPath);
                        Logger.Global.Log($"Deleting DLL at {path}!");
                        File.Delete(path);

                        Logger.Global.Log($"Moving new DLL to {newPath}!");
                        File.Move(newPath, path);
                        //File.Delete(newPath);
                    }
                    Directory.Delete(extractionPath);

                    LaunchPadConfig.HasUpdated = true;
                    Logger.Global.LogError($"Mod loader has been updated to version {latestVersion}, please restart your game!");
                }
            }
        }

        public static async UniTask RevertUpdate() {
            var pluginPath = Path.Combine(Paths.PluginPath, "StationeersLaunchPad");
            foreach (var file in LaunchPadUpdater.Assemblies) {
                var fileName = $"{file}.dll";
                var backupFileName = $"{file}.dll.bak";

                var backupPath = Path.Combine(pluginPath, backupFileName);
                if (!File.Exists(backupPath))
                    continue;

                var path = Path.Combine(pluginPath, fileName);
                if (File.Exists(path))
                    File.Delete(path);

                Logger.Global.Log($"Moving backup DLL to {path}!");
                File.Move(backupPath, path);
                //Logger.Global.Log($"Deleting DLL at {backupPath}!");
                //File.Delete(backupPath);
            }

            Logger.Global.LogWarning($"Mod loader has reverted update changes due to an error.");
        }
    }
}
