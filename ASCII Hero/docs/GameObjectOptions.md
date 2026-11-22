# Game Object Options

Overview of every object `Kind` that can be placed through a world's `{World}_objects.ini`, with its
variants and all keys each one accepts. For the full file-format rules see [AssetFormat.md](AssetFormat.md).

Conventions used below:

- `Key = value` shows the default after the `;` when one exists. `(required)` means loading fails without it.
- `(optional)` means the key has no default; omitting it turns the feature off.
- Bool values are `true`/`false`. Numbers are in world cells (speeds in cells/second).
- Directions are `Left`, `Right`, `Up` or `Down` (Y grows downward).
- Each section is mapped from a placement code in `[ObjectCodes]`.

```ini
[ObjectCodes]
P = Player
```

---

## 1. Common keys (every Kind except where noted)

### 1.1 Appearance source (one of two styles is required)

Sprite-based: uses an authored asset.

```ini
[Example]
Kind = StaticObject          ; (required) see sections below
Asset = MyAsset              ; (required unless Material + Width + Height)
Clip = default               ; default: "default"
Span = 8                     ; (optional) tiles a tileable asset to this many cells
```

Materials-only: a solid block with no sprite files.

```ini
[Example]
Kind = StaticObject
Material = Stone             ; (required) a material from MaterialLibrary.ini
Width = 4                    ; (required) whole cells, 1 or more
Height = 2                   ; (required) whole cells, 1 or more
Character = #                ; (optional) overrides the material's glyph; required if material has none
```

### 1.2 Appearance overrides (all Kinds)

```ini
ForegroundColor = G          ; (optional) palette color code
BackgroundColor = B          ; (optional) color code, None (no fill) or WorldBackdrop
```

### 1.3 Material and physics overrides (all Kinds)

```ini
Material = Stone             ; (optional) replaces the asset's default material
Density = 1.0                ; (optional) default: from the material
Friction = 0.5               ; (optional) default: from the material
Restitution = 0.2            ; (optional) default: from the material (bounciness)
Hardness = 2                 ; (optional) must be above 0; default: from the material
```

### 1.4 Interaction flags (all Kinds except Player)

```ini
default: true for StaticHazard, KinematicHazard, TimedHazard, Collectable,
Climbable = false            ; default: false
ClimbStyle = Climb           ; (optional) Climb or Shimmy; only valid with Climbable = true
Hangable = false             ; default: false
Medium = false               ; default: false (true = water-like volume that gives buoyancy/drag; needs a static, Passable = true object)
Sound = SoundName            ; (optional) sound played on contact; not allowed on Portal
```

### 1.5 Conveyor surface (StaticObject, Gate, Switch, Device, FlowZone, Portal, Collectable, StaticHazard, KinematicObject, KinematicHazard)

Not allowed on free-moving bodies (DynamicObject, ThrowableObject, DynamicHazard).

```ini
ConveyorDirection = Left     ; (optional) Left/Right/Up/Down; if only ConveyorSpeed is set, taken from the clip-name suffix (_left, _right, _up, _down)
ConveyorSpeed = 3            ; default: 3 (when a direction is given)
```

### 1.7 Camera

```ini
CameraTarget = false         ; default: false; true makes the camera follow this body (physics bodies only)
```

### 1.6 Effect clips (sprite-based sections only)

Names of clips in the asset that play as cosmetic effects.

```ini
EffectClip = pop                     ; (optional) the object's own effect (kill, pickup, throwable hit...)
HurtEffectClip = hurt                ; (optional)
CheckpointReachedEffectClip = ...    ; (optional) Player only
WorldEndReachedEffectClip = ...      ; (optional) Player only
DiedEffectClip = ...                 ; (optional) Player only
HealedEffectClip = ...               ; (optional) Player only
PortalEnteredEffectClip = ...        ; (optional) Player only
PortalArrivedEffectClip = ...        ; (optional) Player only
```

---

## 2. Player

### 2.1 The player

One unnamed placement per world.

```ini
[Player]
Kind = Player
Asset = Player
Clip = walk_idle
RecoverySeconds = 1.0        ; default: 1.0 (invulnerable time after being hurt)
MotorForceMultiplier = 22    ; default: 22 (how strongly the player accelerates)
WalkSpeed = 12               ; default: 12
CrawlSpeed = 6               ; default: 6
FallDamage = 0.2             ; default: 0.2 (health lost on a hard landing)
FallDamageImpactSpeed = 28.3 ; default: about 28.3 (landing speed that causes fall damage; equals a 10-cell fall at default gravity)
StompDamage = 1.0            ; default: 1.0 (damage dealt by landing on a hazard)
PortalEnteredSound = Name    ; (optional)
PortalArrivedSound = Name    ; (optional)
CameraTarget = false         ; default: false
```

