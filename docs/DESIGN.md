# Design: a Watson replacement without an LLM

Goal: voice control of the bot crew the way Watson once promised it, only better: **instant, reliable, in German and
English, local, without a language model.** The captain says what should happen, and it happens in under a second.
The crew acknowledges with their **original game voices**.

The earlier LLM crew (tag `LLM-Version`) is kept as a separate mode (`[Allgemein] Modus = llm`).

---

## 1. What Watson was and why it no longer works

The game had voice control through IBM Watson (cloud): speech-to-text plus a "Conversation" service that recognised
intents. From the game code (`VoiceRecognitionManager`):

| Intent | Parameters | Effect |
|---|---|---|
| `set_speed` | quarter, half, max, stop, reverse | speed |
| `fire_weapons`, `cease_fire` | phasers, torpedoes | fire |
| `target`, `cease_target` | engines, shields, weapons | target a subsystem |
| `scan`, `analyze_target` | | scan |
| `engage`, `engage_target` | | attack |
| `warp`, `impulse`, `prepare`, `choose_direction` | | warp/impulse |
| `power` | offense, defense, balance, engines, shields, phasers | power |
| `repair`, `repair_all` | shields, engines, phasers, warpcore, sensors … | repair |
| `red_alert`, `cancel_red_alert` | | alert |
| `transport`, `rescue` | | transporter |
| `answer_hail`, `launch_probe`, `cloak_hunt`, `stealth`, `system_intrusion`, `modulate` | | special functions |

Watson has been shut down, so the game's voice control no longer works. It was also English-only, needed the internet
and was slow: one cloud round trip per command.

**We take over exactly this command set** and execute it via `CommandManager.TriggerCommand`.

---

## 2. Architecture (no LLM)

```
Microphone ──► end-of-speech ──► speech recognition ──► command parser ──► execution ──► feedback
 (headset)     (VAD, < 300 ms)   (local, < 400 ms)      (rules + fuzzy,    (CommandManager,  (original bot voices,
                                                         < 5 ms)            targets, macros)  console)
```

### 2.1 Speech recognition

Vosk (offline, CPU, small model ~45 MB per language) with **two recognisers on the same recording**: one restricted
to the command vocabulary (grammar) for the command, one free for proper names. The grammar is updated at runtime with
the current contact names. Measured with the small German model 0.15 on 12 test sentences:

| | free | with command grammar |
|---|---|---|
| correct | 9/12 | **11/12** (only "Typos" missing, an unknown word) |
| time per sentence | 310–590 ms | **30–115 ms** |

Unknown names ("Typos", "Ragusa") are resolved by sound matching (Cologne phonetics + Levenshtein) against the current
contact list and map targets. The models are downloaded by the mod on first use for the game language.

### 2.2 When does it listen?

Configurable; default "address or push to talk":
1. **Push to talk**: key or VR controller button – most reliable, also while talking with other players.
2. **Address**: an order only counts if it starts with a station or name ("Helm, …", "Tactical, …", "Rao, …") or is an
   unambiguous short order ("Red alert!", "Energize!"). Conversations between players trigger nothing.
3. **Always** (single player).

### 2.3 Command parser: rules, synonyms, fuzzy matching

- **Intents like Watson's**, each with synonym lists in German and English, e.g. "full speed / maximum impulse /
  volle Fahrt" → `SetSpeedFull`.
- **Placeholders**: station, subsystem, speed, **target name**, number.
- **Target names by fuzzy matching** against the current contact list.
- **Several orders in one sentence**: "Shields up, target the Ragusa, full speed!"
- **Negation and cancellation**: "Hold your fire", "Cease fire", "Stand down red alert".
- **Never guess**: if an order is too uncertain, nothing happens and a low beep sounds.

Everything is deterministic and covered by unit tests (`tests/ParserTests`).

### 2.4 Execution: orders, targets, sequences

- **Single orders** directly via `CommandManager.TriggerCommand`.
- **Targets by voice** ("Target the Ragusa"): the console target of the responsible station is set.
- **Sequences as macros with state checks**:
  - **Warp**: destination on the map → engineering `PrepareWarp` → wait until the helm faces the warp vector (it can
    take a while to turn around) → recharge the coils if the charge has faded → `EngageWarp`.
  - **Impulse**: `PrepareImpulse` → wait until ready → `EngageImpulse`.
  - **Attack**: select target → shields up → fire.
