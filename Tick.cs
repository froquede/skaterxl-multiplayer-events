
using GameManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MultiplayerEvents
{
    class Tick : MonoBehaviour
    {
        float countdownDuration = -1f, countdown = -1f;
        public bool GOSUI = false;
        GUIStyle styleActive, styleDisabled, styleSmall, styleSmallAccent, styleCenterTrick, styleAllRight, styleRightNoFont;
        bool styleCreated = false;
        Color lastAccentColor, lastFontColor;
        float lastScreenHeight = -1f;
        public TrickCombo trickConfirmation;

        // The HUD was laid out on a 768px-tall screen; scale fonts and rects by the real
        // height so labels stay the same physical size at 1080p/4K instead of tiny.
        float UiScale => Screen.height / 768f;

        // Spectate + pass-turn are held on the camera-pan dpad axis (see InputBinding).
        bool spectating = false;
        float passHoldTime = 0f, spectateHoldTime = 0f;
        bool passFired = false, spectateFired = false;
        float spectateExitAt = -1f; // scheduled auto-exit time (buffer after the turn flips)

        // Turn-change + new-letter feedback.
        bool hasLastState = false;
        GameOfSkate.GOSState lastPlayerState;
        int lastMyLetterCount = 0, lastOppLetterCount = 0;
        int myPopLetter = -1, oppPopLetter = -1;   // index of the letter currently popping (-1 = none)
        float myPopStart = -10f, oppPopStart = -10f;
        const float LetterPopDuration = 0.32f;


        void Start()
        {
            TrickManager.Instance.onComboEnded += this.Instance_onComboEnded;
        }

        private void Instance_onComboEnded(TrickCombo obj)
        {
            if (Main.eventManager.SKATE != null)
            {
                Main.eventManager.SKATE.OnComboEnded(obj);
            }
        }

        void Update()
        {
            if (countdownDuration >= 0f && countdown <= countdownDuration)
            {
                Utils.ShowNotification((countdownDuration - countdown).ToString("N0"), 1f);
                countdown += Time.deltaTime;
            }

            // Advertise the mod so others can see who they can invite.
            Utils.PublishPresence();

            // Invitation timeouts + accept/decline input.
            MultiplayerEventManager em = Main.eventManager;
            if (em.pendingInviteTo != "" && Time.time > em.pendingInviteExpiry) em.CancelInvite(true);
            if (em.hasIncomingInvite)
            {
                if (Time.time > em.incomingInviteExpiry) em.DeclineIncomingInvite(true);
                else if (PlayerController.Instance != null)
                {
                    if (PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.Confirm)) em.AcceptIncomingInvite();
                    else if (PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.Cancel)) em.DeclineIncomingInvite();
                }
            }

            // Race lobby prompt (only one of the two prompts is ever up - busy check gates it).
            if (em.hasIncomingRaceInvite)
            {
                if (Time.time > em.incomingRaceExpiry) em.DeclineIncomingRace();
                else if (PlayerController.Instance != null)
                {
                    if (PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.Confirm)) em.JoinIncomingRace();
                    else if (PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.Cancel)) em.DeclineIncomingRace();
                }
            }

            if (trickConfirmation != null && PlayerController.Instance != null)
            {
                var cInput = PlayerController.Instance.inputController.player;
                bool canRedo = Main.eventManager.SKATE != null && Main.eventManager.SKATE.retries > 0;

                // Redo is only offered while retries remain; otherwise the choice is locked to Set
                // (the prompt still shows so nothing is silently auto-set).
                if (!canRedo) confirmTrick = true;
                else if (cInput.GetButtonUp(InputBinding.DpadLeftAction) || cInput.GetButtonUp(InputBinding.DpadRightAction)) confirmTrick = !confirmTrick;

                if (cInput.GetButtonUp(InputBinding.Confirm))
                {
                    Main.eventManager.SKATE.OnConfirmEvent(confirmTrick);
                    trickConfirmation = null;
                    confirmTrick = true;
                }
            }

            // Announce turn changes and flag freshly-activated letters for the pop animation.
            GameOfSkate skate = em.SKATE;
            if (skate != null && skate.running && skate.modeLetters != null)
            {
                if (!hasLastState || skate.playerState != lastPlayerState)
                {
                    // Skip the very first Waiting (game just created, nothing has happened yet).
                    if (hasLastState || skate.playerState != GameOfSkate.GOSState.Waiting)
                    {
                        switch (skate.playerState)
                        {
                            case GameOfSkate.GOSState.Setting:   Utils.ShowNotification("Set a trick", 2f); Utils.PlayTurnSound(true); break;
                            case GameOfSkate.GOSState.Defending: Utils.ShowNotification("Match the trick", 2f); Utils.PlayTurnSound(true); break;
                            case GameOfSkate.GOSState.Waiting:   Utils.ShowNotification("Waiting", 1.5f); Utils.PlayTurnSound(false); break;
                        }
                    }
                    lastPlayerState = skate.playerState;
                    hasLastState = true;
                }

                int mine = CountActive(skate.letters);
                int opp = CountActive(skate.opponentLetters);
                if (mine > lastMyLetterCount) { myPopLetter = mine - 1; myPopStart = Time.time; }
                if (opp > lastOppLetterCount) { oppPopLetter = opp - 1; oppPopStart = Time.time; }
                lastMyLetterCount = mine;
                lastOppLetterCount = opp;
            }
            else
            {
                // No active game - reset so the next game re-announces from scratch.
                hasLastState = false;
                lastMyLetterCount = lastOppLetterCount = 0;
                myPopLetter = oppPopLetter = -1;
            }

            // Held dpad actions: pass the turn (Left) / spectate the opponent (Right).
            if (spectating && !(GameStateMachine.Instance.CurrentState is SpectateState)) spectating = false;
            if (spectating && (skate == null || !skate.running)) StopSpectate();

            if (skate != null && skate.running && PlayerController.Instance != null)
            {
                var input = PlayerController.Instance.inputController.player;

                // Pass your setting turn without bailing. Available the whole time it's your turn
                // to set - idle, or after landing a trick you haven't confirmed yet (so you can
                // pass out of an unintended trick-out even though the game registered something).
                bool canPass = skate.playerState == GameOfSkate.GOSState.Setting;
                if (canPass && input.GetButton(InputBinding.PassTurn))
                {
                    passHoldTime += Time.deltaTime;
                    if (passHoldTime >= InputBinding.HoldSeconds && !passFired) { skate.TryPassTurn(); passFired = true; }
                }
                else { passHoldTime = 0f; passFired = false; }

                // Toggle spectating the opponent (enter only while it's their move).
                if (input.GetButton(InputBinding.Spectate))
                {
                    spectateHoldTime += Time.deltaTime;
                    if (spectateHoldTime >= InputBinding.HoldSeconds && !spectateFired)
                    {
                        spectateFired = true;
                        if (spectating) StopSpectate();
                        else if (skate.playerState == GameOfSkate.GOSState.Waiting) StartSpectate(skate);
                    }
                }
                else { spectateHoldTime = 0f; spectateFired = false; }

                // Auto-return to skating when it's our turn - but with a short buffer so the
                // opponent's trick replay (which lags the network event) finishes on screen first.
                if (spectating && skate.playerState != GameOfSkate.GOSState.Waiting)
                {
                    if (spectateExitAt < 0f) spectateExitAt = Time.time + GameConfig.SpectateExitBufferSeconds;
                    if (Time.time >= spectateExitAt) StopSpectate();
                }
                else spectateExitAt = -1f;
            }
        }

        // Enter Skater XL's built-in spectate mode targeting our opponent.
        void StartSpectate(GameOfSkate skate)
        {
            NetworkPlayerController opp = Utils.GetNetworkPlayer(skate.opponentUserID);
            if (opp == null) { Utils.ShowNotification("Can't spectate - opponent not found", 1.5f); return; }
            if (!opp.CanBeSpectated()) { Utils.ShowNotification("Can't spectate the opponent right now", 1.5f); return; }

            try
            {
                GameStateMachine.Instance.Spectate(opp);
                spectating = true;
            }
            catch (Exception e) { Utils.Log("Spectate failed: " + e); }
        }

        // Leave spectate and return to skating.
        void StopSpectate()
        {
            try
            {
                if (GameStateMachine.Instance.CurrentState is SpectateState) GameStateMachine.Instance.RequestPlayState();
            }
            catch (Exception e) { Utils.Log("Exit spectate failed: " + e); }
            spectating = false;
        }

        static int CountActive(bool[] arr)
        {
            if (arr == null) return 0;
            int n = 0;
            for (int i = 0; i < arr.Length; i++) if (arr[i]) n++;
            return n;
        }

        public void StartCountdown(float duration)
        {
            countdownDuration = duration;
            countdown = 0f;

            Utils.ShowNotification(countdownDuration, 1f);
        }

        List<UnityEngine.Object> toDestroy = new List<UnityEngine.Object>();
        public void DelayDestroy(UnityEngine.Object @object)
        {
            toDestroy.Add(@object);
        }

        void LateUpdate()
        {
            if(toDestroy.Count > 0)
            {
                for(int i = 0; i < toDestroy.Count; i++)
                {
                    UnityEngine.Object.Destroy(toDestroy[i]);
                }

                toDestroy = new List<UnityEngine.Object>();
            }
        }

        public bool confirmTrick = true;
        void OnGUI()
        {
            // Rebuild styles on first draw and whenever the configured colors change,
            // so color settings apply live instead of needing a mod reload.
            if (!styleCreated || lastAccentColor != Main.settings.fontColorAccent || lastFontColor != Main.settings.fontColor
                || lastScreenHeight != Screen.height)
            {
                lastAccentColor = Main.settings.fontColorAccent;
                lastFontColor = Main.settings.fontColor;
                lastScreenHeight = Screen.height;
                float s = UiScale; // scale fonts up from the 768px the HUD was tuned on

                styleActive = new GUIStyle(GUI.skin.label);
                styleActive.alignment = TextAnchor.MiddleCenter;
                styleActive.fontSize = Mathf.RoundToInt(48 * s);
                styleActive.fontStyle = FontStyle.Bold;
                styleActive.font = Font.CreateDynamicFontFromOSFont("Tahoma Bold", 24);
                styleActive.normal.textColor = Main.settings.fontColorAccent;

                styleDisabled = new GUIStyle(GUI.skin.label);
                styleDisabled.alignment = TextAnchor.MiddleCenter;
                styleDisabled.fontSize = Mathf.RoundToInt(48 * s);
                styleDisabled.fontStyle = FontStyle.Bold;
                styleDisabled.font = Font.CreateDynamicFontFromOSFont("Tahoma Bold", 24);
                styleDisabled.normal.textColor = Main.settings.fontColor;

                styleSmall = new GUIStyle(GUI.skin.label);
                styleSmall.alignment = TextAnchor.MiddleRight;
                styleSmall.fontSize = Mathf.RoundToInt(20 * s);
                styleSmall.fontStyle = FontStyle.Bold;
                styleSmall.font = Font.CreateDynamicFontFromOSFont("Corbel Bold", 24);
                styleSmall.normal.textColor = Color.white;

                // Same as styleSmall but accent-colored, for whoever's turn it is.
                styleSmallAccent = new GUIStyle(styleSmall);
                styleSmallAccent.normal.textColor = Main.settings.fontColorAccent;

                styleCenterTrick = new GUIStyle(GUI.skin.label);
                styleCenterTrick.alignment = TextAnchor.MiddleCenter;
                styleCenterTrick.fontSize = Mathf.RoundToInt(20 * s);
                styleCenterTrick.fontStyle = FontStyle.Bold;
                styleCenterTrick.font = Font.CreateDynamicFontFromOSFont("Corbel Bold", 24);
                styleCenterTrick.normal.textColor = Color.white;

                styleAllRight = new GUIStyle(GUI.skin.label);
                styleAllRight.alignment = TextAnchor.MiddleRight;
                styleAllRight.fontSize = Mathf.RoundToInt(30 * s);
                styleAllRight.fontStyle = FontStyle.Bold;
                styleAllRight.font = Font.CreateDynamicFontFromOSFont("Corbel Bold", 24);
                styleAllRight.normal.textColor = Color.white;

                styleRightNoFont = new GUIStyle(GUI.skin.label);
                styleRightNoFont.alignment = TextAnchor.MiddleRight;
                styleRightNoFont.fontSize = Mathf.RoundToInt(12 * s);

                styleCreated = true;
            }

            DrawInvitePrompt(); // shown even while paused so invites aren't missed

            if (GameStateMachine.Instance.CurrentState.GetType() == typeof(PauseState) || GameStateMachine.Instance.CurrentState.GetType() == typeof(ReplayState)) return;

            if (GOSUI)
            {
                float s = UiScale;
                GameOfSkate skate = Main.eventManager.SKATE;
                GameOfSkate.GOSState st = skate.playerState;
                bool myTurn = st != GameOfSkate.GOSState.Waiting;
                string turnText = st == GameOfSkate.GOSState.Setting ? "SET A TRICK"
                                : st == GameOfSkate.GOSState.Defending ? "MATCH"
                                : "WAITING";

                Rect rectTopRight = new Rect(Screen.width - 380 * s, 90 * s, 340 * s, 120 * s);
                GUILayout.BeginArea(rectTopRight);
                GUILayout.BeginVertical();
                {
                    GUILayout.Label(turnText, myTurn ? styleSmallAccent : styleSmall);
                    // Show the defender what the game registered for their attempt (issue #6),
                    // persisting briefly past the flip to Waiting on a missed/bailed trick. Never
                    // while setting - the trick is already shown top-center there.
                    if (st != GameOfSkate.GOSState.Setting && skate.lastRegisteredTrick != ""
                        && Time.time - skate.lastRegisteredTime < GameConfig.RegisteredTrickSeconds)
                        GUILayout.Label("You: " + skate.lastRegisteredTrick, styleSmall);
                }
                GUILayout.EndVertical();
                GUILayout.EndArea();

                Rect rect = new Rect(Screen.width - 300 * s, (Screen.height / 2f) - 130 * s, 260 * s, 260 * s);

                GUILayout.BeginArea(rect);
                GUILayout.BeginVertical();
                {
                    GUILayout.Label((myTurn ? "• " : "") + MultiplayerManager.Instance.localPlayer.NickName, myTurn ? styleSmallAccent : styleSmall);
                    GUILayout.BeginHorizontal();
                    {
                        DrawGOSLetters();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(20 * s);

                    GUILayout.Label((!myTurn ? "• " : "") + Main.eventManager.SKATE.opponentNickname, !myTurn ? styleSmallAccent : styleSmall);
                    GUILayout.BeginHorizontal();
                    {
                        DrawGOSLetters(true);
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
                GUILayout.EndArea();

                // Only show the trick name while a trick is actually in play (setting your own
                // or defending the opponent's). Once you're back to Waiting it should clear,
                // rather than leave the last combo lingering on screen.
                // Show the trick top-center while it's in play: while setting/defending, and also
                // while you wait for the opponent to defend the trick you just set ("You set: ...").
                if (st != GameOfSkate.GOSState.Waiting || (skate.myTurn && skate.actualTrickCombo != null)) TrickName();
            }

            if (Main.eventManager.race != null && Main.eventManager.race.running) DrawRaceHUD();

            if (trickConfirmation != null)
            {
                float s = UiScale;
                bool canRedo = Main.eventManager.SKATE != null && Main.eventManager.SKATE.retries > 0;
                Rect confirmationContainer = new Rect(Screen.width - 340 * s, Screen.height - 120 * s, 300 * s, 120 * s);
                GUILayout.BeginArea(confirmationContainer);
                GUILayout.BeginVertical(GUILayout.Width(300 * s));
                GUILayout.Label("Confirm trick? " + Utils.ComboName(), styleSmall);
                GUILayout.BeginHorizontal(GUILayout.Width(300 * s));
                GUILayout.Label((confirmTrick ? "• " : "") + "Set trick", styleSmall);
                if (canRedo)
                {
                    GUILayout.Label((confirmTrick ? "" : "• ") + "Redo (" + Main.eventManager.SKATE.retries + ")", styleSmall);
                }
                else
                {
                    // Out of retries: show Redo dimmed and unselectable so it's clear why.
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, 0.35f);
                    GUILayout.Label("Redo (0)", styleSmall);
                    GUI.color = prev;
                }
                GUILayout.EndHorizontal();

                GUILayout.Label(canRedo ? "Dpad left - right toggle, A / X confirm" : "A / X confirm", styleRightNoFont);
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }

        void DrawGOSLetters(bool opponent = false)
        {
            GameOfSkate skate = Main.eventManager.SKATE;
            if (skate == null || skate.modeLetters == null) return;

            bool[] letters = opponent ? skate.opponentLetters : skate.letters;
            int popIndex = opponent ? oppPopLetter : myPopLetter;
            float popT = (Time.time - (opponent ? oppPopStart : myPopStart)) / LetterPopDuration; // 0..1 across the pop

            for (int i = 0; i < skate.modeLetters.Length && i < letters.Length; i++)
            {
                GUIStyle style = letters[i] ? styleActive : styleDisabled;
                GUIContent content = new GUIContent(char.ToUpper(skate.modeLetters[i]) + ".");
                Rect r = GUILayoutUtility.GetRect(content, style); // reserve normal-size slot (no layout shift)

                if (i == popIndex && popT >= 0f && popT < 1f)
                {
                    // Newly-lit letter: a small, quick scale-pop (~1.3x -> 1x) with a light
                    // brighten that settles to the accent color. ScaleAroundPivot keeps neighbors put.
                    float ease = (1f - popT) * (1f - popT);
                    float scale = 1f + 0.3f * ease;
                    Color prevColor = style.normal.textColor;
                    Matrix4x4 prevMatrix = GUI.matrix;

                    Color flashStart = Color.Lerp(prevColor, Color.white, 0.5f); // brightened, not pure white
                    style.normal.textColor = Color.Lerp(flashStart, prevColor, popT);
                    GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), r.center);
                    GUI.Label(r, content, style);

                    GUI.matrix = prevMatrix;
                    style.normal.textColor = prevColor;
                }
                else
                {
                    GUI.Label(r, content, style);
                }
            }
        }

        void DrawInvitePrompt()
        {
            MultiplayerEventManager em = Main.eventManager;
            if (em.hasIncomingRaceInvite) { DrawRacePrompt(em); return; }
            if (!em.hasIncomingInvite) return;

            int secs = Mathf.Max(0, Mathf.CeilToInt(em.incomingInviteExpiry - Time.time));
            string nick = Utils.NickOf(em.incomingInviteFrom);
            string what = em.incomingInviteType == EventType.SKATE
                ? DottedWord(GameConfig.NormalizeSkateWord(em.incomingInviteWord))
                : em.incomingInviteType.ToString();

            // Bottom-right, matching the trick-confirm HUD (invites are auto-declined
            // while in a game, so this never overlaps the confirm box).
            float s = UiScale;
            Rect rect = new Rect(Screen.width - 340 * s, Screen.height - 120 * s, 300 * s, 120 * s);
            GUILayout.BeginArea(rect);
            GUILayout.BeginVertical(GUILayout.Width(300 * s));
            GUILayout.Label(nick + " invited you", styleSmall);
            GUILayout.Label(what + "  (" + secs + "s)", styleSmall);
            GUILayout.Label("A / X accept, B / O decline", styleRightNoFont);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void DrawRacePrompt(MultiplayerEventManager em)
        {
            float s = UiScale;
            int secs = Mathf.Max(0, Mathf.CeilToInt(em.incomingRaceExpiry - Time.time));
            string nick = Utils.NickOf(em.incomingRaceFrom);

            Rect rect = new Rect(Screen.width - 340 * s, Screen.height - 120 * s, 300 * s, 120 * s);
            GUILayout.BeginArea(rect);
            GUILayout.BeginVertical(GUILayout.Width(300 * s));
            GUILayout.Label(nick + " opened a Race", styleSmall);
            GUILayout.Label(em.incomingRaceLaps + (em.incomingRaceLaps == 1 ? " lap" : " laps") + "  (" + secs + "s)", styleSmall);
            GUILayout.Label("A / X join, B / O decline", styleRightNoFont);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // Live race ranking: finishers (by time) above racers still going (by furthest progress).
        void DrawRaceHUD()
        {
            float s = UiScale;
            Race race = Main.eventManager.race;
            List<RaceProgress> ranking = race.Ranking();
            string me = MultiplayerManager.Instance.localPlayer.UserId;

            Rect panel = new Rect(Screen.width - 320 * s, 80 * s, 300 * s, (70 + 26 * ranking.Count) * s);
            GUILayout.BeginArea(panel);
            GUILayout.BeginVertical();
            GUILayout.Label("Race - " + race.laps + (race.laps == 1 ? " lap" : " laps"), styleSmall);
            for (int i = 0; i < ranking.Count; i++)
            {
                RaceProgress p = ranking[i];
                string status = p.finished
                    ? Race.FormatTime(p.totalMs)
                    : "L" + (p.lapsDone + 1) + "  " + p.nextCp + "/" + race.checkpoints.Count;
                GUILayout.Label((i + 1) + ". " + race.NickFor(p.userId) + "   " + status, p.userId == me ? styleSmallAccent : styleSmall);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        string DottedWord(string word)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in word) sb.Append(char.ToUpper(c)).Append('.');
            return sb.ToString();
        }

        void TrickName()
        {
            float s = UiScale;
            string name = Utils.ComboName();
            if (name == "") return;

            // Label the trick by role: the target to match while defending, and the trick you
            // just locked in while you wait for the opponent to defend it.
            GameOfSkate skate = Main.eventManager.SKATE;
            string label;
            if (skate == null) label = name;
            else if (skate.playerState == GameOfSkate.GOSState.Defending) label = "Match:  " + name;
            else if (skate.playerState == GameOfSkate.GOSState.Waiting && skate.myTurn) label = "You set:  " + name;
            else label = name;

            Rect trickContainer = new Rect(40 * s, 40 * s, Screen.width - 80 * s, 60 * s);
            GUILayout.BeginArea(trickContainer);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, styleCenterTrick);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        public void OnDestroy()
        {
            TrickManager.Instance.onComboEnded -= this.Instance_onComboEnded;
        }
    }
}
