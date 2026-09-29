using Cysharp.Threading.Tasks;
using SFS.Input;
using SFS.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace ModLoader
{
    static class ModUpdaterMethod
    {
        // ------------------------------------------------------------------------------------------------------------
        // checkModUpdate : extension method for the Mod class.
        // Checks on the specified github repository if an update is available. The release marked as "latest" is the
        // one that will be checked.
        // If an update is available, it offers to download it and install it.
        // If some files appear to be missing, it also allows to download the missing files.
        // ------------------------------------------------------------------------------------------------------------
        // PARAMETERS :
        // - github_userName   : Your username on https://github.com/
        // - github_repository : The name of your repository
        // - current_tag       : The tag corresponding to the current version (must be in the format "Vx.y.z", where
        //                       x, y and z are integers - there can be as many numbers as you want, so "V1.0",
        //                       "V1.2.3" or "V1.4.6.4.1" are all valid tags)
        // - assetsList        : The list of all files expected to be downloaded (should at least contain your dll)
        // ------------------------------------------------------------------------------------------------------------
        // EXAMPLE :
        // Call this from your Load method :
        //     List<string> myAssets = new List<string> { { "MyMod.dll" } };
        //     this.checkModUpdate("userName", "repo", "V1.0.0", myAssets);
        // ------------------------------------------------------------------------------------------------------------
        public static void checkModUpdate(this Mod mod, string github_userName, string github_repository, string current_tag, List<string> assetsList)
        {
            try
            {
                AutoUpdater.ModUpdater modUpdater = new AutoUpdater.ModUpdater(mod, github_userName, github_repository, current_tag, assetsList);
                modUpdater.checkIfUpdateAvailable().Forget();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Failed to check update for " + mod.DisplayName + " - reason: " + e.Message);
            }
        }
    }
}


namespace AutoUpdater
{

    class ModUpdater
    {
        private ModLoader.Mod mod;
        private string latestReleasePath;
        private Tag currentTag;
        private List<string> assetsList;

        private HttpClient client;

        private const double C_CONNECTION_TIMEOUT_IN_SECONDS = 5.0;
        private const string C_BACKUP_FILE_EXTENSION = "OLD";


