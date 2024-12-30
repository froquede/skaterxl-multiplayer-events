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
}
