using HarmonyLib;
using ModLoader;
using SFS.IO;
using System;
using System.Collections.Generic;
using UITools;



namespace ANAIS
{
    public class Main : Mod , IUpdatable //IUpdatableMod
    {
        const string C_STR_MOD_ID = "ANAIS";
        const string C_STR_MOD_NAME = "ANAIS";
        const string C_STR_AUTHOR = "Altaïr";
        const string C_STR_GAME_VERSION = "1.6.00.16";
        const string C_STR_MOD_VERSION = "V1.4.4";
        const string C_STR_MOD_DESCRIPTION = "Advanced NAvigation Innovative System\nReplaces the original navigation system with a more elaborated one.";

        private const string C_STR_CLOSEST_APPROACH_LINE_MOD_ID = "CLOSEST_APPROACH_LINE";

        public override string ModNameID => C_STR_MOD_ID;

        public override string DisplayName => C_STR_MOD_NAME;

        public override string Author => C_STR_AUTHOR;

        public override string MinimumGameVersionNecessary => C_STR_GAME_VERSION;

        public override string ModVersion => C_STR_MOD_VERSION;

        public override string Description => C_STR_MOD_DESCRIPTION;

        public override string IconLink => "https://github.com/Altair-soft/ANAIS/blob/main/Resources/LogoANAIS.png?raw=true"; // link to the logo

        public IFolder theModFolder = FileLocations.ModsFolder.GetFolder("ANAIS");

        // Set the dependencies
        public override Dictionary<string, string> Dependencies { get; } = new Dictionary<string, string> { { "UITools", "1.1.6" } };

        public Dictionary<string, FilePath> UpdatableFiles => new Dictionary<string, FilePath> (); // auto-update disabled for now because UITools is broken

        //public Dictionary<string, IFile> UpdatableFiles { get; }


        public Main() : base()
        {
            //UpdatableFiles = new Dictionary<string, IFile> { { "https://github.com/Altair-soft/ANAIS/releases/latest/download/ANAIS.dll", FileLocations.ModsFolder.GetFolder("ANAIS").GetFile("ANAIS.dll") } };
        }

        // This initializes the patcher. This is required if you use any Harmony patches
        public static Harmony patcher;


        public override void Load()
        {
            //UnityEngine.Debug.Log("Load called for ANAIS");

            // If closest approach line is active, this one or ANAIS must be disabled!
            checkModIncompatibility();

            // Initialize logs
            // NOTE: To enable the logs, set "debug" to true, and compile in Debug configuration. Logs are disabled in release.
            // (logs activation is controlled by the ACTIVE_LOGS conditional compilation symbol - right-click on ANAIS, Properties, then "Build" panel)
            // NOTE2: To customize the logs, see the AnaisLogger.cs file
            AnaisLogger.Init(debug: true, theModFolder.GetFile("Logs_ANAIS.txt").Path);

            // Initialize ANAIS settings
            ANAIS_Config.Init(new FolderPath(ModFolder).ExtendToFile("Config.txt"));

            // Register scene changer events
            ModLoader.Helpers.SceneHelper.OnWorldSceneLoaded += new Action(AnaisManager.setWorldSceneActive);
            ModLoader.Helpers.SceneHelper.OnWorldSceneLoaded += new Action(ANAIS_Panel.ShowGUI);
            ModLoader.Helpers.SceneHelper.OnWorldSceneUnloaded += new Action(AnaisManager.setWorldSceneInactive);

            // Start ANAIS thread
            AnaisManager.StartTask();

            // Run auto-updater
            List<string> assetsList = new List<string> { { "ANAIS.dll" } };
            this.checkModUpdate("Altair-soft", "ANAIS", C_STR_MOD_VERSION, assetsList);
        }

        public override void Early_Load()
        {
            // This method runs before anything from the game is loaded. This is where you should apply your patches, as shown below.
            ApplyPatch();
        }

        void ApplyPatch()
        {
            //UnityEngine.Debug.Log("Patching ANAIS");

            // The patcher uses an ID formatted like a web domain
            Main.patcher = new Harmony($"{C_STR_MOD_ID}.{C_STR_MOD_NAME}.{C_STR_AUTHOR}");

            // This pulls your Harmony patches from everywhere in the namespace and applies them.
            Main.patcher.PatchAll();
        }

        void checkModIncompatibility()
        {
            // If closest approach line is active, this one or ANAIS must be disabled!

            if (ModsSettings.main.settings.modsActive.TryGetValue(C_STR_CLOSEST_APPROACH_LINE_MOD_ID, out bool closestApproachActive) && closestApproachActive)
            {
                SFS.UI.MenuGenerator.OpenConfirmation(SFS.Input.CloseMode.Current,
                                                      () => { return "ANAIS and Closest approach line are not compatible. Which one do you want to keep?"; },
                                                      () => { return "ANAIS"           ; }, DisableClosestApproach,
                                                      () => { return "Closest approach"; }, DisableANAIS );
            }

            void DisableClosestApproach()
            {
                DisableModAndRestart(C_STR_CLOSEST_APPROACH_LINE_MOD_ID);
            }

            void DisableANAIS()
            {
                DisableModAndRestart(C_STR_MOD_ID);
            }

            void DisableModAndRestart(string modId)
            {
                ModsSettings.main.settings.modsActive[modId] = false;
                ModsSettings.main.SaveAll();
                ApplicationUtility.Relaunch();
            }
        }
    }
}

