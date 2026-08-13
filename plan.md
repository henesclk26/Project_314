# Project 314 - Demo Gameplay Implementation Plan

## Objective

Implement the first playable social-deduction gameplay loop for the Unity project.
The online demo supports 3-8 players and exactly one killer/rogue robot. Quick
Test is a separate single-player flow for faster iteration. Existing task UIs
are demo content only; the gameplay architecture must support a much larger task
pool later.

Build server-authoritative systems with Unity Netcode. Keep task, role, kill,
meeting, reward, tool, and match state synchronized for host and clients. Do not
break existing normal task UIs. Existing F1 sabotage switching remains available
only in Editor/Development test mode; it must never grant a normal player access
to a rogue task in a production match.

## Core Match Rules `[DONE - CODE / ONLINE VERIFY]`

- Exactly one killer is used in the demo. Reuse the existing villager/killer role
  distribution system where possible.
- There is no round timer.
- Villagers win by either completing the crew task-progress target or ejecting the
  killer.
- The killer wins when the number of living killers equals the number of living
  villagers.
- Resolve win conditions atomically on the server. Ejecting the only killer gives
  an immediate villager win. A kill/death/disconnect that creates killer-villager
  parity gives an immediate killer win. Once MatchEnded is set, ignore all later
  events from that simulation tick.
- Roles are never revealed after an ejection.
- A player who dies or is ejected loses all owned tools, passive effects, pending
  upgrade selection, and active task reservation.
- A dead/ejected player becomes a ghost with free camera movement. Ghosts cannot
  perform tasks, interact with game objects, vote, help living players, or talk to
  living players. Ghosts may hear and talk only to other ghosts once voice chat is
  implemented.

## Match Start and Respawn Flow `[DONE - CODE / ONLINE VERIFY]`

1. Distribute roles privately.
2. Assign every living player one balanced personal task immediately and allow
   normal task interaction right away.
3. Run a 30-second system-boot protection phase:
   - No kill.
   - No sabotage.
   - Personal and cooperative villager tasks remain available and can contribute
     to crew progress and upgrade points normally.
   - Players can move, complete work, inspect the map, and position themselves.
4. At the end of the protection phase, grant the killer's initial sabotage
   availability/state. Detailed sabotage design will be implemented later.

Emergency meetings are unavailable for the first 60 seconds from match start.

After every meeting, respawn all living players at the match spawn points. Add a
5-second global kill/sabotage lock after the meeting closes so clients, cameras, and
UI can settle safely.

## Gameplay UI Inventory `[DONE - CODE / VISUAL + ONLINE VERIFY]`

Build all new gameplay UI with Unity UI Toolkit and keep it visually consistent
with the existing Project 314 terminal/HUD design language. Reuse the existing
task UIs; do not redesign their internal gameplay unless a TaskRun integration
requires a small state indicator. Gameplay UI should be readable at a glance,
avoid role-revealing information, and never block normal play unless explicitly
listed as a blocking screen.

| UI surface | Required contents | When it appears | Purpose |
| --- | --- | --- | --- |
| Match start / role reveal | Role name, short faction objective, 30-second protection countdown, first assigned task title and room | At match start | Privately explains the player's role and establishes the opening phase without revealing anyone else. |
| Core gameplay HUD | Crew task progress, current personal task title, target room, current match phase, protection/meeting lock countdown when relevant | During normal play | Keeps the player oriented and shows the shared villager win race. Do not show killer identity, other players' private tasks, or exact sabotage state. |
| Task assignment toast | New task title, room, task category, optional short system message | When a TaskRun is assigned or reassigned | Makes dynamic repeatable task distribution understandable without opening a menu. |
| Task interaction prompt | Existing-style `[F]` interaction prompt, reservation/busy message, task owner access state | Near a task object | Explains whether the player can start the assigned task or why a terminal is unavailable. |
| Terminal hack prompt | `TERMINAL HACK PREPARING` state, 15-second preparation countdown, then killer-only `TERMINALI HACKLE` prompt | After a normal task at a sabotage-capable terminal is completed | Makes the temporary rogue-task window readable without revealing the killer to villagers. |
| System blackout overlay | `SYSTEM OFFLINE` label, short red disruption flash, remaining lock timer | While Killer Tool 2 is active | Replaces normal task interaction prompts during a lab-wide terminal lock without revealing the killer. |
| Identity scramble alert | `IDENTITY SIGNAL DESYNC`, brief red/amber flash, 30-second remaining timer | While Killer Tool 3 is active | Announces the shared visual anomaly without identifying the tool owner or exposing original player colors. |
| Cooperative task overlay | Shared task title, player's private role, required participant status, paused/busy state, saved progress where applicable | During pressure/reactor/co-op TaskRuns | Coordinates multiple villagers while preserving private assignments and preventing accidental soft locks. |
| Body report prompt | Existing-style `[F] REPORT UNIT` prompt | Within 4 meters of a reportable body | Lets any living player, including the killer, immediately begin a meeting. |
| Emergency meeting prompt | Hold-to-call indicator, global cooldown countdown, unavailable state during locks | At the emergency meeting object | Communicates unlimited use with a shared cooldown and prevents accidental calls. |
| Meeting screen | Reporter label, alive/dead player cards, discussion timer, voting timer, vote buttons, abstain state, vote result | On body report or emergency call | Provides the social-deduction decision phase. It must hide roles, disable gameplay input, and support a total duration of at most 60 seconds. |
| Ejection result overlay | Ejected player name/card, no role reveal, tie/no-ejection result when applicable, short return countdown | After voting | Clearly resolves the vote without revealing faction information. |
| Upgrade choice screen | At 2 points: three passive cards. At 4 points: every currently eligible active-tool card from that role's pool. Include card name, clear effect description, selected card confirmation, and movement-lock state. | Immediately after a task completion crosses the 2 or 4 point threshold | Turns task contribution into a meaningful build choice. This is a blocking and vulnerable screen; it closes without reward on death, ejection, or meeting. |
| Passive/tool HUD | Small selected passive icon plus an armed, triggered, or expended tool icon/state; unavailable state during meeting | During normal play after upgrade selection | Gives the player reliable awareness of their current build without cluttering the task HUD. |
| Threat Sensor alert | `WARNING // NEARBY UNIT OFFLINE`, red/amber pulse, 2.5-second display | A living Threat Sensor owner is within 12 meters of a kill | Gives nearby-event awareness without direction, victim, corpse, distance, or killer identity. |
| Valve Override emergency alert | Valve emergency title, 30-second server timer, brief instruction to reach the three valves | When the killer selects Valve Override | Creates coordinated villager response pressure. It does not reveal the killer or show the killer's location. |
| Ghost HUD | Ghost status, free-camera state, no-interaction status, optional dead-player voice status once voice is added | After death/ejection | Makes loss of agency unambiguous and prevents accidental attempts to affect the living match. |
| Match result screen | Winner faction, non-spoiler summary of result condition, return-to-lobby countdown | When crew progress/ejection or killer parity ends the match | Ends the round clearly and returns all players to the lobby. |
| Lobby return state | Match reset/return status | Immediately after match result | Ensures players understand that a new round will start from clean state. |
| Voice status indicators (deferred) | Proximity speaking indicator, meeting global-voice state, ghost-voice state | After proximity voice is implemented | Makes voice-channel rules legible without exposing role information. |

