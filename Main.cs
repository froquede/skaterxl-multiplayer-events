
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace MultiplayerEvents
{
    [EnableReloading]
    static class Main
    {
        public static Settings settings;
        public static Harmony harmonyInstance;
        public static UnityModManager.ModEntry modEntry;
        public static GameObject go;
        public static Assembly assembly;
        public static MultiplayerEventManager eventManager;
        public static Tick tick;
        public static Cursor cursor;
        public static NativeMenu nativeMenu;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            harmonyInstance = new Harmony(modEntry.Info.Id);
            go = new GameObject("MultiplayerEvents");

            GameObject c = new GameObject("Cursor");
            cursor = c.AddComponent<Cursor>();

            tick = go.AddComponent<Tick>();
            eventManager = new MultiplayerEventManager();

            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = new Action<UnityModManager.ModEntry>(OnSaveGUI);
            modEntry.OnToggle = new Func<UnityModManager.ModEntry, bool, bool>(OnToggle);
            modEntry.OnUnload = Unload;
            Main.modEntry = modEntry;

            assembly = Assembly.GetExecutingAssembly();
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            // Event controls and settings live in the game's own menus (see NativeMenu).
            nativeMenu = go.AddComponent<NativeMenu>();

            Utils.Log("Loaded " + modEntry.Info.Id);
            UnityEngine.Object.DontDestroyOnLoad(go);

            return true;
        }
        static bool Unload(UnityModManager.ModEntry modEntry)
        {

            try
            {
                if (nativeMenu != null) nativeMenu.Uninstall();
                eventManager.Disable(); // also disables the race/SKATE if one is live

                UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(cursor.gameObject);

                harmonyInstance.UnpatchAll(harmonyInstance.Id);
            }
            catch { }

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Utils.Log("Toggled " + modEntry.Info.Id);
            return true;
        }

        static GUIStyle title = new GUIStyle();
        static GUIStyle text = new GUIStyle();
        static GUIStyle box = new GUIStyle("Box");
        static int padding = 14;

        static void Style()
        {
            title.fontSize = 16;
            title.normal.textColor = Color.white;
            text.fontSize = 12;
            text.normal.textColor = Color.gray;
            box.padding.left = box.padding.right = box.padding.top = box.padding.bottom = padding;
        }

        // The UMM window only points to the native menus now.
        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Style();

            GUILayout.BeginVertical(box, GUILayout.Width(440));
            {
                GUILayout.Label("Multiplayer Events", title);
                GUILayout.Space(4);
                GUILayout.Label("Events: Pause > Multiplayer > Events (while in a room)", text);
                GUILayout.Label("Settings & blocked players: Pause > Settings > Multiplayer Events", text);
            }
            GUILayout.EndVertical();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }
    }
}
