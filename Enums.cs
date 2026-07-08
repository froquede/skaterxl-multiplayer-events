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
        public const byte SkateGame = 70;              // in-match S.K.A.T.E. messages
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
        public const int SkateLetterCount = 5;         // S.K.A.T.E.
        public const int DefaultRetries = 1;
    }
}
