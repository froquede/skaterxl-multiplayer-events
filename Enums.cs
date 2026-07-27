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
        public const byte RaceSession = 71;            // race lobby + in-race telemetry (self-scoped by raceId)
    }

    /// <summary>
    /// Keys for the payload sent over <see cref="NetCode.RaceSession"/>. Every message
    /// carries a raceId at index 1; recipients that aren't part of that race ignore it,
    /// so a race never touches non-participants (no overwrite, no stray objects).
    /// </summary>
    static class RaceMessage
    {
        public const string Open = "raceOpen";      // host -> room: lobby open   [key, raceId, hostPlayerId, laps]
        public const string Join = "raceJoin";      // player -> host: I'm in      [key, raceId, joinerPlayerId]
        public const string Cancel = "raceCancel";  // host -> room: lobby closed  [key, raceId]
        public const string Start = "raceStart";    // host -> room: go            [key, raceId, laps, startServerTime, participantsCsv, cpCount, A0,B0,A1,B1...]
        public const string Progress = "raceProg";  // racer -> race: checkpoint   [key, raceId, userId, lapsDone, nextCp, serverTime]
        public const string Finish = "raceFin";     // racer -> race: finished     [key, raceId, userId, totalMs]
        public const string Leave = "raceLeave";    // racer -> race: I quit        [key, raceId, userId]
        public const string Stop = "raceStop";      // host -> race: teardown      [key, raceId]
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

    /// <summary>
    /// Rewired action ids/names read from the player input. Per RewiredConsts.Action:
    /// Up=67, Down=68, Right=69, Left=70. (The two confirm-toggle ids below are named
    /// left/right but are really right/left; it only toggles two options, so harmless.)
    /// </summary>
    static class InputBinding
    {
        public const int DpadLeftAction = 69;   // toggle confirm option
        public const int DpadRightAction = 70;  // toggle confirm option

        // Pass-turn / spectate are HELD on Dpad Left/Right. Dpad Up/Down are the game's
        // respawn / set-respawn (Up teleports) and fire on the press edge, so they're
        // off-limits; Left/Right only pan the camera. Held (not tapped) so a stray pan
        // can't trigger them.
        public const int PassTurn = 70;          // hold DpadLeft  - pass your setting turn
        public const int Spectate = 69;          // hold DpadRight - spectate the opponent
        public const float HoldSeconds = 0.6f;   // how long to hold before it fires

        public const int LT = 8;                 // left trigger axis  (checkpoint cursor: zoom out)
        public const int RT = 9;                 // right trigger axis (checkpoint cursor: zoom in)

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

        // --- Race (preview) ---
        public const int DefaultRaceLaps = 1;
        public const int MaxRaceLaps = 10;
        public const float RaceStartCountdownSeconds = 3f;  // between "Start" and the race clock starting (3-2-1-GO)
        public const float RaceLobbyTimeoutSeconds = 45f;   // how long a join prompt stays up
        public const string RaceParticipantSeparator = ","; // UserIds are GUID-ish, comma-safe

        // A manual shorter than this (seconds) is treated as incidental and stripped from
        // the trick used for setting/matching, so a clean trick popped right after a tiny
        // manual still registers as that clean trick. Tunable in settings.
        public const float SmallManualMaxSeconds = 0.3f;

        // Attempts a defender gets at the set trick when one letter from losing (match
        // point): the final, game-losing letter only counts after this many failed defenses.
        public const int LastLetterTries = 2;

        // Stay in spectate this long after the turn flips, so the opponent's trick replay
        // (which lags the network event) finishes before we're pulled back to skate.
        public const float SpectateExitBufferSeconds = 1.5f;

        // How long the defender's "You: <trick>" attempt feedback stays on the HUD after a
        // defense, so it survives the immediate flip to Waiting on a missed/bailed trick.
        public const float RegisteredTrickSeconds = 3f;

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