- **Standing orders** by keywords: "keep the shields down", "quarter speed only", "fire only on my order", "stay
  undetected", "as you were". Reflexes and orders are checked against them.
- **Reflexes**: shields up under fire, power to shields, repair the weakest system – always bound to the standing orders
  and paused during warp/impulse jumps (the drive needs the power).

### 2.5 Feedback: the original crew speaks

The game has hundreds of professionally voiced lines for Rao, Hoyle and T'nar in German and English (played via
`USSAegisCrew.CrewbotPlayVO`). They are the crew's voice – better than any speech synthesis, and no model is needed for
the answer. In addition: a short beep for "understood" / "not understood", and the recognised text in the overlay.

### 2.6 Multiplayer

The mod runs on the host. Every human on the bridge may give orders; the other players' voices are tapped from the
game's voice chat. The original bot voices are heard by everyone because the game plays them itself.

---

## 3. Manoeuvres: complex instructions without an LLM

Without an LLM there is no free understanding of arbitrary sentences. Instead there is a **manoeuvre library** the code
flies itself, and **sentence templates** that trigger it:

| Manoeuvre | Example | Implementation |
|---|---|---|
| **Avoid** | "Fly around the minefield" | route planner: hazard zones (mines, anomalies, enemies with radius) → waypoints around them |
| **Silent running** | "Power down all systems so they can't detect us" | standing order: shields off, torpedoes disarmed, low speed, reduced signature; reflexes blocked |
| **Approach unseen** | "Stay out of their sensor range and approach the marked target" | estimate enemy sensor radius → route avoiding all sensor circles |
| **Keep distance** | "Keep ten kilometers from the Bird of Prey" | control loop: measure distance, move to/away/stop |
| **Intercept** | "Intercept the freighter" | set target + move to, impulse if far |
| **Flee** | "Get us out of here" | away from the nearest enemy, warp/impulse if possible |
| **Circle / hold** | "Circle the station", "Hold position" | waypoints on a circle / stop |

**Heading control:** the bot helm flies a fixed heading when the crew blackboard holds `IsManualHeading = true` and
`ManualHeading` = heading in degrees (`Atan2(forward.x, forward.z)` as in the game). `Destination`/`MoveTo` only works
with selectable targets, not with free points.

**Limit:** instructions without a template are not understood; the crew does not guess.

---

## 4. The First Officer

An AI first officer without a language model (`src/STBCVoiceCrew/Captain`):

- **Perception** (`CaptainSense`): the captain's objectives and their sub-objectives (the step instructions the
  mission scripts show), star systems and the warp/impulse destinations of the helm, the active map points of the
  mission, hails, red alert, who sits where, and the tactical scanner (scan range and progress per target).
- **Mission knowledge** (`MissionKnowledge`): rules derived from the objective and step texts of the campaign and the
  random missions – distances, "scan only", "don't destroy", "knock their shields down before beaming", safe distances,
  escapes, particle gathering, silent running, energy waves. Status lines ("Status: 25% complete") do not decide the
  next step.
- **Decision tree** (`FirstOfficer`): hail → danger (red alert once per fight, shields, power, fight attackers within
  reach) → objective: warp to the right system (also "Warp to X, Y, or Z"), break away from close enemies first, impulse
  to the places the objectives name (not to mines, debris or stars), approach to scan range and stop, scan the next
  matching unscanned object, attack the nearest enemy for "eliminate" objectives, beam, protect, escape.
- **Orders one at a time**: he speaks the order, then the bot executes it, then he waits until the crew has answered
  (the game's voice queues) before the next one. No planning while a jump is in progress.
- **Advice mode** when a human is captain: warnings and hints only when nobody else is talking, never during briefings.
- **Autopilot** ("Number One, take the bridge"): the player stays in the chair and watches. Used together with the test
  mode (`[Test] AutoTest`), which starts solo missions without a click, for unattended test runs.