### UI Behavior Rules

- Match start, meeting, ejection, upgrade choice, and match result are blocking
  screens and control player movement/input as defined by their match phase.
- Task, alert, passive/tool, and ghost HUD elements are non-blocking overlays.
- Meeting and post-meeting lock screens must pause/hide task interaction prompts.
- A task terminal reserved for someone else should show a clear busy/system-locked
  message rather than silently failing interaction.
- The killer sees the same normal task and meeting UI as villagers. Role-specific
  UI is private and limited to the killer's own upgrades/tools/sabotage state.
- Do not use UI to expose exact player positions, exact death times, or certain
  role evidence in the demo.

## Kill and Body Rules `[DONE - CODE / ONLINE VERIFY]`

- Base kill cooldown: 30 seconds.
- Base kill range: 4 meters, matching terminal interaction range.
- A valid kill requires line of sight through a Physics.Raycast; it must not work
  through walls.
- The killer does not stop, slow down, or play a blocking animation during a kill
  in this demo.
- The victim becomes a body lying on the ground at the death position.
- The body remains until it is reported or a meeting begins.
- The killer can report bodies, like Among Us and Goose Goose Duck.
- Body report range matches the 4-meter terminal interaction range.
- Bodies cannot be moved, hidden, or reported twice in this demo.
- Starting a meeting destroys the reported body and any other remaining bodies.
- Store death position and server time for later evidence/tool systems.

## Meetings and Voting `[DONE - CODE / ONLINE VERIFY]`

- Reporting a body starts a meeting immediately.
- Emergency meetings are unlimited but use one global cooldown, initially 60 seconds
  from the end of the previous meeting. The emergency control should require a
  2-3 second hold at its physical location.
- The emergency control is also unavailable during the first 60 seconds of the
  match, including the initial 30-second protection phase.
- Use a 2D UI inspired by social deduction games: player name cards, alive/dead
  state, reporter state, discussion phase, voting phase, vote result.
- Total meeting duration must not exceed 60 seconds:
  - Discussion: 15-20 seconds.
  - Voting: 35-40 seconds.
- Players who do not vote count as abstaining.
- A tie ejects nobody.
- Ejected role is hidden.
- Meeting UI disables normal movement, tasks, kills, tools, and sabotage.
- Active tool effects must not continue through a meeting: clear Blackout and
  Identity Scramble immediately, and preserve untriggered automatic villager
  defenses for a later valid matching effect.
- When a meeting ends, set the killer's kill cooldown to a fresh 30 seconds. The
  5-second post-meeting lock does not consume this cooldown; the killer still has
  the full 30 seconds remaining once normal play resumes.
- At meeting end, clean up bodies, remove deceased-player effects, respawn living
  players, and apply the 5-second post-meeting lock.

## Repeatable Personal Task System `[DONE - CODE / ONLINE VERIFY]`

Existing task content is currently global/one-shot in several places. Build a new
server-authoritative task-run layer above the existing UIs so tasks can be reused
without turning existing terminal logic into a one-time match win condition.

### Initial Task Content Classification `[DONE - CODE]`

Normal villager task pool for the demo:

1. MissionComputer password task (MissionComputer and MissionComputer 2 are two
   physical instances of this task type).
2. WaveFrequencyTerminal normal frequency task.
3. CircuitMission normal circuit task.
4. PressureTerminal cooperative pressure-calibration task using its two dedicated
   pressure valves. It is a normal villager task; completing it once enables the
   killer's separate three-valve Valve Override offer, but does not start or share
   the two-valve task session.
5. ReactorTerminal cooperative reactor task.

The existing F1-triggered three-valve Valve Mission is not a normal villager task
in production. It is used only by the killer's Valve Override tool. Keep F1 as a
development-only test shortcut for this content.

The DoorLockTerminal -> Battery2 -> Generator1 chain is map infrastructure / a
special opening sequence, not a standard repeatable personal task for now.
Security camera terminals are information tools, not task-progress objectives.

### Two-Valve Pressure Calibration Cooperative Assignment `[DONE - CODE / ONLINE VERIFY]`

- Treat the two-valve PressureTerminal calibration as a three-villager
  cooperative TaskRun, not as three independent personal tasks.
- Its three private assignment roles are: Pressure Terminal Operator, Valve003
  Technician, and Valve004 Technician.
- Select only living villagers for these roles. Never assign the killer to a
  required cooperative slot, because the killer could intentionally make the
  task impossible by withholding input.
- When 4 or more villagers are alive, choose three eligible villagers using a
  weighted random selection that avoids their two most recent task types and
  balances map distribution. Do not use a naive fixed player order.
- When exactly three villagers are alive, assign all three of them to the
  cooperative task. This is valid and should work without special-case failure.
