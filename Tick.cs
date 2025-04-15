
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
        GUIStyle styleActive, styleDisabled, styleSmall, styleCenterTrick, styleAllRight, styleRightNoFont;
        bool styleCreated = false;
        public TrickCombo trickConfirmation;


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

            if (trickConfirmation != null)
            {
                if (PlayerController.Instance.inputController.player.GetButtonUp(69) || PlayerController.Instance.inputController.player.GetButtonUp(70)) confirmTrick = !confirmTrick;
                if (PlayerController.Instance.inputController.player.GetButtonUp("A"))
                {
                    Main.eventManager.SKATE.OnConfirmEvent(confirmTrick);
                    trickConfirmation = null;
                    confirmTrick = true;
                }
            }
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
            if (!styleCreated)
            {
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

            if (GameStateMachine.Instance.CurrentState.GetType() == typeof(PauseState) || GameStateMachine.Instance.CurrentState.GetType() == typeof(ReplayState)) return;

            if (GOSUI)
            {
                Rect rectTopRight = new Rect(Screen.width - 300, 90, 260, 60);
                GUILayout.BeginArea(rectTopRight);
                GUILayout.Label(Main.eventManager.SKATE.playerState.ToString(), styleSmall);
                GUILayout.EndArea();

                Rect rect = new Rect(Screen.width - 300, (Screen.height / 2f) - 130, 260, 260);

                GUILayout.BeginArea(rect);
                GUILayout.BeginVertical();
                {
                    GUILayout.Label((Main.eventManager.SKATE.playerState != GameOfSkate.GOSState.Waiting ? "• " : "") + MultiplayerManager.Instance.localPlayer.NickName, styleSmall);
                    GUILayout.BeginHorizontal();
                    {
                        DrawGOSLetters();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(20);

                    GUILayout.Label((Main.eventManager.SKATE.playerState == GameOfSkate.GOSState.Waiting ? "• " : "") + Main.eventManager.SKATE.opponentNickname, styleSmall);
                    GUILayout.BeginHorizontal();
                    {
                        DrawGOSLetters(true);
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
                GUILayout.EndArea();

                TrickName();
            }

            if (trickConfirmation != null)
            {
                Rect confirmationContainer = new Rect(Screen.width - 340, Screen.height - 120, 300, 120);
                GUILayout.BeginArea(confirmationContainer);
                GUILayout.BeginVertical(GUILayout.Width(300));
                GUILayout.Label("Confirm trick? " + Utils.ComboName(), styleSmall);
                GUILayout.BeginHorizontal(GUILayout.Width(300));
                GUILayout.Label((confirmTrick ? "• " : "") + "Set trick", styleSmall);
                GUILayout.Label((confirmTrick ? "" : "• ") + "Redo (1)", styleSmall);
                GUILayout.EndHorizontal();

                GUILayout.Label("Dpad left - right toggle, A / X confirm", styleRightNoFont);
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }

        void DrawGOSLetters(bool opponent = false)
        {
            bool[] letters = opponent ? Main.eventManager.SKATE.opponentLetters : Main.eventManager.SKATE.letters;
            GUILayout.Label("S.", letters[0] ? styleActive : styleDisabled);
            GUILayout.Label("K.", letters[1] ? styleActive : styleDisabled);
            GUILayout.Label("A.", letters[2] ? styleActive : styleDisabled);
            GUILayout.Label("T.", letters[3] ? styleActive : styleDisabled);
            GUILayout.Label("E.", letters[4] ? styleActive : styleDisabled);
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
