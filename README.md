# Multiplayer Events (Skater XL)

A mod for playing **events with other skaters online** in Skater XL. It's built as a
small framework — the "events" in the name — so new game modes can be added on top of
a shared multiplayer/lifecycle layer. So far it ships a classic game of **S.K.A.T.E.**
and a checkpoint **Race** (preview).

> **Proof of concept.** Everything runs client‑side and trust‑based: there is no
> anti‑cheat. It's about having fun, not competition.

- Works only with **Skater XL v1.2.2.8 (alpha)**
- Requires **Unity Mod Manager** (UMM `0.22.5.0`)
- You can leave any event at any time
- Players running the mod are detected automatically, so you only invite people who can play

---

## Install

1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) and point it at Skater XL.
2. Drop the built `MultiplayerEvents` mod folder into your Skater XL `Mods` directory
   (or install the zip through UMM).
3. Launch the game, connect to multiplayer, and open the UMM UI (**Ctrl + F10** by default).

## Usage

All event creation and settings live in the UMM window under **Multiplayer Events**.

### Game of S.K.A.T.E.

1. Join a multiplayer room with at least one other player who also has the mod.
2. Open the mod UI → **Game of S.K.A.T.E.**
3. Select an opponent (only modded players are listed) and press **Invite**.
4. The opponent gets an in‑world prompt — **A** to accept, **B** to decline (20s to answer).
5. On accept, a coin flip decides who sets first. After a game, **Rematch** re‑invites the same opponent.

**Setter** — land any combo Skater XL accepts (including grinds) to set a trick. You may
**retry while setting** (see *Max retries* below) to avoid bad inputs — after landing you
get a confirm prompt:

- **D‑pad left / right** — toggle between *Set trick* and *Redo (N)*
- **A / X** — confirm

**Defender** — you must land the same combo. Miss it (or bail) and you get a letter.
A trick already used earlier in the game can't be set again, just like the real thing.
First to spell **S.K.A.T.E.** loses.

The HUD calls out whose turn it is (with a sound on each turn change); while defending,
the trick to match shows as **Match: …** and your own last attempt as **You: …** so it's
clear what the game registered. A tiny **manual** flicked in right before a pop is ignored,
so it still registers as the clean trick (threshold tunable — see *Ignore manuals under*).

Two extra actions are **held** (not tapped) on the camera‑pan d‑pad axis, so they never
trigger the respawn binds on d‑pad up/down:

- **Hold D‑pad left** (while it's your turn to set) — **pass the turn** without bailing.
- **Hold D‑pad right** (while waiting on the opponent) — **spectate** them via Skater XL's
  built‑in spectate; it returns to you automatically when it's your turn again.

On **match point** (one letter from losing) the defender gets **two tries** at the set
trick before that final, game‑losing letter counts.

### Race (preview)

A checkpoint race through gates you place yourself. It's a **preview** — expect rough
edges and share feedback.

1. Open the mod UI → **Create Race (preview)** (any player can host).
2. **Add Checkpoint** drops you into a placement view. Right stick orbits the camera,
   triggers zoom, left stick moves the cursor. Press **A** twice to drop a gate (the two
   posts), repeat for each gate, then **B** or **Done Placing** to exit. Gates are raced
   in the order you place them.
3. Set the number of **laps**, then **Open Lobby** to invite the room. Players get a
   prompt — **A** to join, **B** to decline. (You'll only be asked if you're not already
   in an event.)
4. **Start Race** teleports everyone to the start line and runs a **3‑2‑1‑GO** countdown.

While racing, a tall beacon marks your next gate, a live ranking shows everyone's
progress (finishers by time, then who's furthest along), and bailing respawns you at your
last checkpoint. **Rematch Race** re‑runs the same course, and you can **Leave Race** any
time. Only people who joined take part — it never disturbs other players' events.

### Settings

| Setting | Description |
| --- | --- |
| **S.K.A.T.E. letters active color** | Color of letters you've earned. Applies live. |
| **S.K.A.T.E. letters disabled color** | Color of letters not yet earned. Applies live. |
| **Max retries while setting** | How many redos the setter gets per turn (0–5). Drives the `Redo (N)` counter. |
| **Ignore manuals under (s)** | Manuals held for less than this (0–1s) are treated as incidental and dropped from the trick used for setting/matching, so a clean trick popped right after a tiny manual still counts. |
| **S.K.A.T.E. word** | The word to spell when you *host* a game (e.g. `SKATE`, `SK8`). Letters only, up to 8. The invitee adopts the host's word. |

Color changes apply immediately; **Save** persists them between sessions.

---

## Architecture

The mod is a UMM entry point plus a couple of always‑on `MonoBehaviour`s that host a
pluggable set of *events*.

| File | Role |
| --- | --- |
| `Main.cs` | UMM entry point (`Main.Load`). Owns the mod `GameObject`, the UMM `OnGUI`, and global references (`eventManager`, `tick`, `cursor`, `settings`). |
| `MultiplayerEventManager.cs` | Owns the event **lifecycle** — create / start / stop / end — and routes the shared lifecycle Photon message. Also aborts an event when an opponent leaves the room. |
| `Event.cs` | Base class for every event: `state`, `participants`, `isWinner`, and `ToggleEventState(...)` which broadcasts a lifecycle change. |
| `GameOfSkate.cs` | The S.K.A.T.E. mode: turn/letter state machine and its own Photon messages. |
| `Race.cs`, `CheckPoint.cs`, `Cursor.cs` | A second (incomplete) mode — checkpoint racing. Not finished. |
| `Tick.cs` | Always‑on `MonoBehaviour`: subscribes to game hooks (e.g. `TrickManager.onComboEnded`), draws the in‑world HUD, reads confirm input. |
| `Notification.cs`, `Utils.cs` | On‑screen notifications and shared helpers (player ids, online checks, etc.). |
| `Settings.cs` | UMM‑persisted settings. |
| `Enums.cs` | Enums **and** the mod's shared constants: `NetCode`, `SkateMessage`, `InputBinding`, `GameConfig`. |
| `rgui/` | Vendored [RapidGUI](https://github.com/fuqunaga/RapidGUI) helper library. |

### Event lifecycle

Lifecycle changes flow through a single Photon event, `NetCode.EventLifecycle` (65),
raised by `Event.ToggleEventState`. Its payload is:

```
[ (int)MessageType, (int)EventState, (int)EventType, string targetUserId, string senderPlayerId ]
```

`MultiplayerEventManager.OnEvent` routes it:

- **Running** → a non‑owner client calls `CreateEvent` to *join* the event.
- **Stopped / End** → tear down (`Disable(true)` + `Reset()`).

`targetUserId` scopes a message to one player (`""` = everyone). `isEventOwner`
distinguishes the client that created the event locally from clients that joined it off
the network.

### Networking codes

All Photon `RaiseEvent` codes live in `NetCode` (in `Enums.cs`). They were chosen because
the base game didn't use them; keep new ones here so collisions are easy to spot.

| Code | Constant | Used by |
| --- | --- | --- |
| 65 | `NetCode.EventLifecycle` | Event create/start/stop/end (all events) |
| 66 | `NetCode.RaceParticipantPosition` | Race checkpoint progress |
| 67 | `NetCode.RaceCheckpointSync` | Race checkpoint layout sync |
| 68 | `NetCode.Invitation` | Invite handshake (keyed by `InviteMessage`) |
| 70 | `NetCode.SkateGame` | In‑match S.K.A.T.E. messages (keyed by `SkateMessage`) |

Mod presence is advertised out‑of‑band via a Photon player custom property
(`GameConfig.PresencePropertyKey`), not a `RaiseEvent` code, so other clients can list who
has the mod without any polling.

Keyed payloads (like S.K.A.T.E.) use a leading string key from a constants class
(`SkateMessage.Turn`, `.TrickSet`, `.LetterSet`, `.DefenseSuccess`, `.EventEnd`) so both
ends agree on the wire format.

---

## Extending: add a new event

The framework is designed so a new mode is mostly self‑contained in its own `Event`
subclass. To add one (say, `HighScore`):

**1. Register the type** — add a value to `EventType` in `Enums.cs`:

```csharp
public enum EventType { Null, Race, SKATE, HighScore }
```

**2. Reserve your wire format** — add Photon code(s) to `NetCode`, and if you use a keyed
payload, a message‑key class next to `SkateMessage`:

```csharp
static class NetCode { /* ... */ public const byte HighScoreGame = 71; }

static class HighScoreMessage
{
    public const string Score = "score";
}
```

**3. Write the event class** — subclass `Event` and implement `IOnEventCallback`:

```csharp
class HighScore : Event, IOnEventCallback
{
    public HighScore()
    {
        PhotonNetwork.AddCallbackTarget(this);   // start listening
        Main.tick.GOSUI = false;                 // toggle whatever HUD you need
    }

    void IOnEventCallback.OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != NetCode.HighScoreGame) return;

        // Always parse defensively — a malformed/foreign packet must not throw.
        var data = photonEvent.CustomData as object[];
        if (data == null || data.Length < 1) return;
        var key = data[0] as string;
        if (key == HighScoreMessage.Score) { /* ... */ }
    }

    public void SendScore(int score)
    {
        object[] content = { HighScoreMessage.Score, score };
        PhotonNetwork.RaiseEvent(NetCode.HighScoreGame, content,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
    }

    public void Disable() => PhotonNetwork.RemoveCallbackTarget(this); // stop listening
}
```

Use `Event.ToggleEventState(...)` (inherited) to drive the shared lifecycle so the manager
and other clients react to your start/stop/end.

**4. Hook it into the manager** — `MultiplayerEventManager`:

- Add a typed field if you need direct access (like `SKATE` / `race`): `public HighScore highScore;`
- In `CreateEvent`, instantiate it and assign `multiplayerEvent` (and your typed field).
- Add teardown in `Disable()` (`if (highScore != null) highScore.Disable();`) and null it in `Reset()`.
- If your mode is 1v1, handle forfeits in `OnPlayerLeftRoom`.

**5. Add UI** — in `Main.OnGUI`, add a creation button and any in‑event controls, following
the existing S.K.A.T.E. block.

**6. Add game hooks (optional)** — if your mode reacts to gameplay (tricks landed, spots,
etc.), subscribe in `Tick.Start` and forward to your event, the way `onComboEnded` forwards
to `GameOfSkate.OnComboEnded`.

### Conventions

- **No magic numbers/strings on the wire.** Add codes to `NetCode`, keys to a
  `*Message` class, tuning values to `GameConfig`, input ids to `InputBinding`.
- **Parse every incoming packet defensively** (`as object[]` + length/type checks). It all
  runs client‑side and trust‑based.
- **Balance every `AddCallbackTarget` with a `RemoveCallbackTarget`** in your `Disable()`,
  or you'll leak handlers across events.

---

## Building

The Visual Studio project/solution files are gitignored, so you build against your local
Skater XL install. Reference the game/UMM assemblies (the ones checked into `bin/` —
`SkaterXL.Core.dll`, `Cinemachine.dll`, `Rewired_Core.dll`, UMM, Photon, `UnityEngine.*`,
plus `0Harmony`) and compile `MultiplayerEvents.dll` with entry method
`MultiplayerEvents.Main.Load` (see `Info.json`).

## Known limitations

- Skater XL **v1.2.2.8 (alpha)** only.
- No anti‑cheat — everything is client‑side and trusted.
- **Race** mode is unfinished.