- A cooperative assignment is a priority overlay: pause each selected player's
  normal personal TaskRun and restore it, with its saved progress, after the
  cooperative run resolves or is cancelled.
- The killer receives no villager TaskRun. They may still open and perform an
  otherwise available solo terminal task as an alibi, but it never awards crew
  progress, upgrade points, or a terminal-hack preparation window.
- Do not offer/start this cooperative task when fewer than three villagers are
  alive. Keep it out of the assignment pool rather than creating an impossible
  objective.
- If an assigned villager dies or disconnects before the task starts and at least
  three villagers remain, replace only the missing role with another eligible
  living villager. If the task is already active, pause its saved progress, then
  fill the missing role and resume only after the shared valve session is idle.
- If the living villager count drops below three during this task, cancel the
  cooperative run, preserve its saved task progress for diagnostics, release all
  reservations/valve locks, and return surviving players to their paused personal
  TaskRuns. Do not award crew progress for an incomplete cancelled run.

Existing rogue/sabotage task content:

1. MissionComputer file sabotage.
2. CircuitMission power-diversion sabotage.
3. WaveFrequencyTerminal satellite-routing sabotage.

### Terminal Hack Unlock Flow `[DONE - CODE / ONLINE VERIFY]`

- Rogue tasks are not randomly assigned as personal TaskRuns. They are unlocked
  per physical terminal after that terminal's normal task is completed.
- When any valid villager normal TaskRun completes at a sabotage-capable
  terminal, the server creates a terminal-owned `HackPreparing` state and
  starts a 15-second server timer. A killer may also perform a temporary normal
  terminal task as an alibi, but that isolated Alibi TaskRun never contributes
  to crew progress, upgrade points, or hack preparation.
- During preparation, the terminal interaction UI shows a neutral
  `TERMINAL HACK PREPARING` message and countdown. It must not reveal who
  completed the normal task or identify the killer.
- When the 15 seconds expire, the server changes the terminal to
  `HackAvailable`. Only the living killer receives the `TERMINALI HACKLE`
  interaction prompt. Villagers do not receive a usable hack prompt and cannot
  open the rogue screen.
- The killer must be looking at the unlocked terminal and press `F`. This is the
  production role-authorized path for the existing F1 sabotage UI. F1 remains a
  development-only shortcut and cannot bypass the terminal's `HackAvailable`
  state in a production match.
- Starting the hack consumes the terminal's current hack window and creates a
  separate rogue `TaskRun` for the killer. The normal task run is already
  completed and is never reset. Normal and rogue progress are stored separately.
- A successful rogue task grants the killer exactly `+1 Killer Sabotage Point`.
  It never increases crew task progress and does not grant a normal upgrade
  point. The point is server-owned and reserved for the limited sabotage system.
- Closing the rogue UI, losing focus, or leaving the terminal does not silently
  award the point. Preserve the rogue TaskRun according to the normal
  Assigned/Reserved/InProgress lifecycle so the killer can resume it while the
  hack window remains valid.
- After rogue completion or cancellation, put that physical terminal into the
  existing 45-75 second server cooldown. It cannot prepare another hack during
  this cooldown, and its normal task assignment rules remain independent.
- If a meeting starts during the 15-second preparation or while a rogue task is
  active, pause the preparation/task state, hide interaction prompts, and resume
  it after the meeting only if the killer is still alive and the match remains
  active. A meeting must never award or consume sabotage progress by itself.
- If the terminal's normal task is completed while it already has a pending or
  active hack state, do not stack another window. Keep one deterministic state
  per physical terminal.
- Use a server-owned terminal identity rather than only the task type. The two
  MissionComputer instances, for example, must be able to host independent
  normal and rogue cycles.

### TaskRun Lifecycle `[DONE - CODE / ONLINE VERIFY]`

Each assignment must be represented as a server-owned TaskRun with this lifecycle:

Assigned -> Reserved -> InProgress -> Completed / Cancelled -> Cooldown.

- Each living villager has one active personal task assignment at a time. The
  killer may create one temporary solo `Alibi` TaskRun by opening an available
  password, wave, or circuit terminal; it is resumable but grants no reward.
- Assign tasks with balanced map distribution. Do not always send every player to a
  unique distant location; occasionally place players in nearby areas so witnesses,
  alibis, and risk naturally occur.
- A terminal can be reserved by only one personal task owner at a time. Other
  players cannot use that normal task terminal while reserved.
- Do not assign a player either of their two most recently completed task types.
- A completed terminal enters a 45-75 second server-selected cooldown before it
  can be assigned again.
- Task progress persists if the task UI is closed or interrupted.
- Do not let a personal assignment alone lock a shared physical task group. A
  reservation identifies the owner, but an exclusive world-interaction lock is
  acquired only when that task actually starts its physical session.
- For tasks that share physical objects, store persistent TaskRun progress
  separately from the short-lived world-interaction session. A paused run keeps
  its progress but releases the shared-object lock; when it resumes, it must
  reacquire the lock from the server before accepting input.
- If a meeting starts, pause task runs and retain their reservations. Resume after
  the meeting for the same owner if still alive.
- If the owner dies, is ejected, disconnects, or otherwise loses ownership, retain
  the task progress, cancel the reservation, put the terminal into a 15-second
  maintenance delay, then reassign the task to an eligible living player without
  an active assignment.
- Villagers see their task title and target room in HUD. Start with room names;
  add a simple general-direction compass marker later if desired. Do not implement
  full pathfinding arrows for the demo.
- Killers are never assigned villager TaskRuns. They may voluntarily perform a
  normal solo terminal task as an alibi, but it contributes no crew progress or
  upgrade points and cannot create a hack window by itself.
- Killers may choose a rogue task when its role/sabotage logic permits. This must
  not contribute to crew task progress.

### Generic Task Availability and Cooperative Safety Rules `[DONE - CODE / ONLINE VERIFY]`

Every task definition must declare, in data rather than per-task ad hoc code:

- Required participant count and any named role slots.
- Minimum living villager count needed before it may be offered or started.
- Whether the killer may receive it as a normal assignment.
- Whether it is solo, cooperative, or a special map/system sequence.
- Its shared-object/session-lock group, if it uses one.
- What happens when a required participant dies, disconnects, a meeting starts,
  or the task loses viability.

Apply these rules to every assignment and every start request:

- Solo villager tasks require one living villager and can be reassigned using the
  standard TaskRun flow.
- Cooperative tasks are offered only when all required villager slots can be
  filled. The killer is never a required slot in a cooperative villager task.
- A cooperative task enters InProgress only after the server verifies all of its
  required slots are filled by living, eligible villagers.
- If a participant disappears and enough villagers still exist, replace only the
  missing role, preserve progress, and resume when its world-session lock is
  available.
- If fewer villagers than the task minimum remain, cancel/pause that cooperative
  run, release its reservations, and remove that task type from the current
  assignment pool. Never leave an impossible task active.
- The crew task target does not become impossible because repeatable eligible solo
  tasks remain available as replacements. Do not dynamically reduce the match
  target merely because a cooperative task type became unavailable.

Initial data requirements:

| Task type | Minimum living villagers | Required villagers | Killer can receive normal version |
| --- | ---: | ---: | --- |
| Password, Wave, Circuit | 1 | 1 | Yes |
| Two-valve PressureTerminal calibration | 3 | 3 | No |
| Reactor fuel and three-lever synchronization | 3 | 3 | No |
| Valve Override emergency | 3 | 3 | No; killer triggers it only |

## Crew Task Target

Set the target from the number of villagers at match start:

    crewTaskTarget = startingVillagerCount * 3

Initial table:

| Players | Villagers | Crew task target |
| --- | --- | --- |
| 4 | 3 | 9 |
| 5 | 4 | 12 |
| 6 | 5 | 15 |
| 7 | 6 | 18 |
| 8 | 7 | 21 |

Each completed personal normal task contributes one crew task point. Cooperative
task completion contributes one crew task point once, regardless of participant
count. Every valid living participant who completed their required cooperative
role receives one upgrade point. Keep contributor tracking server-owned and
validate actual role input before awarding either reward.

## Information Rules

- Normal task completion is real for all roles. A killer can truthfully complete
  normal tasks and help crew progress to build an alibi.
- There are no universal terminal logs or role-revealing evidence systems in the
  demo.
- Security camera rooms continue to show their current camera information.
- Seeing someone near/performing a cooperative task can provide social evidence,
  but no information source should identify a role with certainty.
- Do not create fake task logs or killer-only knowledge of dead players in this
  first implementation.

## Upgrade Economy `[DONE - CODE / ONLINE VERIFY]`

- Players earn upgrade points by completing assigned tasks, not by merely opening
  a task UI.
- Killer sabotage tasks earn killer-only upgrade/sabotage progress and never add
  to crew task progress.
- Upgrade thresholds: first selection at 2 upgrade points; second selection at 4
  upgrade points.
- The first selection offers all three passive upgrades. The player chooses one.
- The second selection shows every currently eligible active-tool card from that
  role's pool and grants exactly one selected tool. Do not show locked cards or
  add passive-enhancement fallbacks because each player may own only one passive
  in this demo.
- Maximum two upgrade selections per player per match.
- When a task completion crosses a threshold, show the upgrade selection UI
  immediately after that task closes. The selecting player is movement-locked and
  vulnerable while choosing.
- The selection screen closes and the pending reward is lost if the player dies,
  is ejected, or a meeting begins.
- Tool use is disabled during meetings. Active disruption effects are cleared when
  a meeting starts; untriggered automatic villager defenses remain armed.
- All player-owned placed effects and passive effects disappear when their owner
  dies or is ejected.

## Passive Upgrade Pool (Demo) `[DONE - CODE / ONLINE VERIFY]`

Each player can select one passive. Passive effects do not stack. Keep all values
centralized/configurable for playtests.

### Villager Passives

1. OVERDRIVE SERVOS
   - Movement speed +10%.

2. FORENSIC CACHE
   - When reporting a body, show a broad death-age band only:
     0-10 seconds, 10-25 seconds, or 25+ seconds.
   - Never show exact death time or killer identity.

3. THREAT SENSOR
   - When a living owner is within 12 meters of a kill event, show a 2.5-second
     red/amber HUD warning:
       WARNING // NEARBY UNIT OFFLINE
   - Do not show direction, distance, corpse position, victim identity, or killer
     identity.
   - The owner does not receive the warning if they are the victim.
   - Do not trigger it during meetings, death/ghost state, or upgrade selection.

### Killer Passives

1. PURSUIT PROTOCOL
   - Kill cooldown becomes 25 seconds.
   - Never allow cooldown below 25 seconds in this demo.

2. ESCAPE ROUTINE
   - After a successful kill, movement speed +15% for 5 seconds.
   - No stacking. Disable during meeting, task UI, and post-meeting lock.

3. AMBUSH PROTOCOL
   - Kill range becomes 4.75 meters.
   - Physics line-of-sight validation remains mandatory.

## Active Tool Pool `[DONE - CODE / ONLINE VERIFY]`

Implement two active tools for villagers and three for the killer after the core
match loop works. Do not implement tool effects before deciding their exact
counterplay, cooldown, charges, UI, world feedback, and interaction with meetings.

The second upgrade choice presents every currently eligible card and grants
exactly one tool. For the killer, show all eligible cards from the Valve Override,
System Blackout, and Identity Scramble pool at once. Valve Override is simply
omitted when its pressure-task, shared-valve, or living-villager conditions are
not met. Never show a locked tool card or grant a second tool in the demo.

For villagers, show both Priority Uplink and Identity Anchor cards. The selected
tool becomes an armed one-use automatic defense, not a manually activated HUD
button. Its HUD icon must clearly display `ARMED`, then animate when it triggers
and remain marked `EXPENDED` for the rest of the match.

### Villager Tool 1: PRIORITY UPLINK `[DONE - CODE / ONLINE VERIFY]`