        // ------------------------------------------------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------------------------------------------------
        public ModUpdater(ModLoader.Mod mod, string github_userName, string github_repository, string current_tag, List<string> assetsList)
        {
            this.mod = mod;
            latestReleasePath = "https://github.com/" + github_userName + "/" + github_repository + "/releases/latest";

            currentTag = new Tag(current_tag);
            this.assetsList = assetsList;

            client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(C_CONNECTION_TIMEOUT_IN_SECONDS);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Main update function
        // ------------------------------------------------------------------------------------------------------------
        // Checks if all files are present and up-to-date. If not, proposes to update or complete missing files.
        // ------------------------------------------------------------------------------------------------------------
        public async UniTask checkIfUpdateAvailable()
        {
            // called first to remove previously backed up files if an update was made on the previous call
            cleanBackups();

            try
            {
                // Launch a request on the latest release URL
                var request = new HttpRequestMessage(HttpMethod.Head, latestReleasePath);
                request.Headers.UserAgent.ParseAdd(mod.DisplayName + "_AutoUpdater");

                HttpResponseMessage response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // After the redirection, this is now the path to the latest release, with the actual tag
                    Uri finalUri = response.RequestMessage.RequestUri;

                    // retrieve tag
                    string[] listStr = finalUri.AbsolutePath.Split('/');
                    string tag = listStr.Last();

                    Tag newTag = new Tag(tag);

                    if(currentTag < newTag)
                    {
                        // Yay ! An update is available !
                        await proposeModUpdateAndRestart();
                    }
                    else if(!checkMissingFiles())
                    {
                        // Oops, some files are missing...
                        await proposeCompleteMissingFilesAndRestart();
                    }
                }
                else
                {
                    throw new Exception("Unable to reach host");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Failed to check update for " + mod.DisplayName + " - reason: " + e.Message);
            }
        }


        // ------------------------------------------------------------------------------------------------------------
        // Update management functions
        // ------------------------------------------------------------------------------------------------------------
        private async UniTask proposeModUpdateAndRestart()
        {
            bool doUpdate = await SFS.UI.MenuGenerator.OpenConfirmationAsync(SFS.Input.CloseMode.Current,
                                                                             () => { return "New update available for " + mod.DisplayName + " - Download and install it?"; },
                                                                             () => { return "Yes!!!"; },
                                                                             () => { return "No"; });

            if (doUpdate)
            {
                await UpdateToLatest();

                MenuGenerator.OpenConfirmation(CloseMode.Current,
                        () => "Updates successful. Restart so changes take effect?",
                        () => "Restart",
                        ApplicationUtility.Relaunch);
            }
        }

        private async UniTask proposeCompleteMissingFilesAndRestart()
        {
            bool downloadFiles = await SFS.UI.MenuGenerator.OpenConfirmationAsync(SFS.Input.CloseMode.Current,
                                                                                         () => { return "Some files are missing for " + mod.DisplayName + " - Download them?"; },
                                                                                         () => { return "Yes"; },
                                                                                         () => { return "No"; });

            if (downloadFiles)
            {
                await CompleteMissingFiles();

                MenuGenerator.OpenConfirmation(CloseMode.Current,
                        () => "Updates successful. Restart so changes take effect?",
                        () => "Restart",
                        ApplicationUtility.Relaunch);
            }
        }


        private async UniTask UpdateToLatest()
        {
            foreach (string asset in assetsList)
            {
                try
                {
                    // Make a backup of the current file
                    backupAsset(asset);

                    // Download file
                    await downloadAsset(asset);

                    // delete backup (only if it's not a dll - because the dll is loaded and can't be deleted)
                    if (!isDll(asset))
                    {
                        cleanBackup(asset);
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("could not update " + asset + ": " + e.Message);

                    // If the dll itself failed to update, restore it so that the player can have another chance before having to reinstall manually the mod
                    if (isDll(asset))
                    {
                        restoreBackup(asset);
                    }
                }
            }
        }

        private async UniTask CompleteMissingFiles()
        {
            foreach (string asset in assetsList)
            {
                try
                {
                    if (!assetExists(asset))
                    {
                        // Download file
                        await downloadAsset(asset);
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("could not download asset " + asset + ": " + e.Message);
                }
            }
        }


        private async UniTask downloadAsset(string asset)
        {
            string downloadPath = latestReleasePath + "/download/" + asset;

            // Download file
            System.IO.Stream downloadStream = await client.GetStreamAsync(downloadPath);

            // Create new file
            string filePath = mod.ModFolder + "/" + asset;
            System.IO.FileStream fileStream = new FileStream(filePath, FileMode.Create);

            // copy data to new file
            await downloadStream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }

        private bool checkMissingFiles()
        {
            foreach (string asset in assetsList)
            {
                if (!assetExists(asset)) return false;
            }

            return true;
        }
        

        // ------------------------------------------------------------------------------------------------------------
        // Functions for assets management
        // ------------------------------------------------------------------------------------------------------------
        private bool isDll(string assetName)
        {
            return Path.GetExtension(assetName).Equals(".dll");
        }

        private bool assetExists(string assetName)
        {
            return File.Exists(mod.ModFolder + "/" + assetName);
        }

        private void backupAsset(string assetName)
        {
            string filePath = mod.ModFolder + "/" + assetName;
            string backupFilePath = filePath + "." + C_BACKUP_FILE_EXTENSION;

            try
            {
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                }

                if (File.Exists(filePath))
                {
                    File.Move(filePath, backupFilePath);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Failed to backup file " + filePath + ": " + e.Message);
            }
        }

        private void restoreBackup(string assetName)
        {
            string filePath = mod.ModFolder + "/" + assetName;
            string backupFilePath = filePath + "." + C_BACKUP_FILE_EXTENSION;

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                if (File.Exists(backupFilePath))
                {
                    File.Move(backupFilePath, filePath);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Failed to restore file " + filePath + ": " + e.Message);
            }

        }

        private void cleanBackups()
        {
            foreach (string assetName in assetsList)
            {
                cleanBackup(assetName);
            }
        }

        private void cleanBackup(string assetName)
        {
            string fullAssetName = mod.ModFolder + "/" + assetName + "." + C_BACKUP_FILE_EXTENSION;

            if (File.Exists(fullAssetName))
            {
                File.Delete(fullAssetName);
            }
        }
    }
}
