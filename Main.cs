
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

            // IndexOf returns -1 if a saved color doesn't round-trip exactly to a
            // known one; fall back to the first color instead of crashing on load.
            fontColor = colorsNames[Mathf.Max(0, colors.IndexOf(settings.fontColor))];
            fontColorAccent = colorsNames[Mathf.Max(0, colors.IndexOf(settings.fontColorAccent))];

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
        static string blockNameInput = ""; // manual "block by name" field in settings
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
                    GUILayout.Space(e == null ? 12 : 2);

                    if (e != null) {
                        if (eventManager.isEventOwner)
                        {
                            if (eventManager.race != null)
                            {
                                GUILayout.Label("Race (preview)", text);
                                GUILayout.Space(4);

                                if (e.state == EventState.Stopped)
                                {
                                    int cpCount = eventManager.race.checkpoints.Count;

                                    bool placing = Main.cursor != null && Main.cursor.active;
                                    if (GUILayout.Button(placing ? "Done Placing" : "Add Checkpoint", GUILayout.Height(42f), GUILayout.Width(212f)))
                                    {
                                        if (placing) Utils.DisableCursor();
                                        else Utils.EnableCursor();
                                    }
                                    GUILayout.Label("Checkpoints: " + cpCount, text);
                                    if (cpCount > 0 && GUILayout.Button("Clear Checkpoints", GUILayout.Height(28f), GUILayout.Width(212f)))
                                    {
                                        eventManager.ClearRaceCheckpoints();
                                    }
                                    GUILayout.Space(6);

                                    GUILayout.BeginHorizontal();
                                    GUILayout.Label("Laps", GUILayout.Width(60));
                                    if (GUILayout.Button("-", GUILayout.Width(28))) eventManager.race.laps = Mathf.Max(1, eventManager.race.laps - 1);
                                    GUILayout.Label(eventManager.race.laps.ToString(), GUILayout.Width(24));
                                    if (GUILayout.Button("+", GUILayout.Width(28))) eventManager.race.laps = Mathf.Min(GameConfig.MaxRaceLaps, eventManager.race.laps + 1);
                                    GUILayout.EndHorizontal();
                                    GUILayout.Space(6);

                                    // Lobby is optional - invite others to join. You can also start solo.
                                    if (!eventManager.raceLobbyOpen)
                                    {
                                        if (GUILayout.Button("Open Lobby", GUILayout.Height(28f), GUILayout.Width(212f)))
                                            eventManager.OpenRaceLobby();
                                    }
                                    else
                                    {
                                        GUILayout.Label("Lobby open - joined: " + eventManager.raceJoined.Count, text);
                                        if (GUILayout.Button("Close Lobby", GUILayout.Height(28f), GUILayout.Width(212f)))
                                            eventManager.CancelRaceLobby();
                                    }
                                    GUILayout.Space(6);

                                    // Start is always available once there's at least one checkpoint.
                                    if (cpCount > 0)
                                    {
                                        if (GUILayout.Button("Start Race", GUILayout.Height(42f), GUILayout.Width(212f)))
                                            eventManager.StartRace();
                                    }
                                    else GUILayout.Label("Add at least one checkpoint to start", text);

                                    GUILayout.Space(12);
                                    if (GUILayout.Button("<", GUILayout.Height(42f), GUILayout.Width(42f)))
                                    {
                                        eventManager.Disable(true);
                                        eventManager.Reset();
                                    }
                                }
                                else
                                {
                                    if (GUILayout.Button("Stop Race", GUILayout.Height(42f), GUILayout.Width(212f)))
                                    {
                                        eventManager.StopRace();
                                    }
                                }
                            }
                        }

                        if (eventManager.SKATE != null)
                        {
                            GUILayout.Label("Game of S.K.A.T.E.", text);
                            GUILayout.Space(8);

                            if (e.state == EventState.Stopped || e.state == EventState.End)
                            {
                                if (eventManager.isEventOwner)
                                {
                                    if (eventManager.pendingInviteTo == "")
                                    {
                                        GUILayout.Label("Select opponent (must have the mod): ");
                                        string[] players = Utils.getListOfPlayers(true);
                                        eventManager.SKATE.opponent = RGUI.SelectionPopup(eventManager.SKATE.opponent, players);
                                        if (players.Length == 0) GUILayout.Label("No other players with the mod detected", text);

                                        if (eventManager.SKATE.opponent != "")
                                        {
                                            GUILayout.BeginHorizontal();
                                            if (GUILayout.Button("Invite", GUILayout.Height(42f), GUILayout.Width(150f)))
                                            {
                                                eventManager.InviteOpponent();
                                            }
                                            if (GUILayout.Button("Block", GUILayout.Height(42f), GUILayout.Width(90f)))
                                            {
                                                Utils.BlockPlayer(Utils.NickOf(eventManager.SKATE.opponent), Utils.UserIdOf(eventManager.SKATE.opponent));
                                                eventManager.SKATE.opponent = "";
                                            }
                                            GUILayout.EndHorizontal();
                                        }

                                        GUILayout.Space(12);
                                        if (GUILayout.Button("<", GUILayout.Height(42f), GUILayout.Width(42f)))
                                        {
                                            eventManager.Disable(true);
                                            eventManager.Reset();
                                        }
                                    }
                                    else
                                    {
                                        GUILayout.Label("Waiting for " + eventManager.pendingInviteNick + " to accept...", text);
                                        GUILayout.Label("Word: " + GameConfig.NormalizeSkateWord(eventManager.agreedSkateWord), text);
                                        GUILayout.Space(8);
                                        if (GUILayout.Button("Cancel invite", GUILayout.Height(42f), GUILayout.Width(212f)))
                                        {
                                            eventManager.CancelInvite();
                                        }
                                    }
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
                    else
                    {
                        if (GUILayout.Button("Game of S.K.A.T.E.", GUILayout.Height(42f), GUILayout.Width(212f)))
                        {
                            eventManager.CreateEvent(EventType.SKATE, new object[] {});
                        }

                        if (eventManager.lastSkateOpponent != "")
                        {
                            GUILayout.Space(8);
                            if (GUILayout.Button("Rematch " + Utils.NickOf(eventManager.lastSkateOpponent), GUILayout.Height(42f), GUILayout.Width(212f)))
                            {
                                eventManager.Rematch();
                            }
                        }

                        // Race (preview): lobby/invite based, and fully self-scoped by raceId so it
                        // can't disturb other players' events. Anyone can host - no admin needed.
                        GUILayout.Space(8);
                        if (GUILayout.Button("Create Race (preview)", GUILayout.Height(42f), GUILayout.Width(212f)))
                        {
                            eventManager.CreateEvent(EventType.Race, new object[] { });
                        }
                        if (eventManager.lastRaceCheckpoints.Count > 0)
                        {
                            GUILayout.Space(6);
                            if (GUILayout.Button("Rematch Race (reuse course)", GUILayout.Height(42f), GUILayout.Width(212f)))
                            {
                                eventManager.RematchRace();
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
                float lw = 190f; // label column width; keeps every control aligned down the panel

                GUILayout.Label("Settings", title);
                GUILayout.Space(2);
                GUILayout.Label("Changes apply live; save to persist between sessions", text);
                GUILayout.Space(10);

                // --- Appearance ---
                GUILayout.Label("Appearance", subtitle);
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Active letters color", GUILayout.Width(lw));
                fontColorAccent = colorsNames[colorsNames.IndexOf(RGUI.SelectionPopup(fontColorAccent, colorsNames.ToArray()))];
                settings.fontColorAccent = colors[colorsNames.IndexOf(fontColorAccent)];
                GUILayout.EndHorizontal();
                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Inactive letters color", GUILayout.Width(lw));
                fontColor = colorsNames[colorsNames.IndexOf(RGUI.SelectionPopup(fontColor, colorsNames.ToArray()))];
                settings.fontColor = colors[colorsNames.IndexOf(fontColor)];
                GUILayout.EndHorizontal();

                GUILayout.Space(12);

                // --- Gameplay ---
                GUILayout.Label("Gameplay", subtitle);
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Max retries per turn", GUILayout.Width(lw));
                if (GUILayout.Button("-", GUILayout.Width(28))) settings.maxRetries = Mathf.Max(0, settings.maxRetries - 1);
                GUILayout.Label(settings.maxRetries.ToString(), GUILayout.Width(24));
                if (GUILayout.Button("+", GUILayout.Width(28))) settings.maxRetries = Mathf.Min(5, settings.maxRetries + 1);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Ignore manuals under (s)", GUILayout.Width(lw));
                if (GUILayout.Button("-", GUILayout.Width(28))) settings.smallManualMaxSeconds = Mathf.Max(0f, settings.smallManualMaxSeconds - 0.05f);
                GUILayout.Label(settings.smallManualMaxSeconds.ToString("0.00"), GUILayout.Width(36));
                if (GUILayout.Button("+", GUILayout.Width(28))) settings.smallManualMaxSeconds = Mathf.Min(1f, settings.smallManualMaxSeconds + 0.05f);
                GUILayout.EndHorizontal();
                GUILayout.Label("A tiny manual before a pop won't change the trick", text);
                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label("S.K.A.T.E. word (host)", GUILayout.Width(lw));
                settings.skateWord = GUILayout.TextField(settings.skateWord, GameConfig.MaxSkateWordLength, GUILayout.Width(120));
                GUILayout.EndHorizontal();
                GUILayout.Label("Letters & numbers. In game: " + GameConfig.NormalizeSkateWord(settings.skateWord), text);

                GUILayout.Space(12);

                // --- Moderation ---
                GUILayout.Label("Moderation", subtitle);
                GUILayout.Space(2);
                GUILayout.Label("Blocked players can't invite you and won't appear in event lists.", text);
                GUILayout.Space(6);

                if (settings.blockedPlayers.Count == 0)
                {
                    GUILayout.Label("No blocked players", text);
                }
                else
                {
                    string toRemove = null;
                    for (int i = 0; i < settings.blockedPlayers.Count; i++)
                    {
                        string entry = settings.blockedPlayers[i];
                        string nick = Utils.NickOf(entry);
                        string label = nick != "" ? nick : Utils.UserIdOf(entry);

                        GUILayout.BeginHorizontal();
                        GUILayout.Label(label, GUILayout.Width(lw));
                        if (GUILayout.Button("Unblock", GUILayout.Width(90f))) toRemove = entry;
                        GUILayout.EndHorizontal();
                    }
                    if (toRemove != null) Utils.UnblockPlayer(toRemove); // deferred so we don't mutate mid-iteration
                }
                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Block by name", GUILayout.Width(lw));
                blockNameInput = GUILayout.TextField(blockNameInput, 32, GUILayout.Width(120));
                if (GUILayout.Button("Block", GUILayout.Width(70f)))
                {
                    Utils.BlockPlayer(blockNameInput, "");
                    blockNameInput = "";
                }
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