- This is a one-use automatic defense. When the killer starts System Blackout,
  every living owner automatically consumes Priority Uplink; no key press or
  confirmation is required.
- The server grants the owner an 8-second local blackout bypass for only their
  currently assigned, otherwise eligible task interaction. The global blackout
  remains active for everyone else and no other terminal is restored.
- If the owner already has that task UI open when Blackout begins, keep that one
  screen open for the bypass duration. Otherwise show a small cyan
  `PRIORITY UPLINK ACTIVE` HUD confirmation and allow only that owner to open
  their assigned task during the 8-second window.
- Resolve Blackout atomically on the server in this order: determine all valid
  Priority Uplink owners, consume and grant their 8-second scoped bypasses, then
  close task UIs only for players without a bypass. A valid Uplink owner must
  never have their protected task UI closed by the same Blackout event.
- The bypass never grants access to an unassigned task, another player's private
  co-op role, a system-locked valve session, or a terminal made unavailable by
  ordinary task eligibility rules. It preserves the normal TaskRun validation.
- If the owner is dead, in a meeting, has no eligible assigned task, or the
  match is ending when Blackout starts, do not consume the tool. Keep it armed
  for a later valid Blackout.

### Villager Tool 2: IDENTITY ANCHOR `[DONE - CODE / ONLINE VERIFY]`

- This is a one-use automatic defense. When the killer starts Identity Scramble,
  every living owner automatically consumes Identity Anchor; no key press or
  confirmation is required.
- For the full scramble duration, the owner keeps their true networked player
  color while all non-anchored robot visuals use the common scramble color. Add
  a small stable cyan identity ring/glyph above the owner's world model so the
  effect remains readable at a glance.
- When choosing the scramble's common color, the server excludes the true color
  indices of all living armed Identity Anchor owners. The cyan ring/glyph remains
  a fallback guarantee if a future effect or palette change could otherwise
  create a visual collision.
- Do not reveal the owner's role, killer identity, player names, or other
  players' original colors through this effect. It is local coordination/alibi
  information only, not role-proof evidence.
- If the owner is dead, in a meeting, or the match is ending when Identity
  Scramble starts, do not consume the tool. Keep it armed for a later valid
  scramble.

### Killer Tool 1: VALVE OVERRIDE `[DONE - CODE / ONLINE VERIFY]`

- This is an immediate-use tool card. Selecting it from the killer's upgrade
  choice starts it immediately; it is not stored for later manual activation.
- Do not include this card in the killer's upgrade-choice pool until the normal
  two-valve PressureTerminal calibration task has been successfully completed at
  least once during the current match. Track this with a server-owned per-match
  eligibility flag. Completing that normal task again does not stack or add more
  eligibility; the flag simply remains true for the rest of the match.
- Offer this card only when all three conditions are true at choice generation:
  the per-match eligibility flag is true, the shared valve group is idle, and at
  least three villagers are currently alive. Do not display it as a locked card.
- If Valve Override is offered in an open killer upgrade-choice screen, create a
  server-owned pending Valve Override reservation immediately. While this pending
  reservation exists, do not assign or start the two-valve pressure calibration.
  This guarantees the killer does
  not lose a valid upgrade choice because villagers begin a conflicting valve run
  while the choice screen is open.
- Release the pending reservation if the killer selects another card, dies, is
  ejected, a meeting begins, the upgrade screen closes without a choice, or the
  match ends. If the killer selects Valve Override, promote the pending
  reservation directly into the active valve-session lock in the same server
  transaction.
- On selection, trigger the existing F1-triggered three-valve Valve Mission as a
  30-second emergency for the living villagers.
- All living villagers receive a clear valve emergency notification. Only the
  three privately assigned villagers can operate their assigned valve; other
  villagers may investigate, witness, and communicate but cannot provide input.
  The killer receives no direct location or identity information from this tool.
- Create a three-villager cooperative emergency run for Valve Override: assign one
  living villager to each valve, pause their personal TaskRuns, and require each
  role's own valve input. If more than three villagers are alive, select the three
  roles with the same weighted assignment rules as the pressure task. If a role is
  lost while at least three villagers remain, pause and replace that role; if fewer
  than three remain, resolve the emergency as unsuccessful.
- The 30-second timer is server-authoritative and pauses during meetings.
- If villagers complete the three-valve mission before the timer expires, end the
  emergency with no extra reward for the killer.
- If villagers do not complete the three-valve mission before the timer expires,
  end the emergency and award the killer +1 Killer Sabotage Point. This is a
  separate server-owned resource for the later sabotage system; it is not an
  upgrade point and does not unlock a third upgrade choice in this demo. Do not
  add crew task progress and do not apply an additional instant-win or permanent
  penalty in the demo.
- The tool cannot be selected/triggered while the normal two-valve pressure
  calibration, another Valve Override emergency, a meeting, the boot protection
  phase, or the post-meeting lock is active.
- Valve003 intentionally belongs to both valve tasks. Do not split or duplicate
  this object. Create one server-owned shared valve-session lock with three
  states: Idle, PressureCalibrationActive, and ValveOverrideActive.
- A normal two-valve calibration acquires the shared lock only when its physical
  pressure/valve session begins. A three-valve Valve Override acquires it in the
  same server transaction that accepts the killer's tool choice. This makes two
  simultaneous start requests deterministic: the first accepted server request
  owns the session; the other is rejected and the requesting client remains in
  its current UI state.
- While one session is active, the other task cannot start and all of its
  interactables reject input with a clear busy/system-locked state.
- If a normal pressure run has partial progress but is paused, preserve that
  progress and release the shared session lock. It may resume only when the lock
  returns to Idle. A Valve Override may therefore run in between two attempts of
  the same repeatable pressure task without state collision.
- Release the lock when the active session resolves, is cancelled, pauses due to
  inactivity, or is cleaned up by meeting/death/match end. Use a short server
  inactivity timeout for a started but abandoned normal valve session; keep the
  saved TaskRun progress, but do not allow it to hold Valve003 forever.
