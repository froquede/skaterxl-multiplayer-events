using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerEvents
{
    public enum EventState
    {
        Stopped,
        Running,
        Paused,
        End
    }

    public enum MessageType
    {
        EventState
    }

    public enum ParticipantState
    {
        Active,
        Idle
    }

    public enum EventType
    {
        Null,
        Race,
        SKATE
    }

    /// <summary>
    /// Photon RaiseEvent codes used by this mod. These were picked because the
    /// base game did not use them at the time; keep them here so any future
    /// collision is easy to spot and change in one place.
    /// </summary>
    static class NetCode
    {
        public const byte EventLifecycle = 65;         // create/start/stop/end broadcasts
        public const byte RaceParticipantPosition = 66;
        public const byte RaceCheckpointSync = 67;
        public const byte Invitation = 68;             // invite/accept/decline/cancel handshake
        public const byte SkateGame = 70;              // in-match S.K.A.T.E. messages
    }

    /// <summary>
    /// Keys for the payload sent over <see cref="NetCode.Invitation"/>.
    /// Payload: [ key, (int)EventType, targetUserId, senderPlayerId, word ].
    /// </summary>
    static class InviteMessage
    {
        public const string Invite = "invite";   // owner -> invitee
        public const string Accept = "accept";   // invitee -> owner
        public const string Decline = "decline"; // invitee -> owner
        public const string Cancel = "cancel";   // owner -> invitee (withdrawn / timed out)
    }

    /// <summary>Keys for the payload sent over <see cref="NetCode.SkateGame"/>.</summary>
    static class SkateMessage
    {
        public const string Turn = "turn";
        public const string TrickSet = "trickSet";
        public const string LetterSet = "letterSet";
        public const string DefenseSuccess = "defenseSuccess";
        public const string EventEnd = "eventEnd";
    }

    /// <summary>Rewired action ids/names read from the player input.</summary>
    static class InputBinding
    {
        public const int DpadLeftAction = 69;   // toggle confirm option
        public const int DpadRightAction = 70;  // toggle confirm option
        public const string Confirm = "A";
        public const string Cancel = "B";
    }

    static class GameConfig
    {
        public const string PlayerIdSeparator = " | "; // "NickName | UserId"
        public const float RaceCountdownSeconds = 10f;
        public const int DefaultRetries = 1;

        public const float InviteTimeoutSeconds = 20f;
        // Invite spam guard: a sender may trigger up to InviteMaxPerWindow invites per rolling
        // InviteWindowSeconds; extra invites are silently dropped. Lenient enough for fat-fingers
        // / legit re-invites, but stops a player spamming popups at a streamer.
        public const float InviteWindowSeconds = 30f;
        public const int InviteMaxPerWindow = 3;
        public const string PresencePropertyKey = "ME_ver"; // Photon custom prop advertising the mod
        public const string DefaultSkateWord = "SKATE";
        public const int MaxSkateWordLength = 8;            // keeps the HUD readable

        // Basic, non-exhaustive block list. The word is drawn on other players' screens,
        // so this just stops the obvious trolling; it is not meant to be comprehensive.
        static readonly string[] BlockedWordFragments =
        {
            "FUCK", "SHIT", "CUNT", "DICK", "COCK", "PUSS", "BITCH", "WHORE", "SLUT",
            "TWAT", "WANK", "PENIS", "VAGINA", "DILDO", "RAPE", "NAZI", "FAG", "NIGG",
            "KKK", "COON", "SPIC", "KIKE", "CHINK", "TRANNY", "RETARD",
        };

        public static bool IsWordAllowed(string upperWord)
        {
            if (string.IsNullOrEmpty(upperWord)) return true;

            // Check the raw word AND a de-leeted copy so digit substitutions (N1GG, F4G, 5PIC,
            // CH1NK, R3TARD, 8ITCH, ...) can't sneak a slur past the list. Digits are still shown
            // as typed in game; this mapping only feeds the profanity check.
            string deLeet = DeLeet(upperWord);
            foreach (string bad in BlockedWordFragments)
            {
                if (upperWord.Contains(bad) || deLeet.Contains(bad)) return false;
            }
            return true;
        }

        static string DeLeet(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '0': sb.Append('O'); break;
                    case '1': sb.Append('I'); break;
                    case '3': sb.Append('E'); break;
                    case '4': sb.Append('A'); break;
                    case '5': sb.Append('S'); break;
                    case '6': sb.Append('G'); break;
                    case '7': sb.Append('T'); break;
                    case '8': sb.Append('B'); break;
                    case '9': sb.Append('G'); break;
                    default:  sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Uppercase, alphanumeric-only, clamped, profanity-checked S.K.A.T.E. word.</summary>
        public static string NormalizeSkateWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return DefaultSkateWord;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in word.Trim().ToUpper())
            {
                // Allow digits too so words like "SK8" work; only spaces/punctuation are dropped.
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }

            string result = sb.ToString();
            if (result.Length == 0) return DefaultSkateWord;
            if (result.Length > MaxSkateWordLength) result = result.Substring(0, MaxSkateWordLength);
            if (!IsWordAllowed(result)) return DefaultSkateWord; // trolls fall back to SKATE
            return result;
        }
    }
}
