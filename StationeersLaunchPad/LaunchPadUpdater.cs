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

namespace StationeersLaunchPad
{
    // similar to jixxed's updater
    public static class LaunchPadUpdater
    {
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

        public static async UniTask CheckVersion()
        {
            var request = UnityWebRequest.Get("https://api.github.com/repos/StationeersLaunchPad/StationeersLaunchPad/releases/latest");
            Logger.Global.Log($"Requesting version...");
            var result = await request.SendWebRequest();

            if (result.result != UnityWebRequest.Result.Success)
            {
                request.Dispose();
                Logger.Global.LogError($"Failed to send web request! result: {result.result}, error: {result.error}");
                return;
            }

            var text = result.downloadHandler.text;
            var matches = versionRegex.Matches(text);
            if (matches.Count == 0)
            {
                request.Dispose();
                Logger.Global.LogError($"Failed to find version regex matches.");
                return;
            }

            var latestVersion = new Version(matches[0].Groups[1].Value.TrimStart('V', 'v'));
            var currentVersion = new Version(LaunchPadPlugin.pluginVersion.TrimStart('V', 'v'));

            if (latestVersion <= currentVersion)
            {
                request.Dispose();
                Logger.Global.Log($"Plugin is up-to-date.");
                return;
            }

            Logger.Global.LogWarning($"Plugin is NOT up-to-date.");
            var downloadMatches = GameManager.IsBatchMode ? downloadRegexServer.Matches(text) : downloadRegexClient.Matches(text);
            if (downloadMatches.Count == 0)
            {
                request.Dispose();
                Logger.Global.LogError($"Failed to find download regex matches.");
                return;
            }

            var downloadRequest = UnityWebRequest.Get(downloadMatches[0].Groups[1].Value);
            Logger.Global.Log($"Requesting download file...");
            var downloadResult = await downloadRequest.SendWebRequest();

            if (downloadResult.result != UnityWebRequest.Result.Success)
            {
                downloadRequest.Dispose();
                request.Dispose();
                Logger.Global.LogError($"Failed to send web request to download! result: {result.result}, error: {result.error}");
                return;
            }

            var tempPath = Path.GetTempPath();
            var extractionPath = Path.Combine(tempPath, "StationeersLaunchPad");
            var zipFilePath = Path.Combine(tempPath, "SLP.zip");
            Logger.Global.Log($"Writing file to {zipFilePath}...");
            File.WriteAllBytes(zipFilePath, downloadResult.downloadHandler.data);

            var zipFile = ZipFile.Open(zipFilePath, ZipArchiveMode.Read);
            Logger.Global.Log($"Extracting file contents to {extractionPath}...");
            zipFile.ExtractToDirectory(tempPath);
            zipFile.Dispose();
            File.Delete(zipFilePath);

            Logger.Global.Log($"Extracted file contents to {extractionPath}!");
            if (!Directory.Exists(extractionPath))
            {
                downloadRequest.Dispose();
                request.Dispose();
                Logger.Global.LogError($"Failed to exteract zip file");
                return;
            }

            var pluginPath = Path.Combine(Paths.PluginPath, "StationeersLaunchPad");
            foreach (var file in LaunchPadUpdater.Assemblies)
            {
                var fileName = $"{file}.dll";
                var backupFileName = $"{file}.dll.bak";
                var newPath = Path.Combine(extractionPath, fileName);
                if (!File.Exists(newPath))
                    continue;

                var path = Path.Combine(pluginPath, fileName);
                if (!File.Exists(path))
                    continue;

                if (File.Exists(backupFileName)) {
                    File.Delete(backupFileName);
                }

                Logger.Global.Log($"Backing up DLL to {path}!");
                File.Copy(path, backupFileName);
                Logger.Global.Log($"Deleting DLL at {path}!");
                File.Delete(path);

                Logger.Global.Log($"Copying new DLL to {newPath}!");
                File.Copy(newPath, path);
                File.Delete(newPath);
            }
            Directory.Delete(extractionPath);

            downloadRequest.Dispose();
            request.Dispose();

            LaunchPadConfig.HasUpdated = true;
            Logger.Global.LogError($"Mod loader has been updated to version {latestVersion}, please restart your game!");
        }
    }
}