- A Valve Override emergency reserves all three valves used by the Valve Mission
  until it resolves, then releases the shared valve-task lock into the normal
  TaskRun cooldown flow.

### Killer Tool 2: SYSTEM BLACKOUT `[DONE - CODE / ONLINE VERIFY]`

- This is an immediate-use tool card. Selecting it from the killer's upgrade
  choice starts a single 15-second, lab-wide terminal lock; it is not stored for
  later manual activation.
- Keep one server-authoritative `SystemBlackoutEndServerTime` (or equivalent
  active/until state). Every client derives the remaining duration from server
  time, so host, clients, reopened terminals, and late joiners agree on the
  lock state.
- On activation, first resolve valid Priority Uplink bypasses, then close every
  other open crew task UI through its existing normal close path. Never reset
  task progress, task assignments, circuit state, pressure state, or carried fuel
  because a screen was forcibly closed.
- For the lock duration, all crew-task terminal and valve/fuel interaction
  prompts are replaced with `SYSTEM OFFLINE`; their usual `[F]` interaction
  prompt is hidden and the interactables reject task-open/input requests at the
  server. Existing task assignment guidance stays visible so players know which
  work will resume afterward.
- Use the shared rogue-task palette: a brief dark-red signal flash, ember-red
  `SYSTEM OFFLINE` text, and a small 15-to-0 second remaining timer. Do not
  display a source, direction, culprit name, or terminal-specific explanation.
- The killer sees the same general blackout feedback only. The tool never grants
  player locations, task assignments, or direct confirmation that a particular
  crew member was interrupted.
- Do not offer or activate System Blackout during boot protection, an active
  meeting, the post-meeting lock, match end, or an already active blackout. If a
  meeting begins during an active blackout, clear it immediately; meeting UI
  must never remain blocked after the meeting ends.
- `SYSTEM OFFLINE` is a temporary availability state, not a task failure. When
  the server timer expires, restore normal interaction prompts and each task's
  existing eligibility/busy rules automatically.
- This tool has no kill cooldown interaction, no direct damage, and no sabotage
  point reward. Its demo role is purely to create a short timing window for the
  killer.

### Killer Tool 3: IDENTITY SCRAMBLE `[DONE - CODE / ONLINE VERIFY]`

- This is an immediate-use, single-use tool card. Selecting it from the killer's
  upgrade choice immediately starts a 30-second global color scramble; it is not
  stored for later manual activation.
- The server chooses one random valid robot color index for the activation and
  writes it with a server-authoritative `IdentityScrambleEndServerTime` (or
  equivalent active/until state). Every client calculates the remaining time from
  server time and renders the same color.
- While active, every spawned player robot, including the killer and dead robot
  bodies that remain in the world, renders in the chosen common color. Player
  names, meeting cards, role state, and the underlying personal color assignment
  are not changed.
- Do not overwrite `playerColorIndex`. Update the existing player color renderer
  to resolve an effective display color: use the global scramble color while the
  server state is active, otherwise use the player's own networked color index.
  This guarantees exact restoration after expiry, meeting cleanup, late joins,
  and object respawns.
- Apply the same effective-color resolver to the spawned body visual/prefab. If a
  body uses a copied or detached renderer rather than the live player hierarchy,
  give its visual component the victim's base color index and subscribe it to the
  global scramble state so world bodies change and restore with player robots.
- Show every living player a short neutral red/amber system flash such as
  `IDENTITY SIGNAL DESYNC`, plus a small 30-to-0 second indicator. Do not reveal
  the chosen tool owner, the killer's location, or the original color of any
  player through this UI.
- Do not offer or activate Identity Scramble during boot protection, an active
  meeting, the post-meeting lock, match end, or another active identity scramble.
  If a meeting begins while it is active, clear the scramble immediately so the
  meeting and its return-to-world state begin with normal colors.
- This tool has no kill cooldown interaction, no direct damage, no task-progress
  loss, and no Killer Sabotage Point reward. Its role is temporary visual alibi
  pressure and confusion in open play.

## Sabotage (Deferred Design) `[PARTIAL - TOOLS DONE / FULL LOOP TODO]`

Do not implement the full sabotage loop yet, except for the Valve Override,
System Blackout, and Identity Scramble tools defined above. The current design
direction is:

- The killer earns limited sabotage availability through specified task/sabotage
  completion thresholds. In this demo, the first concrete unlock is the
  terminal-level normal-completion -> 15-second preparation -> killer hack flow
  defined above.
- The killer cannot bank unlimited sabotage charges.
- Sabotage directs players and creates opportunities; it should not be an instant
  alternate win condition in the demo.
- A future unresolved sabotage should temporarily disrupt a terminal/system rather
  than directly end the match.
- Emergency meeting availability may be affected by certain future sabotages.
- Keeping the crew task target fixed when cooperative tasks become unavailable is
  an intentional demo-level killer advantage. Eligible repeatable solo tasks keep
  a task victory technically possible, but a reduced villager population should
  strongly favor a killer parity win.
- The first economy guard is now server-enforced: Killer Sabotage Points have a
  maximum demo balance of 2 and never grant a normal upgrade point. This keeps
  the reserved resource bounded until a spending/activation loop is designed.

## Voice Chat (Deferred Implementation) `[TODO - DEFERRED]`

Do not block core gameplay implementation on voice chat. Use external voice chat
for early technical tests if necessary. Before social-balance playtests, implement
server-controlled proximity voice:

- Free-roam living players use proximity voice, initial distance approximately
  12 meters.
- Meeting changes living players to a global meeting voice channel.
- Dead/ejected players use a separate ghost channel and can hear/talk only to
  ghosts.
- Wall occlusion is not required for the first voice-chat version.

## Technical Match Management `[PARTIAL - DEMO LIMITS APPLY]`

- Keep all roles, living/dead state, task runs, crew progress, kill cooldowns,
  meeting state, upgrades, tool ownership, Killer Sabotage Points, bodies, and
  win state server-authoritative.