### 2.2 Named entry point

Arrival spot for portals; no player is created.

```ini
[EntryNorth]
Kind = Player
Asset = Player
Entry = North                ; (required for this variant) unique name within the world
```

---

## 3. StaticObject

Solid or decorative object that never moves (platforms, walls, scenery).

```ini
[Platform]
Kind = StaticObject
Asset = Platform
Clip = default
Span = 8                     ; (optional)
```

Takes only the common keys from section 1.

---

## 4. Two-state objects (Gate, Switch, Device, FlowZone, TimedHazard)

All have an off/on state. Extra keys shared by these:

```ini
ClipWhenOn = open            ; (optional) clip shown in the On state
InitialState = Off           ; default: Off (set On to start on)
OnSeconds = 2                ; (optional) switches itself off after this many seconds on; 0/absent = no timer
OffSeconds = 2               ; (optional) switches itself on after this many seconds off; default: OnSeconds
```

With `OnSeconds` set, the object alternates on and off by itself (`OnSeconds = 2` alone gives 2 s on, 2 s off); `InitialState` sets which phase it starts in. A timer can be combined with a `Link`, but a switch then only flips the state, and the timer carries on from there.

### 4.1 Gate, opened by a key

```ini
[LockedGate]
Kind = Gate
Asset = Gate
Clip = closed
ClipWhenOn = open
Lock = RedDoor               ; (optional) opened and closed by a Key collectable with the same lock name
Sound = DoorMove             ; (optional) played whenever the gate opens or closes
```

