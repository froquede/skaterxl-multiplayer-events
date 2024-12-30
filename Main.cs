
using HarmonyLib;
using RapidGUI;
using System;
using System.Collections.Generic;
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

            Utils.Log("Loaded " + modEntry.Info.Id);
            UnityEngine.Object.DontDestroyOnLoad(go);

            colors = new List<Color>
            {
                Color.white,
                Color.gray,
                Color.red,
                Color.blue,
                Color.green,
                Color.cyan,
                Color.black,
                Color.magenta,
                Color.yellow
            };

            colorsNames = new List<string>
            {
                "White",
                "Gray",
                "Red",
                "Blue",
                "Green",
                "Cyan",
                "Black",
                "Magenta",
                "Yellow"
            };

            fontColor = colorsNames[colors.IndexOf(settings.fontColor)];
            fontColorAccent = colorsNames[colors.IndexOf(settings.fontColorAccent)];

            return true;
        }
        static bool Unload(UnityModManager.ModEntry modEntry)
        {

            try
            {
                if (eventManager.race != null) eventManager.race.Disable();
                eventManager.Disable();

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
        static GUIStyle subtitle = new GUIStyle();
        static GUIStyle text = new GUIStyle();
        static GUIStyle box = new GUIStyle("Box");
        static int width = 396;
        static int padding = 14;

        static void Style()
        {
            title.fontSize = 16;
            title.normal.textColor = Color.white;
            subtitle.fontSize = 13;
            subtitle.normal.textColor = new Color32(212, 212, 212, 255);
            text.fontSize = 12;
            text.normal.textColor = Color.gray;
            box.padding.left = box.padding.right = box.padding.top = box.padding.bottom = padding;
        }

        static List<Color> colors;
        static List<string> colorsNames;
        static string fontColor, fontColorAccent;
        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Style();

            GUILayout.BeginHorizontal(box);
            GUILayout.BeginVertical(GUILayout.Width(440));
            {
                GUILayout.Label("Event", title);
                if (Utils.isOnline())
                {
                    Event e = eventManager.multiplayerEvent;
                    if (e != null) {
                        if (eventManager.admin)
                        {
                            if (eventManager.race != null)
                            {
                                if (e.state == EventState.Stopped)
                                {
                                    if (GUILayout.Button("Add Checkpoint", GUILayout.Height(42f), GUILayout.Width(212f)))
                                    {
                                        Utils.EnableCursor();
                                    }
                                    if (GUILayout.Button("Start Race", GUILayout.Height(42f), GUILayout.Width(212f)))
                                    {
                                        eventManager.StartEvent();
                                    }

                                    GUILayout.Space(12);
                                    if (GUILayout.Button("<", GUILayout.Height(42f), GUILayout.Width(42f)))
                                    {
                                        eventManager.Disable();
                                        eventManager.Reset();
                                    }
                                }
                                else
                                {
                                    if (GUILayout.Button("Stop Race", GUILayout.Height(42f), GUILayout.Width(212f)))
                                    {
                                        eventManager.StopEvent();
                                    }
                                }
                            }

                            if(eventManager.SKATE != null)
                            {
                                if (e.state == EventState.Stopped)
                                {
                                    GUILayout.Label("Select opponent: ");
                                    eventManager.SKATE.opponent = RGUI.SelectionPopup(eventManager.SKATE.opponent, Utils.getListOfPlayers());

                                    if (eventManager.SKATE.opponent != "" && GUILayout.Button("Start game", GUILayout.Height(42f), GUILayout.Width(212f)))
                                    {
                                        eventManager.StartEvent(eventManager.SKATE.opponentUserID);
                                    }

                                    GUILayout.Space(12);
                                    if (GUILayout.Button("<", GUILayout.Height(42f), GUILayout.Width(42f)))
                                    {
                                        eventManager.Disable();
                                        eventManager.Reset();
                                    }
                                }
                                else
                                {
                                    if (GUILayout.Button("Stop game", GUILayout.Height(42f), GUILayout.Width(212f)))
                                    {
                                        eventManager.StopEvent();
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Game of S.K.A.T.E.", GUILayout.Height(42f), GUILayout.Width(212f)))
                        {
                            eventManager.CreateEvent(EventType.SKATE, new object[] {});
                        }

                        if (Utils.isAdmin())
                        {
                            if (GUILayout.Button("Create Race (not working yet)", GUILayout.Height(42f), GUILayout.Width(212f)))
                            {
                                eventManager.CreateEvent(EventType.Race, new object[] { });
                            }
                        }
                    }
                }
                else
                {
                    GUILayout.Space(2);
                    GUILayout.Label("Connect to multiplayer for creating events", text);
                }
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(440));
            {
                GUILayout.Label("Settings", title);
                GUILayout.Space(2);
                GUILayout.Label("Save and reload the mod to apply changes", text);
                GUILayout.Space(8);

                GUILayout.BeginHorizontal();
                GUILayout.Label("S.K.A.T.E. letters active color: ");
                fontColorAccent = colorsNames[colorsNames.IndexOf(RGUI.SelectionPopup(fontColorAccent, colorsNames.ToArray()))];
                settings.fontColorAccent = colors[colorsNames.IndexOf(fontColorAccent)];
                GUILayout.EndHorizontal();
                GUILayout.Space(8);

                GUILayout.BeginHorizontal();
                GUILayout.Label("S.K.A.T.E. letters disabled color: ");
                fontColor = colorsNames[colorsNames.IndexOf(RGUI.SelectionPopup(fontColor, colorsNames.ToArray()))];
                settings.fontColor = colors[colorsNames.IndexOf(fontColor)];
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            settings.Draw(modEntry);
        }

        static float Slider(string title, float value, float min, float max, float step, float default_value, string subtext = "")
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label("<b>" + title + "</b>", subtitle, GUILayout.Width(width));
            if (subtext != "")
            {
                GUILayout.Space(2);
                GUILayout.Label(subtext, text, GUILayout.Width(width));
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = Color.white;
            float result = GUILayout.HorizontalScrollbar(value, step, min, max + step);
            if (GUILayout.Button("reset", GUILayout.Height(20), GUILayout.Width(60))) result = default_value;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            return result;
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }
    }
}