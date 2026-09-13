using GameManagement;
using HarmonyLib;
using I2.Loc;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MultiplayerEvents
{
    /// <summary>
    /// Hosts the mod's controls inside Skater XL's own menus (this used to be the UMM window):
    ///  - Pause > Multiplayer > "Events" button, opening an events page styled like
    ///    the other multiplayer sub-menus (S.K.A.T.E. invites, race setup/lobby, rematch, stop).
    ///  - Pause > Settings > "Multiplayer Events" category with the mod settings + block list.
    /// The in-world HUD and prompts (Tick) are untouched. Everything is installed lazily once the
    /// game's menus exist and torn down again in <see cref="Uninstall"/> (hot reload).
    /// </summary>
    class NativeMenu : MonoBehaviour
    {
        const string Title = "Multiplayer Events";       // events page header + settings category
        const string MainMenuLabel = "Events";           // button on the top multiplayer page
        const string SettingsAnchorCategory = "Multiplayer Settings"; // our category goes right after it
        const float SettingsWaitSeconds = 10f; // give up waiting for the anchor category after this
        const float ManualStep = 0.05f;
        const int ManualSteps = 20; // 0..1s in 0.05s steps
        const float ClickGuardSeconds = 0.35f; // ignore presses this soon after a click or page change
        const float ConfirmSeconds = 3f;       // window for the second press on destructive buttons

        // Row heights (the page layout uses each row's own height; the page adds 16px between rows).
        const float HeaderHeight = 88f;
        const float InfoHeight = 40f;
        const float SpacerHeight = 16f;
        static readonly Color Accent = new Color32(52, 134, 240, 255); // the menu's selection blue

        // Letter color palette (kept as named colors so saved settings round-trip exactly).
        public static readonly Color[] Colors = { Color.white, Color.gray, Color.red, Color.blue, Color.green, Color.cyan, Color.black, Color.magenta, Color.yellow };
        public static readonly string[] ColorNames = { "White", "Gray", "Red", "Blue", "Green", "Cyan", "Black", "Magenta", "Yellow" };

        enum EventsView { Offline, Idle, SkateWaiting, SkatePlaying, RaceSetup, RaceHosting, RaceJoined, Joining }

        MultiplayerMenuController menuController;
        MenuButton mpButton;
        MultiplayerMainMenu.ButtonVisibilityDef mpButtonVisibility;
        MenuButton playerInviteButton; // on the native player info page
        float nextPlayerInviteRefresh;
        GameObject eventsMenu;
        ProceduralMenuPage eventsPage;
        ProceduralMenuPage settingsPage;

        bool installingEvents, eventsFailed;
        bool installingSettings, settingsFailed;
        bool uninstalled;
        float initializedAt = -1f;

        readonly PageState events = new PageState();
        readonly PageState settingsState = new PageState();
        static GameObject buttonTemplate; // native multiplayer menu button, cloned for page rows
        static float clickGuardUntil;
        string selectedOpponent = ""; // "Nick | UserId"
        static string selectedBlockTarget = ""; // "Nick | UserId", settings page block picker
        bool pendingPlacement;        // enter checkpoint placement once back in PlayState
        bool reopenEvents;            // reopen the events page when the multiplayer menu comes back
        EventsView lastView = EventsView.Offline;

        // Rebuild bookkeeping for a page whose items depend on changing state.
        class PageState
        {
            public string builtSignature;
            public bool rebuilding;
            public float nextCheck;
        }

        void Update()
        {
            // Touching the game's singletons before it initialized them can break their
            // one-time lookup, so wait for the state machine first.
            if (uninstalled || !GameStateMachine.IsInitialized) return;
            if (initializedAt < 0f) initializedAt = Time.unscaledTime;

            if (menuController == null && !installingEvents && !eventsFailed) InstallEventsMenu();
            if (settingsPage == null && !installingSettings && !settingsFailed) TryInstallSettings();

            if (eventsPage != null) UpdateEventsPage();
            if (menuController != null && menuController.playerInfoMenu.isActiveAndEnabled && Time.unscaledTime >= nextPlayerInviteRefresh)
            {
                nextPlayerInviteRefresh = Time.unscaledTime + 0.25f;
                RefreshPlayerInviteButton();
            }
            if (settingsPage != null) UpdateSettingsPage();
            FixSettingsTitle();
            DiscardAbandonedSetup();
            EnterPendingPlacement();
        }

        // --- Multiplayer menu: button + events page ------------------------------

        async void InstallEventsMenu()
        {
            MultiplayerManager mm = MultiplayerManager.Instance;
            if (mm == null || mm.menuController == null || mm.menuController.mainMenu == null) return;

            installingEvents = true;
            GameObject holder = null;
            try
            {
                MultiplayerMenuController mc = mm.menuController;

                // Clones are built under an inactive holder so none of their Awake/OnEnable
                // (player list callbacks, localization) run before we strip them.
                holder = new GameObject("MultiplayerEvents clone holder");
                holder.SetActive(false);

                CreateMainMenuButton(mc, holder.transform);
                Transform optionsArea = CreateEventsMenuFrame(mc, holder.transform);
                CreatePlayerInviteButton(mc, holder.transform);
                menuController = mc;

                GameObject pageGo = await Addressables.InstantiateAsync(ProceduralMenuPage.prefabKey, optionsArea).Task;
                if (uninstalled)
                {
                    Addressables.ReleaseInstance(pageGo);
                    return;
                }
                eventsPage = pageGo.GetComponent<ProceduralMenuPage>();
                eventsPage.layoutGroup.spacing = 16f; // same as the settings pages
                events.builtSignature = null;
                Utils.Log("Native multiplayer menu installed");
            }
            catch (Exception e)
            {
                eventsFailed = true;
                Utils.Log("Native multiplayer menu failed: " + e);
                RemoveMainMenuButton(); // don't leave a button that opens nothing
                if (eventsMenu != null) Destroy(eventsMenu);
                eventsMenu = null;
            }
            finally
            {
                if (holder != null) Destroy(holder);
                installingEvents = false;
            }
        }

        void RemoveMainMenuButton()
        {
            MultiplayerManager mm = MultiplayerManager.Instance;
            if (mpButtonVisibility != null && mm != null && mm.menuController != null) mm.menuController.mainMenu.options.Remove(mpButtonVisibility);
            if (mpButton != null) Destroy(mpButton.gameObject);
            mpButtonVisibility = null;
            mpButton = null;
        }

        static GameObject FindOption(MultiplayerMainMenu menu, string name)
        {
            MultiplayerMainMenu.ButtonVisibilityDef def = menu.options.FirstOrDefault(o => o != null && o.buttonGO != null && o.buttonGO.name == name);
            return def != null ? def.buttonGO : null;
        }

        void CreateMainMenuButton(MultiplayerMenuController mc, Transform holder)
        {
            MultiplayerMainMenu main = mc.mainMenu;
            GameObject template = FindOption(main, "Settings Button") ?? FindOption(main, "Exit Button");
            if (template == null || template.GetComponent<MenuButton>() == null) throw new Exception("No multiplayer menu button to clone");
            buttonTemplate = template;
            GameObject anchor = FindOption(main, "Players Button") ?? template;

            GameObject go = Instantiate(template, holder, false);
            go.name = "Events Button";
            StripLocalization(go);

            mpButton = go.GetComponent<MenuButton>();
            // Fresh events: the cloned ones still carry the template's serialized calls.
            mpButton.onClick = new Button.ButtonClickedEvent();
            mpButton.onGreyedOutClick = new Button.ButtonClickedEvent();
            mpButton.onClick.AddListener(ShowEventsMenu);
            mpButton.GreyedOut = false;
            mpButton.SetText(MainMenuLabel, true);

            go.SetActive(PhotonNetwork.InRoom);
            go.transform.SetParent(anchor.transform.parent, false);
            go.transform.SetSiblingIndex(anchor.transform.GetSiblingIndex()); // right above "Players"

            mpButtonVisibility = new MultiplayerMainMenu.ButtonVisibilityDef
            {
                name = go.name,
                buttonGO = go,
                showInPublicRoom = true,
                showInPrivateRoom = true,
                showInGameMode = true,
            };
            main.options.Add(mpButtonVisibility);
        }

        // Adds "Invite to Game of S.K.A.T.E." under the native Block button on the player info page.
        void CreatePlayerInviteButton(MultiplayerMenuController mc, Transform holder)
        {
            MultiplayerPlayerInfoMenu info = mc.playerInfoMenu;
            MenuButton anchor = info.BlockPlayerButton != null ? info.BlockPlayerButton : info.ShowProfileButton;
            if (anchor == null) return; // optional; the events page still covers invites

            GameObject go = Instantiate(anchor.gameObject, holder, false);
            go.name = "Invite To SKATE Button";
            StripLocalization(go);

            playerInviteButton = go.GetComponent<MenuButton>();
            playerInviteButton.onClick = new Button.ButtonClickedEvent();
            playerInviteButton.onGreyedOutClick = new Button.ButtonClickedEvent();
            Action onClick = Guarded(OnPlayerInviteClicked);
            playerInviteButton.onClick.AddListener(() => onClick());

            go.SetActive(false); // RefreshPlayerInviteButton decides per viewed player
            go.transform.SetParent(anchor.transform.parent, false);
            go.transform.SetSiblingIndex(anchor.transform.GetSiblingIndex() + 1);
        }

        static string PlayerIdOf(NetworkPlayerController p)
        {
            return p.NickName + GameConfig.PlayerIdSeparator + p.UserId;
        }

        // Called when the info page refreshes (via Harmony) and periodically while it is open.
        public void RefreshPlayerInviteButton()
        {
            if (playerInviteButton == null || menuController == null) return;

            NetworkPlayerController p = menuController.playerInfoMenu.viewedPlayer;
            MultiplayerEventManager m = Main.eventManager;
            bool show = p != null && !p.IsLocal && Utils.isOnline()
                && Utils.GetModdedUserIds().Contains(p.UserId) && !Utils.IsBlocked(p.UserId, p.NickName);
            if (!show)
            {
                if (playerInviteButton.gameObject.activeSelf) playerInviteButton.gameObject.SetActive(false);
                return;
            }

            string label = "Invite to Game of S.K.A.T.E.";
            bool greyed = false;
            if (m.pendingInviteTo == p.UserId) label = "Cancel S.K.A.T.E. invite";
            else if (ViewOf(m) != EventsView.Idle) greyed = true;

            if (!playerInviteButton.gameObject.activeSelf) playerInviteButton.gameObject.SetActive(true);
            if (playerInviteButton.Label != null && playerInviteButton.Label.text != label) playerInviteButton.SetText(label, true);
            if (playerInviteButton.GreyedOut != greyed)
            {
                playerInviteButton.GreyedOut = greyed;
                playerInviteButton.GreyedOutInfoText = greyed ? "Finish or leave your current event first" : "";
                playerInviteButton.RefreshSelectionState();
            }
        }

        void OnPlayerInviteClicked()
        {
            NetworkPlayerController p = menuController != null ? menuController.playerInfoMenu.viewedPlayer : null;
            if (p == null) return;

            MultiplayerEventManager m = Main.eventManager;
            if (m.pendingInviteTo == p.UserId)
            {
                m.CancelInvite();
                RefreshPlayerInviteButton();
                return;
            }
            if (ViewOf(m) != EventsView.Idle) return;

            InviteToSkate(PlayerIdOf(p));
            CloseMenus(); // the game starts when they accept
        }

        // Reuses the player list sub-menu as a frame (canvas, right-hand panel, header) so the
        // page looks native, and returns the area the procedural page goes into.
        Transform CreateEventsMenuFrame(MultiplayerMenuController mc, Transform holder)
        {
            GameObject src = mc.playerListMenu.gameObject;
            GameObject menu = Instantiate(src, holder, false);
            menu.name = "MultiplayerEventsMenu";

            DestroyImmediate(menu.GetComponent<MultiplayerPlayerListMenu>());
            foreach (FixFirstSelected f in menu.GetComponents<FixFirstSelected>()) DestroyImmediate(f);

            Transform panel = menu.transform.Find("Panel");
            if (panel == null) throw new Exception("Player list menu has no Panel");
            DestroyChild(panel, "Player List");
            DestroyChild(panel, "Button Area");
            StripLocalization(menu);

            MVCListHeaderView header = panel.GetComponentInChildren<MVCListHeaderView>(true);
            if (header != null)
            {
                header.interactable = false;
                header.SetText(Title, true);
                TMP_Text count = header.GetObject<TMP_Text>("PlayerCountLabel");
                if (count != null) count.SetText("");
            }

            // Same placement as the Settings menu's "Options Area" below its header.
            RectTransform area = new GameObject("Options Area", typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(panel, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.pivot = new Vector2(0.5f, 0.5f);
            area.anchoredPosition = new Vector2(0f, -32f);
            area.sizeDelta = new Vector2(0f, -192f);

            menu.SetActive(false);
            menu.transform.SetParent(src.transform.parent, false);
            menu.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
            eventsMenu = menu;
            return area;
        }

        static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) DestroyImmediate(child.gameObject);
        }

        // Our labels are plain text; a leftover I2 Localize would overwrite them with the
        // template's term (or blank them) on the next language refresh.
        static void StripLocalization(GameObject go)
        {
            foreach (Localize l in go.GetComponentsInChildren<Localize>(true)) DestroyImmediate(l);
        }

        void ShowEventsMenu()
        {
            if (eventsMenu == null || menuController == null) return;

            // Same pattern as MultiplayerMenuController.View*: hide every sub-menu, show ours.
            menuController.mainMenu.gameObject.SetActive(false);
            menuController.roomList.gameObject.SetActive(false);
            menuController.roomInfo.gameObject.SetActive(false);
            menuController.gameModeMenu.gameObject.SetActive(false);
            menuController.playerListMenu.gameObject.SetActive(false);
            menuController.playerInfoMenu.gameObject.SetActive(false);
            eventsMenu.SetActive(true);
            GuardClicks();
        }

        // Called (via Harmony) whenever the game switches to one of its own sub-menus.
        public void HideEventsMenu()
        {
            if (eventsMenu != null && eventsMenu.activeSelf) eventsMenu.SetActive(false);
        }

        // Called (via Harmony) when the multiplayer menu is shown again. The game always opens it
        // on its top page; come back to ours if we only left it for Settings or placement.
        public void OnMultiplayerMenuEnabled()
        {
            if (!reopenEvents) return;
            reopenEvents = false;
            if (Utils.isOnline()) ShowEventsMenu();
        }

        void BackToMainMenu()
        {
            reopenEvents = false;
            HideEventsMenu();
            if (menuController != null) menuController.ViewMainMpMenu();
        }

        // Leave the menus for gameplay (used when something starts, so a stray press on the
        // rebuilt page can't stop it).
        void CloseMenus()
        {
            reopenEvents = false;
            HideEventsMenu();
            GameStateMachine gsm = GameStateMachine.Instance;
            if (gsm != null && !(gsm.CurrentState is PlayState)) gsm.RequestPlayState();
        }

        void OpenSettings()
        {
            if (settingsPage != null) SettingsMenuController.Instance.SetCurrentCategory(Title);
            reopenEvents = true; // Back from Settings returns to this page
            GameStateMachine.Instance.RequestSettingsState();
        }

        void UpdateEventsPage()
        {
            bool visible = eventsMenu != null && eventsMenu.activeInHierarchy;
            EventsView view = ViewOf(Main.eventManager);

            if (visible)
            {
                // Mirrors the other sub-menus: B/Esc goes back, leaving the room closes the page.
                if (view == EventsView.Offline || MyInputModule.CheckUnusedCancelEvent)
                {
                    BackToMainMenu();
                    return;
                }
                // Something started while the page was open (e.g. the invitee accepted): get out of
                // the menu instead of leaving focus on the rebuilt page's Stop button.
                if (view != lastView && IsLive(view))
                {
                    lastView = view;
                    CloseMenus();
                    return;
                }
            }
            lastView = view;

            if (events.rebuilding || Time.unscaledTime < events.nextCheck) return;
            events.nextCheck = Time.unscaledTime + (visible ? 0.1f : 0.5f);

            if (EventsSignature() != events.builtSignature) RebuildPage(eventsPage, events, EventsSignature, BuildEventsItems, visible);
        }

        static bool IsLive(EventsView view)
        {
            return view == EventsView.SkatePlaying || view == EventsView.RaceHosting || view == EventsView.RaceJoined;
        }

        static EventsView ViewOf(MultiplayerEventManager m)
        {
            if (!Utils.isOnline()) return EventsView.Offline;
            if (m.multiplayerEvent == null) return EventsView.Idle;

            if (m.race != null)
            {
                if (!m.isEventOwner) return EventsView.RaceJoined;
                return m.multiplayerEvent.state == EventState.Stopped ? EventsView.RaceSetup : EventsView.RaceHosting;
            }
            if (m.SKATE != null)
            {
                if (m.multiplayerEvent.state == EventState.Running) return EventsView.SkatePlaying;
                if (m.isEventOwner) return m.pendingInviteTo != "" ? EventsView.SkateWaiting : EventsView.Idle;
            }
            return EventsView.Joining;
        }

        // Everything the page content depends on; a change triggers a rebuild.
        static string EventsSignature()
        {
            MultiplayerEventManager m = Main.eventManager;
            EventsView view = ViewOf(m);
            if (view == EventsView.Offline) return "offline";

            StringBuilder sb = new StringBuilder();
            sb.Append(view).Append('|').Append(m.pendingInviteNick).Append('|').Append(m.lastSkateOpponent)
              .Append('|').Append(m.lastRaceCheckpoints.Count > 0);
            if (m.race != null)
            {
                sb.Append('|').Append(m.race.checkpoints.Count).Append('|').Append(Main.cursor != null && Main.cursor.active)
                  .Append('|').Append(m.raceLobbyOpen).Append('|').Append(m.raceJoined.Count);
            }
            sb.Append('|').Append(string.Join(",", Utils.getListOfPlayers(true)));
            return sb.ToString();
        }

        async Task BuildEventsItems()
        {
            MultiplayerEventManager m = Main.eventManager;
            ProceduralMenuPage page = eventsPage;

            switch (ViewOf(m))
            {
                case EventsView.Offline:
                    AddInfo(page, "offline", "Connect to multiplayer for creating events");
                    break;

                case EventsView.Idle:
                    await BuildSkateSetup(m);
                    AddHeader(page, "raceHeader", "Race (preview)");
                    AddButton(page, "createRace", "Create Race", () =>
                    {
                        if (Main.eventManager.multiplayerEvent == null) Main.eventManager.CreateEvent(EventType.Race, new object[] { });
                    });
                    if (m.lastRaceCheckpoints.Count > 0)
                        AddButton(page, "rematchRace", "Rematch Race (reuse course)", () => Main.eventManager.RematchRace());
                    break;

                case EventsView.SkateWaiting:
                    AddHeader(page, "skateHeader", "Game of S.K.A.T.E.");
                    AddInfo(page, "waiting", "Waiting for " + m.pendingInviteNick + " to accept...");
                    AddInfo(page, "word", "Word: " + GameConfig.NormalizeSkateWord(m.agreedSkateWord));
                    AddButton(page, "cancelInvite", "Cancel invite", () => Main.eventManager.CancelInvite());
                    break;

                case EventsView.SkatePlaying:
                    AddHeader(page, "skateHeader", "Game of S.K.A.T.E.");
                    AddInfo(page, "playing", "Playing against " + m.SKATE.opponentNickname);
                    AddConfirmButton(page, "stop", "Stop game", () => Main.eventManager.StopEvent());
                    break;

                case EventsView.RaceSetup:
                    await BuildRaceSetup(m);
                    break;

                case EventsView.RaceHosting:
                    AddHeader(page, "raceHeader", "Race (preview)");
                    AddInfo(page, "hosting", "Race in progress");
                    AddConfirmButton(page, "stopRace", "Stop Race", () => Main.eventManager.StopRace());
                    break;

                case EventsView.RaceJoined:
                    AddHeader(page, "raceHeader", "Race (preview)");
                    AddInfo(page, "joined", "You're in this race");
                    AddConfirmButton(page, "leaveRace", "Leave Race", () => Main.eventManager.LeaveRace());
                    break;

                default:
                    AddInfo(page, "joining", "Joining event...");
                    break;
            }

            AddSpacer(page, "footerSpacer");
            AddButton(page, "settings", "Settings", OpenSettings);
            AddButton(page, "back", "Back", BackToMainMenu);
        }

        async Task BuildSkateSetup(MultiplayerEventManager m)
        {
            ProceduralMenuPage page = eventsPage;
            AddHeader(page, "skateHeader", "Game of S.K.A.T.E.");

            string[] opponents = Utils.getListOfPlayers(true);
            if (opponents.Length == 0)
            {
                AddInfo(page, "noPlayers", "No other players with the mod detected");
            }
            else
            {
                if (Array.IndexOf(opponents, selectedOpponent) < 0) selectedOpponent = opponents[0];
                MenuPageItemBase picker = await page.AddStringEnumSetting("opponent", "Opponent",
                    () => Mathf.Max(0, Array.IndexOf(opponents, selectedOpponent)),
                    i => selectedOpponent = opponents[Mathf.Clamp(i, 0, opponents.Length - 1)],
                    opponents.Select(Utils.NickOf));
                picker.localize = false;

                AddButton(page, "invite", "Invite", () => { InviteToSkate(selectedOpponent); CloseMenus(); });
            }

            string last = m.lastSkateOpponent;
            if (last != "" && Utils.GetNetworkPlayer(Utils.UserIdOf(last)) != null && !Utils.IsBlocked(last))
            {
                AddButton(page, "rematch", "Rematch " + Utils.NickOf(last), () => { InviteToSkate(last); CloseMenus(); });
            }
        }

        async Task BuildRaceSetup(MultiplayerEventManager m)
        {
            ProceduralMenuPage page = eventsPage;
            int cpCount = m.race.checkpoints.Count;
            bool placing = Main.cursor != null && Main.cursor.active;

            AddHeader(page, "raceHeader", "Race (preview)");
            AddInfo(page, "checkpoints", "Checkpoints: " + cpCount);

            // Course
            AddButton(page, "placement", placing ? "Done Placing" : "Add Checkpoints", () =>
            {
                if (Main.cursor != null && Main.cursor.active) Main.cursor.ClearPlacement(); // exit + drop the half-placed preview gate
                else StartPlacement();
            });
            if (placing) AddInfo(page, "placingHelp", "A: drop a post (2 per gate)   X: undo   B: done");
            if (cpCount > 0)
            {
                AddButton(page, "removeLast", "Remove Last Checkpoint", () => Main.eventManager.RemoveLastRaceCheckpoint());
                AddConfirmButton(page, "clearCheckpoints", "Clear Checkpoints", () => Main.eventManager.ClearRaceCheckpoints());
            }

            await page.AddIntSetting("laps", "Laps",
                () => Main.eventManager.race != null ? Main.eventManager.race.laps : 1,
                v => { if (Main.eventManager.race != null) Main.eventManager.race.laps = Mathf.Clamp(v, 1, GameConfig.MaxRaceLaps); },
                1, GameConfig.MaxRaceLaps);

            // Lobby is optional - invite others to join. You can also start solo.
            AddSpacer(page, "lobbySpacer");
            if (!m.raceLobbyOpen)
            {
                AddButton(page, "openLobby", "Open Lobby", () => Main.eventManager.OpenRaceLobby());
            }
            else
            {
                AddInfo(page, "lobby", "Lobby open - joined: " + m.raceJoined.Count);
                AddButton(page, "closeLobby", "Close Lobby", () => Main.eventManager.CancelRaceLobby());
            }

            if (cpCount > 0) AddButton(page, "startRace", "Start Race", () => { Main.eventManager.StartRace(); CloseMenus(); });
            else AddInfo(page, "needCheckpoint", "Add at least one checkpoint to start");

            AddConfirmButton(page, "abortRace", "Cancel Race", () => Main.eventManager.AbortRaceSetup());
        }

        static void InviteToSkate(string playerId)
        {
            MultiplayerEventManager m = Main.eventManager;
            if (string.IsNullOrEmpty(playerId) || !Utils.isOnline()) return;

            if (m.multiplayerEvent == null) m.CreateEvent(EventType.SKATE, new object[] { }); // become owner
            if (m.SKATE == null || !m.isEventOwner || m.pendingInviteTo != "") return;

            m.SKATE.opponent = playerId;
            m.InviteOpponent();
        }

        // Placement needs the game running (camera/cursor input), so close the menus first and
        // turn the cursor on once PlayState has re-enabled gameplay and A has been released.
        void StartPlacement()
        {
            pendingPlacement = true;
            CloseMenus();
            reopenEvents = true; // pausing into Multiplayer comes back to the race setup
        }

        void EnterPendingPlacement()
        {
            if (!pendingPlacement) return;
            GameStateMachine gsm = GameStateMachine.Instance;
            if (gsm == null || gsm.IsLoading || !(gsm.CurrentState is PlayState)) return;
            if (PlayerController.Instance.inputController.player.GetButton(InputBinding.Confirm)) return;

            pendingPlacement = false;
            MultiplayerEventManager m = Main.eventManager;
            if (m.race == null || !m.isEventOwner || Main.cursor == null || Main.cursor.active) return;
            Utils.EnableCursor();
        }

        // An owner's S.K.A.T.E. event with no invite out (cancelled, declined, timed out) only
        // exists for the page to re-invite from. Once the page is closed, drop it so its HUD
        // doesn't linger and incoming invites aren't auto-declined as "busy".
        void DiscardAbandonedSetup()
        {
            MultiplayerEventManager m = Main.eventManager;
            if (m == null || m.SKATE == null || !m.isEventOwner || m.pendingInviteTo != "") return;
            if (m.multiplayerEvent.state == EventState.Running) return;
            if (eventsMenu != null && eventsMenu.activeInHierarchy) return;

            m.Disable(true);
            m.Reset();
        }

        // --- Shared page helpers -------------------------------------------------

        async void RebuildPage(ProceduralMenuPage page, PageState state, Func<string> signatureOf, Func<Task> build, bool visible)
        {
            state.rebuilding = true;
            string signature = null;
            try
            {
                string selectedId = SelectedItemId(page);
                do
                {
                    signature = signatureOf();
                    page.RemoveAll();
                    await build();
                    if (uninstalled || page == null) return;
                }
                while (signature != signatureOf());

                page.UpdatePage(); // order/refresh items; hand-added rows don't mark the page dirty
                if (visible && page.gameObject.activeInHierarchy)
                {
                    SelectItem(page, selectedId);
                    GuardClicks();
                }
            }
            catch (Exception e)
            {
                Utils.Log("Menu page build failed: " + e);
            }
            finally
            {
                state.builtSignature = signature; // on failure too, so we only retry on a state change
                state.rebuilding = false;
            }
        }

        static void GuardClicks()
        {
            clickGuardUntil = Time.unscaledTime + ClickGuardSeconds;
        }

        // Wraps a click so rapid/held presses and presses landing on a just-rebuilt page are ignored.
        static Action Guarded(Action action)
        {
            return () =>
            {
                if (Time.unscaledTime < clickGuardUntil) return;
                GuardClicks();
                action();
            };
        }

        // Clones the native menu button (without running its Awake) for use as a page row.
        static GameObject CloneTemplate(string name)
        {
            if (buttonTemplate == null) throw new Exception("No menu button template yet");

            GameObject holder = new GameObject("MultiplayerEvents clone holder");
            holder.SetActive(false); // strip localization before the clone's Awake runs
            GameObject go = Instantiate(buttonTemplate, holder.transform, false);
            go.name = name;
            StripLocalization(go);
            go.SetActive(true);
            go.transform.SetParent(null, false);
            Destroy(holder);
            return go;
        }

        static void RegisterItem(ProceduralMenuPage page, MenuPageItemBase item, GameObject go)
        {
            MenuPageItemBase last = page.items.LastOrDefault();
            item.sortingOrder = (last != null ? last.sortingOrder : -1) + 1;
            item.localize = false;
            go.transform.SetParent(page.itemParent, false);
            page.items.Add(item);
            item.UpdateItem();
        }

        // ProceduralMenuPage.AddButton can't be used: the game ships no "Prefabs/Menu/Button"
        // addressable (its own pages never add buttons), so the load returns null. Instead clone
        // the native multiplayer menu button and register it as a page item by hand.
        static ButtonItem AddButton(ProceduralMenuPage page, string id, string label, Action action)
        {
            GameObject go = CloneTemplate(label + " Item");

            MenuButton button = go.GetComponent<MenuButton>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onGreyedOutClick = new Button.ButtonClickedEvent();
            button.GreyedOut = false;
            button.interactable = true;

            ButtonItem item = go.AddComponent<ButtonItem>();
            item.id = id;
            item.label = label;
            item.selectable = button;
            item.onClick = Guarded(action);
            RegisterItem(page, item, go);
            return item;
        }

        // Destructive action: the first press turns the label into a confirmation, the second
        // (within a few seconds) runs it.
        static void AddConfirmButton(ProceduralMenuPage page, string id, string label, Action action)
        {
            float armedUntil = 0f;
            ButtonItem item = null;
            item = AddButton(page, id, label, () =>
            {
                if (Time.unscaledTime <= armedUntil)
                {
                    armedUntil = 0f;
                    action();
                    return;
                }
                armedUntil = Time.unscaledTime + ConfirmSeconds;
                item.selectable.SetText(label + " - press again to confirm", true);
            });
            ConfirmReset reset = item.gameObject.AddComponent<ConfirmReset>();
            reset.label = label;
            reset.expires = () => armedUntil > 0f && Time.unscaledTime > armedUntil;
            reset.clear = () => armedUntil = 0f;
        }

        static TMP_Text MakeTextRow(ProceduralMenuPage page, string id, string text, float height, bool header)
        {
            GameObject go = CloneTemplate(id + " Text");
            MenuButton button = go.GetComponent<MenuButton>();
            TMP_Text label = button.Label;
            Color textColor = button.colors.normalColor; // the menu's regular (dark) button text color

            // Keep only the label: drop the button behaviour and its selection visuals.
            DestroyImmediate(button);
            foreach (Transform child in go.transform.Cast<Transform>().ToList())
            {
                if (label == null || !label.transform.IsChildOf(child)) DestroyImmediate(child.gameObject);
            }

            RectTransform rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);

            // A disabled Selectable so the page's first-selection logic skips this row.
            Selectable blocker = go.AddComponent<Selectable>();
            blocker.transition = Selectable.Transition.None;
            blocker.navigation = new Navigation { mode = Navigation.Mode.None };
            blocker.interactable = false;

            TextItem item = go.AddComponent<TextItem>();
            item.id = id;
            item.label = text;
            item.text = label;
            if (label != null)
            {
                label.fontSize *= header ? 0.9f : 0.72f;
                // The panel is light: headers use the menu accent blue, info lines a softer grey.
                label.color = header ? Accent : Color.Lerp(textColor, Color.white, 0.35f);
                label.alignment = header ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.MidlineLeft;
                label.enableWordWrapping = false;
                FitOnOneLine(label);
                if (header) AddSeparator(go, label);
            }
            RegisterItem(page, item, go);
            return label;
        }

        // Keep a label on one line: shrink the font (down to 60%) when the text is too wide.
        static void FitOnOneLine(TMP_Text label)
        {
            if (label == null) return;
            label.enableWordWrapping = false;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = label.fontSize * 0.6f;
            label.enableAutoSizing = true;
        }

        // Thin accent line under a section header, aligned with the text left edge.
        static void AddSeparator(GameObject row, TMP_Text label)
        {
            RectTransform text = label.rectTransform;
            text.offsetMin = new Vector2(text.offsetMin.x, 14f); // lift the text above the line

            GameObject line = new GameObject("Separator", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)line.transform;
            rt.SetParent(row.transform, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(text.offsetMin.x, 6f);
            rt.offsetMax = new Vector2(-32f, 8f);

            Image image = line.GetComponent<Image>();
            image.color = new Color(Accent.r, Accent.g, Accent.b, 0.6f);
            image.raycastTarget = false;
        }

        static void AddHeader(ProceduralMenuPage page, string id, string text)
        {
            MakeTextRow(page, id, text, HeaderHeight, true);
        }

        static void AddInfo(ProceduralMenuPage page, string id, string text)
        {
            MakeTextRow(page, id, text, InfoHeight, false);
        }

        static void AddSpacer(ProceduralMenuPage page, string id)
        {
            MakeTextRow(page, id, "", SpacerHeight, false);
        }

        static string SelectedItemId(ProceduralMenuPage page)
        {
            GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (current == null) return null;
            MenuPageItemBase item = page.items.FirstOrDefault(i => i != null && i.gameObject == current);
            return item != null ? item.id : null;
        }

        // Rebuilding destroys the selected item; put the selection back (same item if it still
        // exists, else the first selectable one) so controller navigation keeps working.
        static void SelectItem(ProceduralMenuPage page, string id)
        {
            if (EventSystem.current == null) return;

            List<MenuPageItemBase> selectable = page.items
                .Where(i => i != null && i.gameObject.activeInHierarchy)
                .Where(i => { Selectable s = i.GetComponent<Selectable>(); return s != null && s.interactable; })
                .OrderBy(i => i.sortingOrder)
                .ToList();

            MenuPageItemBase target = selectable.FirstOrDefault(i => i.id == id) ?? selectable.FirstOrDefault();
            if (target != null) EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        // --- Settings category ---------------------------------------------------

        void TryInstallSettings()
        {
            SettingsMenuController smc = SettingsMenuController.Instance;
            if (smc == null) return;

            // Default pages are created asynchronously; wait for the multiplayer one so our
            // category lands next to it rather than first.
            bool anchorReady = smc.GetSettingsPage(SettingsAnchorCategory) != null;
            if (!anchorReady && Time.unscaledTime - initializedAt < SettingsWaitSeconds) return;

            InstallSettings(smc);
        }

        async void InstallSettings(SettingsMenuController smc)
        {
            installingSettings = true;
            try
            {
                int anchor = smc.SettingsCategories.FindIndex(c => c.Name == SettingsAnchorCategory);
                ProceduralMenuPage page = await smc.CreateSettingsPage(Title, anchor >= 0 ? anchor + 1 : -1);
                if (uninstalled)
                {
                    RemoveSettingsPage(smc, page);
                    return;
                }
                if (page.GetComponent<SaveSettingsOnDisable>() == null) page.gameObject.AddComponent<SaveSettingsOnDisable>();
                settingsState.builtSignature = null;
                settingsPage = page; // items are (re)built by UpdateSettingsPage
                Utils.Log("Native settings page installed");
            }
            catch (Exception e)
            {
                settingsFailed = true;
                Utils.Log("Native settings page failed: " + e);
            }
            finally
            {
                installingSettings = false;
            }
        }

        void UpdateSettingsPage()
        {
            if (settingsState.rebuilding || Time.unscaledTime < settingsState.nextCheck) return;
            settingsState.nextCheck = Time.unscaledTime + 0.25f;
            if (buttonTemplate == null) return; // rows are cloned from the multiplayer menu

            // Only the block list changes the page's items; values are read live by the items.
            if (SettingsSignature() != settingsState.builtSignature)
                RebuildPage(settingsPage, settingsState, SettingsSignature, BuildSettingsItems, true);
        }

        // The settings header is localized by term; ours has no translation, so when the menu opens
        // (e.g. via our Settings button) the header's localizer restores the previous category name
        // ("Main Settings") after the controller set ours. Re-apply it while our page is showing.
        void FixSettingsTitle()
        {
            if (settingsPage == null || !settingsPage.gameObject.activeInHierarchy) return;
            CategoryButton header = SettingsMenuController.Instance != null ? SettingsMenuController.Instance.SettingsCategoryButton : null;
            if (header != null && header.Label != null && header.Label.text != Title) header.SetText(Title);
        }

        static string SettingsSignature()
        {
            string players = Utils.isOnline() ? string.Join(",", Utils.getListOfPlayers(false)) : "offline";
            return string.Join("\n", Main.settings.blockedPlayers.ToArray()) + "|" + players;
        }

        async Task BuildSettingsItems()
        {
            ProceduralMenuPage page = settingsPage;

            AddHeader(page, "appearance", "Appearance");
            await page.AddStringEnumSetting("activeColor", "Active letters color",
                () => ColorIndex(Main.settings.fontColorAccent),
                i => Main.settings.fontColorAccent = Colors[Mathf.Clamp(i, 0, Colors.Length - 1)],
                ColorNames);

            await page.AddStringEnumSetting("inactiveColor", "Inactive letters color",
                () => ColorIndex(Main.settings.fontColor),
                i => Main.settings.fontColor = Colors[Mathf.Clamp(i, 0, Colors.Length - 1)],
                ColorNames);

            AddHeader(page, "gameplay", "Gameplay");
            await page.AddIntSetting("maxRetries", "Max retries per turn",
                () => Main.settings.maxRetries,
                v => Main.settings.maxRetries = Mathf.Clamp(v, 0, 5),
                0, 5);

            await page.AddIntSetting("smallManual", "Ignore manuals under",
                () => Mathf.RoundToInt(Main.settings.smallManualMaxSeconds / ManualStep),
                v => Main.settings.smallManualMaxSeconds = Mathf.Clamp(v, 0, ManualSteps) * ManualStep,
                0, ManualSteps,
                v => (v * ManualStep).ToString("0.00") + "s");

            InputFieldItem wordItem = null;
            wordItem = (InputFieldItem)await page.AddInputField("skateWord", "S.K.A.T.E. word",
                () => GameConfig.NormalizeSkateWord(Main.settings.skateWord),
                v =>
                {
                    // Show what will actually be used (letters/digits, clamped, filtered).
                    string word = GameConfig.NormalizeSkateWord(v);
                    Main.settings.skateWord = word;
                    if (wordItem != null) wordItem.selectable.SetTextWithoutNotify(word);
                });
            wordItem.selectable.maxTextLength = GameConfig.MaxSkateWordLength;
            FitOnOneLine(wordItem.selectable.label);
            wordItem.selectable.dialogTitle = "S.K.A.T.E. word (letters & numbers, up to " + GameConfig.MaxSkateWordLength + ")";

            AddHeader(page, "moderation", "Moderation");
            AddInfo(page, "moderationHelp", "Blocked players can't invite you");

            // Block someone in the room (everyone, not only modded players; already-blocked are hidden).
            string[] roomPlayers = Utils.isOnline() ? Utils.getListOfPlayers(false) : new string[0];
            if (roomPlayers.Length == 0)
            {
                AddInfo(page, "noRoomPlayers", Utils.isOnline() ? "No other players in the room" : "Join a room to block players from it");
            }
            else
            {
                if (Array.IndexOf(roomPlayers, selectedBlockTarget) < 0) selectedBlockTarget = roomPlayers[0];
                MenuPageItemBase picker = await page.AddStringEnumSetting("blockTarget", "Player",
                    () => Mathf.Max(0, Array.IndexOf(roomPlayers, selectedBlockTarget)),
                    i => selectedBlockTarget = roomPlayers[Mathf.Clamp(i, 0, roomPlayers.Length - 1)],
                    roomPlayers.Select(Utils.NickOf));
                picker.localize = false;
                AddConfirmButton(page, "blockPlayer", "Block player", () =>
                {
                    if (selectedBlockTarget == "") return;
                    Utils.BlockPlayer(Utils.NickOf(selectedBlockTarget), Utils.UserIdOf(selectedBlockTarget));
                    selectedBlockTarget = "";
                });
            }
            List<string> blocked = Main.settings.blockedPlayers;
            if (blocked.Count == 0)
            {
                AddInfo(page, "noBlocked", "No blocked players");
            }
            else
            {
                for (int i = 0; i < blocked.Count; i++)
                {
                    string entry = blocked[i];
                    string nick = Utils.NickOf(entry);
                    string name = nick != "" ? nick : Utils.UserIdOf(entry);
                    AddButton(page, "unblock" + i, "Unblock " + name, () => Utils.UnblockPlayer(entry));
                }
            }

            InputFieldItem blockItem = null;
            blockItem = (InputFieldItem)await page.AddInputField("blockByName", "Block by name",
                () => "",
                v =>
                {
                    if (string.IsNullOrEmpty(v)) return;
                    Utils.BlockPlayer(v, "");
                    if (blockItem != null) blockItem.selectable.SetTextWithoutNotify("");
                });
            blockItem.selectable.maxTextLength = 32;
            FitOnOneLine(blockItem.selectable.label);
            blockItem.selectable.useProfanityFilter = false; // a filter would mangle the name we match on
            blockItem.selectable.dialogTitle = "Block player by name";
        }

        static int ColorIndex(Color c)
        {
            for (int i = 0; i < Colors.Length; i++)
            {
                if (Colors[i] == c) return i;
            }
            return 0;
        }

        static void RemoveSettingsPage(SettingsMenuController smc, ProceduralMenuPage page)
        {
            if (page == null) return;
            if (smc != null)
            {
                int index = smc.SettingsCategories.FindIndex(c => c.Panel == page.gameObject);
                if (index >= 0) smc.SettingsCategories.RemoveAt(index);
            }
            page.RemoveAll();
            if (!Addressables.ReleaseInstance(page.gameObject)) Destroy(page.gameObject);
        }

        // --- Teardown -----------------------------------------------------------

        public void Uninstall()
        {
            uninstalled = true;
            try
            {
                if (eventsMenu != null && eventsMenu.activeSelf && menuController != null) BackToMainMenu();

                RemoveMainMenuButton();
                if (playerInviteButton != null) Destroy(playerInviteButton.gameObject);
                playerInviteButton = null;

                if (eventsPage != null)
                {
                    eventsPage.RemoveAll();
                    Addressables.ReleaseInstance(eventsPage.gameObject);
                }
                if (eventsMenu != null) Destroy(eventsMenu);

                if (settingsPage != null) RemoveSettingsPage(SettingsMenuController.Instance, settingsPage);
            }
            catch (Exception e)
            {
                Utils.Log("Native menu uninstall failed: " + e);
            }

            menuController = null;
            buttonTemplate = null;
            eventsMenu = null;
            eventsPage = null;
            settingsPage = null;
        }
    }

    /// <summary>A non-interactive text row (section header, info line or spacer) on a menu page.</summary>
    class TextItem : MenuPageItemBase
    {
        public TMP_Text text;

        public override void UpdateItem()
        {
            if (text != null) text.SetText(label);
        }
    }

    /// <summary>Puts a confirm button's label back once its confirmation window runs out.</summary>
    class ConfirmReset : MonoBehaviour
    {
        public string label;
        public Func<bool> expires;
        public Action clear;

        void Update()
        {
            if (expires == null || !expires()) return;
            clear();
            MenuButton button = GetComponent<MenuButton>();
            if (button != null) button.SetText(label, true);
        }
    }

    /// <summary>Persists settings when the settings page is hidden (category switch or menu closed).</summary>
    class SaveSettingsOnDisable : MonoBehaviour
    {
        void OnDisable()
        {
            if (Main.settings != null && Main.modEntry != null) Main.settings.Save(Main.modEntry);
        }
    }

    /// <summary>Hides the events page whenever the game opens one of its own multiplayer sub-menus.</summary>
    [HarmonyPatch]
    static class MultiplayerMenuControllerPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type t = typeof(MultiplayerMenuController);
            yield return AccessTools.Method(t, nameof(MultiplayerMenuController.ViewMainMpMenu));
            yield return AccessTools.Method(t, nameof(MultiplayerMenuController.ViewRoomList));
            yield return AccessTools.Method(t, nameof(MultiplayerMenuController.ViewPlayersMenu));
            yield return AccessTools.Method(t, nameof(MultiplayerMenuController.ViewPlayerInfoMenu));
        }

        static void Postfix()
        {
            if (Main.nativeMenu != null) Main.nativeMenu.HideEventsMenu();
        }
    }

    /// <summary>Shows/updates the S.K.A.T.E. invite button whenever the native player info page refreshes.</summary>
    [HarmonyPatch(typeof(MultiplayerPlayerInfoMenu), nameof(MultiplayerPlayerInfoMenu.UpdateInfo))]
    static class MultiplayerPlayerInfoMenuPatch
    {
        static void Postfix()
        {
            if (Main.nativeMenu != null) Main.nativeMenu.RefreshPlayerInviteButton();
        }
    }

    /// <summary>Lets the events page come back when the multiplayer menu is reopened (e.g. back from Settings).</summary>
    [HarmonyPatch(typeof(MultiplayerMenuController), nameof(MultiplayerMenuController.OnEnable))]
    static class MultiplayerMenuControllerEnablePatch
    {
        static void Postfix()
        {
            if (Main.nativeMenu != null) Main.nativeMenu.OnMultiplayerMenuEnabled();
        }
    }
}