The matching key (`Kind = Collectable`, `Type = Key`, `Lock = RedDoor`) takes `Sound` (played on pickup) and `TurnSound` (played when the key is turned, before the gate's own `Sound`). The key is never used up.

### 4.2 Gate, opened by a switch

```ini
[SwitchGate]
Kind = Gate
Asset = Gate
Clip = closed
ClipWhenOn = open
Link = Bridge1               ; (optional) opened by a Switch with the same link name
```

A gate cannot have both `Lock` and `Link`.

### 4.3 Switch, momentary

```ini
[Lever]
Kind = Switch
Asset = Lever
Clip = off
ClipWhenOn = on
Link = Bridge1               ; (optional) the link name it controls
Latching = false             ; default: false (on only while pressed)
```

### 4.4 Switch, latching

```ini
[Lever]
Kind = Switch
Asset = Lever
Clip = off
ClipWhenOn = on
Link = Bridge1               ; (optional)
Latching = true              ; stays on after being pressed
```

### 4.5 Device, always on

A machine (e.g. a conveyor) that is not linked, so it always runs.

```ini
[Belt]
Kind = Device
Asset = Belt
Clip = default
ConveyorDirection = Right    ; (optional)
ConveyorSpeed = 3            ; default: 3
```

### 4.6 Device, switched

Runs only while its linked switch is on.

```ini
[Belt]
Kind = Device
Asset = Belt
Clip = off
ClipWhenOn = on
Link = Belt1                 ; (optional) link name of the switch controlling it
ConveyorDirection = Right    ; (optional)
ConveyorSpeed = 3            ; default: 3
```

### 4.7 FlowZone, fixed direction

An area (wind, water current) that pushes bodies inside it.

```ini
[Wind]
Kind = FlowZone
Asset = Wind
Clip = default
FlowDirection = Right        ; (required unless the clip name ends in _left/_right/_up/_down)
FlowSpeed = 12               ; default: 12
```

### 4.8 FlowZone, switched

Flows only while its linked switch is on.

```ini
[Fan]
Kind = FlowZone
Asset = Fan
Clip = off_up
ClipWhenOn = on_up
Link = Fan1                  ; (optional)
FlowDirection = Up           ; (optional) taken from the clip suffix if omitted
FlowSpeed = 12               ; default: 12
```

### 4.9 TimedHazard

A hazard (e.g. a retracting spike) that hurts only while on. Passable by default. Takes the hazard keys `Damage` and `Sound`; it is not killable and plays no effects.

```ini
[Spike]
Kind = TimedHazard
Asset = Spike
ClipWhenOn = up              ; clip shown while out and dangerous
OnSeconds = 2                ; 2 s out, then 2 s hidden, and so on
InitialState = Off           ; default: Off (starts hidden)
Damage = 0.5
Sound = Hurt
```

Leave `Clip` out and the hazard is invisible while off.

---

## 5. DynamicObject

Moves under physics (gravity, collisions). Can optionally patrol.

Keys shared by all DynamicObject-like Kinds (DynamicObject, ThrowableObject, DynamicHazard):

```ini
GravityAffected = true       ; default: true
InitialVelocityX = 0         ; default: 0
InitialVelocityY = 0         ; default: 0
```

### 5.1 Plain physics object

Pushable crate, ball or similar.

```ini
[Crate]
Kind = DynamicObject
Asset = Crate
Clip = default
GravityAffected = true
Restitution = 0.2            ; (optional)
```

### 5.2 Bouncing / floating object

Ignores gravity and bounces off things.

```ini
[BouncingBall]
Kind = DynamicObject
Asset = Ball
Clip = medium
GravityAffected = false
Restitution = 1.0
InitialVelocityX = 14
InitialVelocityY = 10
```

### 5.3 Patrolling, full world width

Patrols on X when `Patrol = true` and no bounds are given.

```ini
[Walker]
Kind = DynamicObject
Asset = Walker
Clip = walk_right
Patrol = true
```

### 5.4 Patrolling between two points (X)

```ini
[Walker]
Kind = DynamicObject
Asset = Walker
Clip = walk_right
PatrolMinX = 10              ; (optional) default: 0 (world left edge) when only PatrolMaxX is set
PatrolMaxX = 30              ; (optional) default: world right edge when only PatrolMinX is set
PatrolInitialDirectionX = Right ; (optional) Left or Right; default: inferred
```

### 5.5 Patrolling until the platform edge

Turns around at the end of the surface it stands on.

```ini
[Walker]
Kind = DynamicObject
Asset = Walker
Clip = walk_right
Patrol = true
PatrolStayOnPlatform = true  ; default: false
```

### 5.6 Patrolling vertically or on both axes (flying)

Use with `GravityAffected = false`. Vertical force defaults to 0, so give `PatrolMaxForceY` a value.

```ini
[Flyer]
Kind = DynamicObject
Asset = Flyer
Clip = fly
GravityAffected = false
PatrolMinY = 5               ; (optional) default: 0 when only PatrolMaxY is set
PatrolMaxY = 15              ; (optional) default: world bottom edge when only PatrolMinY is set
PatrolMinX = 10              ; (optional) adds X patrolling
PatrolMaxX = 30              ; (optional)
PatrolInitialDirectionY = Down ; (optional) Up or Down; default: inferred
PatrolMaxForceY = 0          ; default: 0 (set higher to allow vertical thrust)
```

### 5.7 Patrol tuning keys (all patrolling DynamicObject and DynamicHazard)

```ini
Patrol = true                ; (optional) true forces patrol; false disables it even if bounds are set
PatrolMinX = ...             ; (optional) see above
PatrolMaxX = ...             ; (optional)
PatrolMinY = ...             ; (optional)
PatrolMaxY = ...             ; (optional)
PatrolInitialDirectionX = Left ; (optional) Left or Right
PatrolInitialDirectionY = Up ; (optional) Up or Down
PatrolCruiseSpeedX = 6       ; default: 6
PatrolCruiseSpeedY = 6       ; default: 6
PatrolForceMultiplierX = 33  ; default: 33 (how fast it reaches cruise speed)
PatrolForceMultiplierY = 33  ; default: 33
PatrolMaxForceX = Infinity   ; default: unlimited (cap on horizontal force)
PatrolMaxForceY = 0          ; default: 0 (cap on vertical force)
PatrolStayOnPlatform = false ; default: false
PatrolBlockedTurnTime = 1.0  ; default: 1.0 (seconds blocked before turning around)
```

---

## 6. ThrowableObject

A loose object the player can pick up and throw.

```ini
[Rock]
Kind = ThrowableObject
Asset = Rock
Clip = default
Gear = Throwable             ; (required) gear slot name from Global/Settings.ini [Gear]
Damage = 0.5                 ; default: 0.5
HitSpeed = 5                 ; default: 5 (minimum impact speed at which it hurts a Killable hazard)
Reusable = true              ; default: true (false = used up on hit)
ThrowSpeed = 20              ; default: 20
ThrowAngle = 30              ; default: 30 (degrees)
PickupSound = Name           ; (optional)
EffectClip = hit             ; (optional)
GravityAffected = true       ; default: true
InitialVelocityX = 0         ; default: 0
InitialVelocityY = 0         ; default: 0
```

---

## 7. Hazards

Keys shared by DynamicHazard, KinematicHazard and StaticHazard:

```ini
Damage = 1.0                 ; default: 1.0 (negative values fall back to the default)
Health = 1.0                 ; default: 1.0 (must be above 0; stomps and thrown objects each remove some, at 0 the hazard dies)
RecoverySeconds = 0.5        ; default: 0.5 (time between hits)
Killable = false             ; default: false (true = it can be damaged by the player landing on it, or by thrown objects; false = it is only ever harmful)
KillImpactSpeed = 5          ; default: 5 (minimum downward landing speed for a stomp to count; only applies to stomps, gentler touches just hurt the player)
EffectClip = pop             ; (optional) played when killed
EffectPersists = false       ; default: false (true leaves the effect behind permanently, e.g. a dead husk)
HurtEffectClip = hurt        ; (optional)
HurtSound = Name             ; (optional)
KillSound = Name             ; (optional)
```

### 7.1 StaticHazard

Spikes, fire. Never moves; passable by default (hurts on contact).

```ini
[Spikes]
Kind = StaticHazard
Asset = Spikes
Clip = default
Span = 3                     ; (optional)
Damage = 1.0
```

### 7.2 DynamicHazard, standing still

Falls and bounces under physics but does not patrol.

```ini
[Boulder]
Kind = DynamicHazard
Asset = Boulder
Clip = default
GravityAffected = true
InitialVelocityX = 0
InitialVelocityY = 0
```

### 7.3 DynamicHazard, patrolling full width

```ini
[Creature]
Kind = DynamicHazard
Asset = Creature
Clip = walk_right
Patrol = true
```

### 7.4 DynamicHazard, patrolling between points

```ini
[Creature]
Kind = DynamicHazard
Asset = Creature
Clip = walk_right
PatrolMinX = 10
PatrolMaxX = 30
PatrolInitialDirectionX = Right ; (optional)
```

### 7.5 DynamicHazard, patrolling until an edge

```ini
[Creature]
Kind = DynamicHazard
Asset = Creature
Clip = walk_right
Patrol = true
PatrolStayOnPlatform = true  ; default: false
PatrolBlockedTurnTime = 1.0  ; default: 1.0
```

### 7.6 DynamicHazard, flying

```ini
[Bat]
Kind = DynamicHazard
Asset = Bat
Clip = fly
GravityAffected = false
PatrolMinY = 5
PatrolMaxY = 15
PatrolMaxForceY = 0          ; default: 0
```

All patrol keys from 5.7 apply to DynamicHazard.

### 7.7 KinematicHazard, constant drift or fixed in place

Not moved by physics. Without patrol keys it keeps moving at its initial velocity forever (0 = fixed in place, e.g. a static saw). On a patrolled axis the initial velocity is only a starting value that the patrol then overrides.

```ini
[Saw]
Kind = KinematicHazard
Asset = Saw
Clip = spin
InitialVelocityX = 0         ; default: 0 (constant speed in cells/second; non-zero makes it drift)
InitialVelocityY = 0         ; default: 0
```

### 7.8 KinematicHazard, patrolling

Follows a fixed path at a constant speed, ignoring collisions.

```ini
[Saw]
Kind = KinematicHazard
Asset = Saw
Clip = spin
PatrolMinX = 10              ; (optional) X path; default: 0 if only Max set
PatrolMaxX = 30              ; (optional) default: world right edge if only Min set
PatrolMinY = ...             ; (optional) adds a vertical path
PatrolMaxY = ...             ; (optional)
PatrolSpeedX = 6             ; default: 6
PatrolSpeedY = 6             ; default: 6
PatrolInitialDirectionX = Right ; (optional) Left or Right
PatrolInitialDirectionY = Down  ; (optional) Up or Down
```

Using `Patrol = true` with no bounds patrols the full world width.

---

## 8. KinematicObject

Moves along a fixed path, unaffected by gravity or collisions. Carries bodies standing on it.
Takes `Link` (optional) in addition to the common keys: when set, a Switch with the same link name starts/stops the movement, and the object starts stopped. Without `Link` it always runs.

### 8.1 Fixed or constant drift

Without patrol keys it keeps moving at its initial velocity (0 = fixed in place). On a patrolled axis the initial velocity is only the starting value; the patrol then steers the velocity (usually leave it out there). On an un-patrolled axis it stays as a constant drift.

```ini
[Platform]
Kind = KinematicObject
Asset = Platform
Clip = default
InitialVelocityX = 0         ; default: 0 (cells/second)
InitialVelocityY = 0         ; default: 0
```

### 8.2 Moving platform (horizontal)

```ini
[MovingPlatform]
Kind = KinematicObject
Asset = Platform
Clip = default
Link = Lift1                 ; (optional)
PatrolMinX = 10
PatrolMaxX = 30
PatrolSpeedX = 6             ; default: 6
PatrolInitialDirectionX = Right ; (optional)
```

### 8.3 Moving platform / climbable (vertical)

```ini
[Elevator]
Kind = KinematicObject
Asset = Elevator
Clip = default
PatrolMinY = 5
PatrolMaxY = 20
PatrolSpeedY = 6             ; default: 6
PatrolInitialDirectionY = Up ; (optional)
Climbable = true             ; (optional) e.g. a moving rope or ladder
```

### 8.4 Moving platform (both axes)

```ini
[Diagonal]
Kind = KinematicObject
Asset = Platform
Clip = default
PatrolMinX = 10
PatrolMaxX = 30
PatrolMinY = 5
PatrolMaxY = 20
PatrolSpeedX = 6             ; default: 6
PatrolSpeedY = 6             ; default: 6
```

### 8.5 Moving full world width

```ini
[Platform]
Kind = KinematicObject
Asset = Platform
Clip = default
Patrol = true
```

---

## 9. Portal

Leads to another world.

```ini
[Door]
Kind = Portal
Asset = Door
Clip = default
TargetWorld = 2              ; (required) world number, 0 or more
TargetEntry = North          ; (optional) named Player Entry in the target world; default: its default spawn
```

`Sound` is not allowed on a Portal (the player's `PortalEnteredSound` is used).

---

## 10. Collectable

Picked up on touch. `Type` decides the effect; an absent or unknown `Type` makes an inert collectable.

Shared keys:

```ini
Type = Diamond               ; (required for an effect) see variants
EffectClip = pop             ; (optional)
EffectPersists = false       ; default: false
```

### 10.1 Diamond

Adds 1 to the score.

```ini
[Diamond]
Kind = Collectable
Asset = Diamond
Clip = default
Type = Diamond
```

### 10.2 Ring

Adds a ring; every set number of rings grants an extra life.

```ini
[Ring]
Kind = Collectable
Asset = Ring
Clip = default
Type = Ring
```

### 10.3 Checkpoint

Sets the respawn point; the effect stays behind as a used marker.

```ini
[Checkpoint]
Kind = Collectable
Asset = Checkpoint
Clip = default
Type = Checkpoint
EffectClip = used
EffectPersists = true
```

### 10.4 WorldEnd

Ends the world.

```ini
[Goal]
Kind = Collectable
Asset = Goal
Clip = default
Type = WorldEnd
```

### 10.5 Key

Carried in a gear slot; opens a Gate with the same `Lock`.

```ini
[RedKey]
Kind = Collectable
Asset = Key
Clip = default
Type = Key
Lock = RedDoor               ; (optional) a key without a lock name does nothing
```

### 10.6 HealthPack

Restores full health; stays in place while health is already full.

```ini
[HealthPack]
Kind = Collectable
Asset = HealthPack
Clip = default
Type = HealthPack
```

---

## 11. Kind summary

| Kind | Moves | Hurts | Special keys |
| --- | --- | --- | --- |
| Player | Yes (input) | No | Entry, WalkSpeed, CrawlSpeed, FallDamage... |
| StaticObject | No | No | Span |
| Gate | No | No | Lock, Link, ClipWhenOn, InitialState |
| Switch | No | No | Link, Latching, ClipWhenOn, InitialState |
| Device | No | No | Link, Conveyor*, ClipWhenOn, InitialState |
| FlowZone | No | No | FlowDirection, FlowSpeed, Link |
| DynamicObject | Physics | No | Patrol*, GravityAffected, InitialVelocity* |
| ThrowableObject | Physics | Yes (thrown) | Gear, Damage, HitSpeed, ThrowSpeed |
| DynamicHazard | Physics | Yes | Patrol*, hazard keys |
| KinematicObject | Fixed path | No | Patrol* (speed based), Link |
| KinematicHazard | Fixed path | Yes | Patrol* (speed based), hazard keys |
| StaticHazard | No | Yes | hazard keys |
| Portal | No | No | TargetWorld, TargetEntry |
| Collectable | No | No | Type, Lock, EffectClip |
