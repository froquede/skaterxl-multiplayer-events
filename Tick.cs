
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
        public TrickCombo trickConfirmation;

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

            if (trickConfirmation != null && PlayerController.Instance != null)
            {
                if (PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.DpadLeftAction) || PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.DpadRightAction)) confirmTrick = !confirmTrick;
                if (PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.Confirm))
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
                            case GameOfSkate.GOSState.Setting:   Utils.ShowNotification("Set a trick", 2f); break;
                            case GameOfSkate.GOSState.Defending: Utils.ShowNotification("Match the trick", 2f); break;
                            case GameOfSkate.GOSState.Waiting:   Utils.ShowNotification("Waiting", 1.5f); break;
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
            if (!styleCreated || lastAccentColor != Main.settings.fontColorAccent || lastFontColor != Main.settings.fontColor)
            {
                lastAccentColor = Main.settings.fontColorAccent;
                lastFontColor = Main.settings.fontColor;

                styleActive = new GUIStyle(GUI.skin.label);
                styleActive.alignment = TextAnchor.MiddleCenter;
                styleActive.fontSize = 48;
                styleActive.fontStyle = FontStyle.Bold;
                styleActive.font = Font.CreateDynamicFontFromOSFont("Tahoma Bold", 24);
                styleActive.normal.textColor = Main.settings.fontColorAccent;

                styleDisabled = new GUIStyle(GUI.skin.label);
                styleDisabled.alignment = TextAnchor.MiddleCenter;
                styleDisabled.fontSize = 48;
                styleDisabled.fontStyle = FontStyle.Bold;
                styleDisabled.font = Font.CreateDynamicFontFromOSFont("Tahoma Bold", 24);
                styleDisabled.normal.textColor = Main.settings.fontColor;

                styleSmall = new GUIStyle(GUI.skin.label);
                styleSmall.alignment = TextAnchor.MiddleRight;
                styleSmall.fontSize = 20;
                styleSmall.fontStyle = FontStyle.Bold;
                styleSmall.font = Font.CreateDynamicFontFromOSFont("Corbel Bold", 24);
                styleSmall.normal.textColor = Color.white;

                // Same as styleSmall but accent-colored, for whoever's turn it is.
                styleSmallAccent = new GUIStyle(styleSmall);
                styleSmallAccent.normal.textColor = Main.settings.fontColorAccent;

                styleCenterTrick = new GUIStyle(GUI.skin.label);
                styleCenterTrick.alignment = TextAnchor.MiddleCenter;
                styleCenterTrick.fontSize = 20;
                styleCenterTrick.fontStyle = FontStyle.Bold;
                styleCenterTrick.font = Font.CreateDynamicFontFromOSFont("Corbel Bold", 24);
                styleCenterTrick.normal.textColor = Color.white;

                styleAllRight = new GUIStyle(GUI.skin.label);
                styleAllRight.alignment = TextAnchor.MiddleRight;
                styleAllRight.fontSize = 30;
                styleAllRight.fontStyle = FontStyle.Bold;
                styleAllRight.font = Font.CreateDynamicFontFromOSFont("Corbel Bold", 24);
                styleAllRight.normal.textColor = Color.white;

                styleRightNoFont = new GUIStyle(GUI.skin.label);
                styleRightNoFont.alignment = TextAnchor.MiddleRight;

                styleCreated = true;
            }

            DrawInvitePrompt(); // shown even while paused so invites aren't missed

            if (GameStateMachine.Instance.CurrentState.GetType() == typeof(PauseState) || GameStateMachine.Instance.CurrentState.GetType() == typeof(ReplayState)) return;

            if (GOSUI)
            {
                GameOfSkate.GOSState st = Main.eventManager.SKATE.playerState;
                bool myTurn = st != GameOfSkate.GOSState.Waiting;
                string turnText = st == GameOfSkate.GOSState.Setting ? "SET A TRICK"
                                : st == GameOfSkate.GOSState.Defending ? "MATCH"
                                : "WAITING";

                Rect rectTopRight = new Rect(Screen.width - 300, 90, 260, 60);
                GUILayout.BeginArea(rectTopRight);
                GUILayout.Label(turnText, myTurn ? styleSmallAccent : styleSmall);
                GUILayout.EndArea();

                Rect rect = new Rect(Screen.width - 300, (Screen.height / 2f) - 130, 260, 260);

                GUILayout.BeginArea(rect);
                GUILayout.BeginVertical();
                {
                    GUILayout.Label((myTurn ? "• " : "") + MultiplayerManager.Instance.localPlayer.NickName, myTurn ? styleSmallAccent : styleSmall);
                    GUILayout.BeginHorizontal();
                    {
                        DrawGOSLetters();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(20);

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
                if (Main.eventManager.SKATE.playerState != GameOfSkate.GOSState.Waiting) TrickName();
            }

            if (trickConfirmation != null)
            {
                Rect confirmationContainer = new Rect(Screen.width - 340, Screen.height - 120, 300, 120);
                GUILayout.BeginArea(confirmationContainer);
                GUILayout.BeginVertical(GUILayout.Width(300));
                GUILayout.Label("Confirm trick? " + Utils.ComboName(), styleSmall);
                GUILayout.BeginHorizontal(GUILayout.Width(300));
                GUILayout.Label((confirmTrick ? "• " : "") + "Set trick", styleSmall);
                GUILayout.Label((confirmTrick ? "" : "• ") + "Redo (" + Main.eventManager.SKATE.retries + ")", styleSmall);
                GUILayout.EndHorizontal();

                GUILayout.Label("Dpad left - right toggle, A / X confirm", styleRightNoFont);
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
            if (!em.hasIncomingInvite) return;

            int secs = Mathf.Max(0, Mathf.CeilToInt(em.incomingInviteExpiry - Time.time));
            string nick = Utils.NickOf(em.incomingInviteFrom);
            string what = em.incomingInviteType == EventType.SKATE
                ? DottedWord(GameConfig.NormalizeSkateWord(em.incomingInviteWord))
                : em.incomingInviteType.ToString();

            // Bottom-right, matching the trick-confirm HUD (invites are auto-declined
            // while in a game, so this never overlaps the confirm box).
            Rect rect = new Rect(Screen.width - 340, Screen.height - 120, 300, 120);
            GUILayout.BeginArea(rect);
            GUILayout.BeginVertical(GUILayout.Width(300));
            GUILayout.Label(nick + " invited you", styleSmall);
            GUILayout.Label(what + "  (" + secs + "s)", styleSmall);
            GUILayout.Label("A / X accept, B / O decline", styleRightNoFont);
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
            Rect trickContainer = new Rect(40, 40, Screen.width - 80, 60);
            GUILayout.BeginArea(trickContainer);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Utils.ComboName(), styleCenterTrick);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        public void OnDestroy()
        {
            TrickManager.Instance.onComboEnded -= this.Instance_onComboEnded;
        }
    }
}