- Reconnecting players are not supported in the demo.
- New client connections are rejected after the match starts; late joiners do
  not enter the simulation without a role, task assignment, and reset state.
- AFK players receive no automatic handling in the demo.
- Host migration is deferred. If the host disconnects, end the match and return
  all players to the lobby cleanly.
- At match end, return to the lobby automatically and reset every match-owned
  state before the next match: roles, bodies, task runs, cooldowns, crew progress,
  killer state, upgrades, tools, reservations, and UI.

## Implementation Checklist

Implement this order. Each step must compile, work for host and client, and be
tested before the next one begins. New code should extend the existing Netcode,
player controller, terminal interaction, and UI Toolkit patterns rather than
replace working task implementations.

### Status Legend

- `[DONE - CODE]`: implemented in the project and compile-checked.
- `[IN PROGRESS]`: code exists, but the listed online/manual regression is still
  pending.
- `[TODO]`: not implemented yet or intentionally deferred.
- `[NON-GOAL]`: explicitly outside the first demo scope.

1. **[DONE - CODE / ONLINE VERIFY] Match authority and phases**
   - Status: `[DONE - CODE]`; host/client phase, countdown, protection, and win
     behavior still require the online regression pass.
   - Audit the existing role distribution and create/extend one central
     `MatchFlowManager` server authority.
   - Add server-owned match phases: `Lobby`, `BootProtection`, `Active`,
     `Meeting`, `PostMeetingLock`, and `Ended`.
   - Implement 30-second boot protection, 60-second first-emergency lock, and
     atomic crew/killer win checks.
   - Done when host and clients see the same phase/countdowns and no kill or
     sabotage request is accepted outside `Active`.

2. **[DONE - CODE / ONLINE VERIFY] Player life cycle and bodies**
   - Status: `[DONE - CODE]`; host/client kill, body, ghost, and parity-win
     scenarios remain in the manual regression matrix.
   - Extend the player network state with faction, alive/dead state, kill
     cooldown, death position, and movement/interact permissions.
   - Write server-side kill range (4m), line-of-sight, target-validity, and
     30-second cooldown validation; spawn one reportable body at the death point.
   - Add free-camera ghost mode, living-only interaction restrictions, and
     dead-player communication flags for future voice integration.
   - Done when a host/client kill, body replication, report prompt, ghost state,
     and killer-parity win all agree on every client.

3. **[DONE - CODE / ONLINE VERIFY] Meeting and ejection flow**
   - Status: `[DONE - CODE]`; report, emergency, tie, ejection, cleanup, and
     post-meeting respawn need host/client play verification.
   - Build the UI Toolkit 2D meeting screen: alive player cards, discussion,
     voting, abstain, tie/no-ejection, and role-hidden result overlay.
   - Make report/emergency calls server-authoritative; remove the reported body,
     lock gameplay input, and cancel/clean task screen state safely.
   - Respawn living players at the configured spawn system after the meeting;
     apply the 5-second global lock, then a fresh 30-second killer cooldown.
   - Done when report, emergency, tied vote, ejection, and meeting cleanup work
     with host plus at least one client.

4. **[DONE - CODE / ONLINE VERIFY] Repeatable task orchestration**
   - Status: `[DONE - CODE]`; task ownership, reassignment, repeatable board
     revisions, co-op eligibility, and crew progress need online verification.
   - Create server-owned `TaskDefinition`, `TaskRun`, task reservation, task
     cooldown, participant-role, and shared-session-lock data structures.
   - Implement personal-task assignment, pause/resume, death/disconnect/meeting
     cleanup, and fixed crew-progress target calculation.
   - Keep a networked normal-task revision so closing and reopening Circuit or
     Wave preserves the current board, while a newly assigned run gets a fresh
     puzzle.
   - Connect the existing MissionComputer, WaveFrequencyTerminal,
     CircuitMission, PressureTerminal, and ReactorTerminal interactions to report
     validated TaskRun completion rather than directly awarding match progress.
   - Done when task ownership, UI prompts, repeat assignment, co-op eligibility,
     and crew progress replicate correctly without altering existing minigame
     mechanics.

5. **[DONE - CODE / ONLINE VERIFY] Cooperative-task safety**
   - Status: `[DONE - CODE]`; the 3-player pressure/reactor and shared-valve
     race cases remain to be tested with multiple clients.
   - Implement the three-player PressureTerminal and Reactor task selection
     rules, private role assignment, participant replacement, and saved progress.
   - Add the shared valve-session lock for normal two-valve pressure work and
     Valve Override. Ensure only the server can acquire or release it.
   - Done when fewer than three living villagers cannot start either cooperative
     run, and simultaneous requests cannot make a valve usable by both systems.

6. **[DONE - CODE / ONLINE VERIFY] Upgrade economy and passives**
   - Status: `[DONE - CODE]`; threshold timing, selection loss, passive effects,
     and Threat Sensor behavior need online/manual verification.
   - Add per-player server-owned contribution points, 2/4 point thresholds,
     selection state, and close-without-reward behavior on death/ejection/meeting.
   - Build the UI Toolkit card selection screen and passive/tool HUD.
   - Implement and test the three villager and three killer passive effects,
     including Threat Sensor's non-directional `WARNING // NEARBY UNIT OFFLINE`
     event.
   - Done when each player can receive only one passive and one tool choice, and
     all effects are validated on the server where applicable.

7. **[DONE - CODE / ONLINE VERIFY] Killer active tools**
   - Status: `[DONE - CODE]`; Valve Override, Blackout, Identity Scramble,
     automatic defenses, and terminal hack windows need online/manual coverage.
   - Implement `VALVE OVERRIDE`: eligibility after a completed normal pressure
     task, pending reservation while the card is shown, 30-second emergency, and
     separate Killer Sabotage Point on failure.
   - Implement `SYSTEM BLACKOUT`: server-time 15-second lock, forced safe task
     UI close, `SYSTEM OFFLINE` prompt/overlay, and automatic restoration.
   - Implement `IDENTITY SCRAMBLE`: server-time 30-second common robot color,
     effective-color rendering, neutral alert, and exact color restoration.
   - Implement terminal-owned hack states: normal completion trigger,
     15-second preparation, killer-only `TERMINALI HACKLE` interaction, F1
     production permission check, separate rogue TaskRun, and +1 Killer
     Sabotage Point on successful rogue completion.
   - Implement the automatic villager defenses: Priority Uplink's scoped
     blackout bypass and Identity Anchor's protected effective-color rendering.
   - Keep all three killer tools immediate-use and single-use in the demo; keep
     both villager tools one-use and automatically triggered by their matching
     killer tool.
   - Done when tools cannot conflict with meetings, boot/post-meeting locks, or
     shared valve state, and every client sees the same result.

8. **[DONE - ONLINE VERIFY] Production access and regression pass**
   - Status: code-side access gates, RPC validation, phase gates, repeatable task
     fixes, post-start late-join rejection, production build validation, and the
     real host/client online gameplay regression are complete.
     The latest pass also added server-side sender, alive-state, and distance
     validation for emergency meetings, body reports, and meeting targets.
   - Retain F1 task switching only for Editor/Development testing. Production
     rogue-task access must be role and server-permission based.
   - Validate mission mutation RPCs on the server; direct client calls cannot
     complete normal tasks, alter sabotage state, or operate pressure/fuel
     systems without a living player, valid phase, and matching task role.
   - Gate all physical task interactions and prompts behind BootProtection or
     Active; meeting, post-meeting, lobby, and ended phases must not accept
     task, fuel, valve, camera, or body-report input.
   - Verify existing task UIs, interaction prompts, player freeze/unfreeze, and
     normal task completion behavior after every integration.
   - Run host/client and late-join coverage for task state, bodies, meetings,
     upgrades, Valve Override, System Blackout, Identity Scramble, Priority
     Uplink, and Identity Anchor. Cover automatic-defense consumption, no
     consumption for dead/ineligible owners, Uplink's protected open UI, anchor
     color exclusion, body visuals, meeting cleanup, exact restoration, and the
     normal-task-to-hack preparation window on each sabotage-capable terminal.
   - Verification pass: the active scene validates cleanly, Quick Test starts
     successfully in the Editor, and a Development-disabled Windows build was
     produced successfully from the existing `sci-fi-map` scene. The stale
     `MainMenu.unity` and `test_map.unity` Build Settings entries are disabled
     because those scene files are no longer present; the menu and gameplay
     flow are contained in `sci-fi-map`.
   - Online regression result (2026-08-13): four real Unity Editor processes
     connected through UGS Lobby + Relay (`0,4,5,6`). Host and clients matched on
     `BootProtection`/`Active`, role distribution, six-task crew target, and
     replicated task runs. The same-scene `StartGame` path was fixed so the
     server starts the match after the minimum player count is present, and a
     fourth client was rejected after a match had already started.
   - The completed four-player matrix covered: host kill, replicated reportable
     body, client-local ghost state, body report, meeting cleanup, tied vote,
     post-meeting lock, ejection/parity win, and lobby reset. The missing body
     prefab reference was repaired and registered in `DefaultNetworkPrefabs`.
   - The cooperative matrix covered PressureTerminal role slots `0/1/2`, killer
     activation rejection, valid operator activation, role-specific valve input,
     real cooperative completion, and crew-progress award. Reactor assignments
     were also observed with three living villagers.
   - The upgrade/tool matrix covered 2-point passive and 4-point tool thresholds,
     Priority Uplink bypass consumption, 15-second System Blackout and expiry,
     Identity Scramble with Identity Anchor color exclusion and expiry, and
     three-client Valve Override completion (`turned=3`, session returned to
     `Idle`). No gameplay or compilation errors were recorded during the final
     pass; the console only showed a transient MCP wire disconnect while the
     temporary Editor clones were being stopped.
   - General multiplayer QA pass (2026-08-13): removed the stale scene-level
     `FirstPersonController` that duplicated the network player prefab. A clean
     four-player session now reports four player objects and exactly one active
     `AudioListener`; all four instances reached `Active` with the same crew
     progress. Added a visible crew-progress fill bar and verified the real
     `0/9 -> 1/9` update after a completed PressureTerminal cooperative task.
   - Added a server snapshot guard so `MatchFlowManager` cannot resolve a false
     Villager win before all connected players have both a role and a spawned
     player object. The refreshed session remained `Active` with `Winner=None`
     after startup. Final Windows build after QA changes succeeded with zero
     build errors, and the built player stayed alive during an 8-second startup
     smoke test.

9. **[IN PROGRESS] Deferred systems and balance**
   - Status: the server-side sabotage-point cap and reward separation are done;
     the first balance-foundation pass is now complete; the broader loop,
     voice integration, and social-balance pass remain pending.
   - Balance foundation `[DONE - CODE]`: added the Resources-backed
     `DemoBalanceConfig` asset as the single source for match phase durations,
     meeting timing, kill/task cooldowns, terminal hack windows, killer-tool
     durations, Threat Sensor range, crew-task scaling, and the bounded
     sabotage-point cap. Runtime defaults preserve the existing demo values
     when the asset is unavailable, so Quick Test and development scenes do
     not depend on an editor-only asset reference.
   - Add proximity voice chat only after the above gameplay loop is stable and a
     supported voice provider/package is selected for the project.
   - Design the broader limited sabotage loop before implementing its spending and
     activation rules. The current demo only stores the bounded resource.
   - Playtest 4, 5, 6, 7, and 8 player matches; tune task target, kill cooldown,
     task cooldown, passive values, blackout duration, and meeting cooldown from
     observed results.

## Explicit Non-Goals for the First Demo Slice `[NON-GOAL]`

- Multiple killers.
- Killer conversion/hacking of villagers.
- Host migration.
- Reconnect persistence.
- Body dragging/hiding.
- Exact role-revealing evidence.
- Full navigation/pathfinding arrows.
- Timed round victory.
- Direct sabotage-based instant victory.
- More than two upgrade choices per player.
