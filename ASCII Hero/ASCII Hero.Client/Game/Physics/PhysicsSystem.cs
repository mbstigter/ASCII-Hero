using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Applies gravity to any <see cref="IGravityAffected"/> body, walk-force/patrol-force
/// contributions, and integrates position from velocity for every <see cref="IPhysicsBody"/> in
/// <see cref="World2D.Objects"/> each frame via a per-frame mass-scaled force accumulator (see
/// <see cref="StepMovingBodyWithForces"/>) - the player included (see <see cref="World.IWalkForceBody"/>).
/// Kinematic bodies move at a constant, predefined velocity and never receive gravity or input.
/// </summary>
public class PhysicsSystem
{
    private bool _wasUpKeyDown;
    private bool _wasDownKeyDown;
    private bool _wasJumpKeyDown;
    private bool _wasAirborneSinceStance;

    /// <summary>
    /// Set the instant the player jumps off a ladder (see the pose ladder in <see cref="Step"/>),
    /// and held until <see cref="IClimberBody.IsTouchingClimbable"/> goes false again. Without
    /// this, <see cref="GameDefaults.ClimbJumpSpeed"/> is slow enough that the player is still both overlapping
    /// the same ladder and holding Up/Down on the very next frame or two, which would otherwise
    /// immediately re-engage <see cref="IClimberBody.IsClimbing"/> before the jump is even visible
    /// - mirroring <see cref="IHangerBody.SuppressHangUntilClear"/> for the same underlying reason.
    /// </summary>
    private bool _suppressClimbUntilClear;

    /// <summary>
    /// Clears all player-control state carried between frames, so a newly loaded world starts
    /// fresh. Key-edge history is seeded from the keys held right now rather than cleared: a key
    /// still held from the previous world is not a fresh press, so it cannot trigger a jump or
    /// stance change on the first frame of the new world.
    /// </summary>
    public void Reset(InputState input)
    {
        _wasUpKeyDown = input.IsUpKeyDown;
        _wasDownKeyDown = input.IsDownKeyDown;
        _wasJumpKeyDown = input.IsJumpKeyDown;
        _wasAirborneSinceStance = false;
        _suppressClimbUntilClear = false;
    }

    /// <summary>Whether the last-seen key state (the edge-detection baseline) has Up, Down and Jump held.</summary>
    internal (bool Up, bool Down, bool Jump) KeyEdgeBaseline => (_wasUpKeyDown, _wasDownKeyDown, _wasJumpKeyDown);

    public void Step(World2D world, InputState input, double deltaSeconds)
    {
        var player = world.Player;
        var velocity = player.Velocity;

        var wasHanging = UpdateLocomotionModes(player, input);
        var stoodUpThisFrame = ApplyStanceInput(player, input, wasHanging, ref velocity);
        var (gripped, targetVelocityX, targetVelocityY) = ResolveMotorTargets(player, input, velocity);

        // Commit any climb/hang jump-off impulse applied above before computing the motor: the
        // motor's gap is measured against player.Velocity, so leaving the impulse only in the
        // local copy would make the motor apply the same velocity change a second time.
        player.Velocity = velocity;
        UpdateWalkForce(world, player, gripped, targetVelocityX, targetVelocityY, deltaSeconds);

        ApplyGroundJump(player, input, stoodUpThisFrame, ref velocity);
        player.Velocity = velocity;

        IntegrateBodies(world, deltaSeconds);
    }

    /// <summary>
    /// Engages/disengages climbing, hanging and swimming from last frame's contacts and the held
    /// keys. Returns whether the player was already hanging before this frame's update.
    /// </summary>
    private bool UpdateLocomotionModes(Player2D player, InputState input)
    {
        // Climbing/hanging: CollisionSystem set IsTouchingClimbable/IsTouchingHangable last frame
        // from the player's current overlap (and snap-speed check) against Body2D.IsClimbable/
        // IsHangable terrain, but that alone doesn't engage either state - merely brushing
        // past/through a passable ladder must never lock movement. Climbing requires a deliberate
        // up/down press while touching one - including mid-jump, there is no grounded requirement, so a ladder can
        // be grabbed out of the air. Crawling can't climb directly (too low a pose to reach a
        // rung) - engaging climb requires Stance == "Walk" already, so a crawling player must
        // first explicitly stand up (the ordinary Crawl->Walk pose toggle below, its own
        // separate key press) before a later press can grab the ladder; there is no combined
        // "stand up and grab on" shortcut. Hanging engages automatically as soon as the surface is
        // touched from underneath. Both disengage the moment the
        // player is no longer touching the corresponding surface; climbing additionally yields to
        // solid ground (landing on a real floor always takes priority over still nominally
        // overlapping a passable ladder, and a Jump press while climbing lets go and launches -
        // see the climbing movement block below), and hanging additionally yields to an explicit
        // "let go" or a Jump press (see the hang pose ladder below).
        if (player.IsClimbing && (!player.IsTouchingClimbable || player.IsGrounded))
        {
            player.IsClimbing = false;
        }
        else if (!player.IsClimbing && player.IsTouchingClimbable && player.Stance == "Walk" && !_suppressClimbUntilClear && (input.IsUpKeyDown || input.IsDownKeyDown))
        {
            player.IsClimbing = true;
        }

        // The debounce lock is released either once the player is no longer touching the
        // climbable surface at all (having actually jumped clear of it after the jump-off below -
        // mirroring the equivalent hang debounce below), or once they land on solid ground -
        // landing is already an unconditional "reset" moment for climbing (see the disengage
        // check above), and a jump arc that lands back on/through the same ladder rect without
        // ever fully clearing its overlap (a short hop rather than a big leap) would otherwise
        // leave the debounce stuck forever, since IsTouchingClimbable never actually goes false.
        if (!player.IsTouchingClimbable || player.IsGrounded)
        {
            _suppressClimbUntilClear = false;
        }

        var wasHanging = player.IsHanging;
        if (player.IsHanging && !player.IsTouchingHangable)
        {
            player.IsHanging = false;
        }
        else if (!player.IsHanging && !player.IsClimbing && player.IsTouchingHangable && !player.SuppressHangUntilClear)
        {
            player.IsHanging = true;
            // No stance change here: IsClambering is derived from the existing Stance, so a
            // player who grabs on while crawling hangs in the compact clamber pose.
        }

        // Once the player is no longer touching the hangable surface at all (having actually
        // fallen clear of it after an explicit "let go" below), the debounce lock is released so
        // a later approach can grab on again normally.
        if (!player.IsTouchingHangable)
        {
            player.SuppressHangUntilClear = false;
        }

        // Swim: medium-based, not reach-based (see ISwimmerBody's own doc comment) - there is no
        // "touching" flag to consult. Note that player.CurrentMedium is only updated later in
        // this same Step (in StepMovingBodyWithForces, via the world.Objects loop at the end), so
        // everything in this player block - swim engagement here, and the medium force scaling
        // of jumps and walk force below - sees the medium resolved during the previous step.
        // Mirrors climbing's deliberate-press engagement (not
        // hanging's automatic grab): a directional key must actually be held while submerged.
        // Grounded/climbing/hanging all take priority and preempt swim entirely - a solid floor
        // stays solid underwater, and a ladder/pipe's own capability check is independent of
        // medium, so this only ever engages once none of those apply.
        if (player.IsSwimming && (!IsSwimmableMedium(player.CurrentMedium) || player.IsGrounded || player.IsClimbing || player.IsHanging))
        {
            player.IsSwimming = false;
        }
        else if (!player.IsSwimming && !player.IsGrounded && !player.IsClimbing && !player.IsHanging
            && IsSwimmableMedium(player.CurrentMedium)
            && (input.IsLeftKeyDown || input.IsRightKeyDown || input.IsUpKeyDown || input.IsDownKeyDown))
        {
            player.IsSwimming = true;
        }

        return wasHanging;
    }

    /// <summary>
    /// Applies edge-triggered Up/Down/Jump input to the stance and climb/hang state (including
    /// climb/hang jump-off impulses written into <paramref name="velocity"/>), and records this
    /// frame's key state for next frame's edge detection. Returns whether the player stood up
    /// from a crawl this frame.
    /// </summary>
    private bool ApplyStanceInput(Player2D player, InputState input, bool wasHanging, ref Vector2D velocity)
    {
        // A single, structured up/down pose ladder, deliberately mirroring floor and hanging
        // as inverses of each other rather than two unrelated sets of key handling:
        //   Floor:    Up -> Walk (stand up from Crawl); Down -> Crawl (crouch down from Walk)
        //   Hanging:  Up -> Clamber (pull knees up from Hang); Down -> Hang (from Clamber);
        //             Down again (already Hang) -> let go entirely; Jump while Hang -> swing/jump
        //             off entirely instead
        // On the ground, Up always means "become more upright" (Crawl -> Walk) and Down always
        // means "become more compact" (Walk -> Crawl) - the ordinary pose toggle. Suspended from
        // a pipe/rope, the sense of "up"/"down" is deliberately inverted to match the player's arm
        // position rather than screen direction: Up pulls the knees up into the compact
        // clamber pose (mirroring Crawl), Down extends back out into the normal fully-stretched
        // hang (mirroring Walk) - and, since fully stretched is already the "least attached" pose,
        // a second Down from there means letting go entirely and dropping. Up and Jump are
        // deliberately distinct inputs everywhere (see InputState.IsJumpKeyDown) - Up is always a
        // directional/posture action, Jump is always a distinct explicit action - because Hang
        // genuinely needs both at the same time (Up pulls into Clamber, Jump swings off), and
        // keeping the same rule on the ground and while climbing avoids a special case. All three
        // latches are edge-triggered (only the frame the key is first pressed) so holding a
        // direction doesn't repeatedly cycle through every step in one press.
        // Landing always returns to the upright stance: crawling off a ledge, or dropping from a
        // clamber, shouldn't leave the player crawling on touchdown. Deliberate crawling on flat
        // ground never leaves the ground, so it is unaffected.
        if (!player.IsGrounded && !player.IsClimbing && !player.IsHanging)
        {
            _wasAirborneSinceStance = true;
        }
        else if (player.IsGrounded && _wasAirborneSinceStance)
        {
            _wasAirborneSinceStance = false;
            player.Stance = "Walk";
        }

        var upKeyDown = input.IsUpKeyDown;
        var downKeyDown = input.IsDownKeyDown;
        var jumpKeyDown = input.IsJumpKeyDown;
        var upPressedThisFrame = upKeyDown && !_wasUpKeyDown;
        var downPressedThisFrame = downKeyDown && !_wasDownKeyDown;
        var jumpPressedThisFrame = jumpKeyDown && !_wasJumpKeyDown;
        var stoodUpThisFrame = false;
        if (!player.IsClimbing && !player.IsHanging)
        {
            if (player.Stance == "Walk" && downPressedThisFrame)
            {
                player.Stance = "Crawl";
            }
            else if (player.Stance == "Crawl" && upPressedThisFrame)
            {
                player.Stance = "Walk";
                stoodUpThisFrame = true;
            }
        }
        else if (player.IsClimbing)
        {
            if (jumpPressedThisFrame)
            {
                // Lets go of the ladder entirely and launches upward (mirroring the Hang jump-off
                // below) - weaker than a standing jump since a ladder grip has less legs-planted
                // momentum behind it than solid ground. Same debounce concern as the hang jump-off
                // below: ClimbJumpSpeed alone isn't fast enough to clear the ladder's overlap
                // (and Up/Down are still likely held) within a single frame, so without
                // _suppressClimbUntilClear the very next frame would immediately re-grab the same
                // ladder before the jump is ever visible. Relative to the gripped surface, like the
                // ground jump is relative to its support.
                var grip = GetGrippedBody(player, ContactType.Climbable);
                player.IsClimbing = false;
                velocity.Y = (grip?.GetCarryVelocity().Y ?? 0.0) - GameDefaults.ClimbJumpSpeed;
                ApplyJumpReaction(player, grip, GameDefaults.ClimbJumpSpeed);
                _suppressClimbUntilClear = true;
            }
        }
        else if (player.IsHanging && wasHanging)
        {
            if (player.IsClambering && downPressedThisFrame)
            {
                player.Stance = "Walk";
            }
            else if (!player.IsClambering && upPressedThisFrame)
            {
                player.Stance = "Crawl";
            }
            else if (!player.IsClambering && jumpPressedThisFrame)
            {
                // Only the fully-stretched Hang (not the compact Clamber grip) can jump/swing
                // off - representing letting go of a pipe/rope while swinging from it, which only
                // makes sense from the stretched-out pose. Jump alone swings straight upward (e.g.
                // onto a pipe a little higher); combined with Left/Right it's a diagonal swing,
                // reusing whichever horizontal speed the player's current pose already grants
                // (see the ordinary horizontal-movement block below) rather than a separate one.
                // Same debounce as the explicit let-go below, so the player can't instantly
                // re-grab the exact surface they just launched off.
                var grip = GetGrippedBody(player, ContactType.Hangable);
                player.IsHanging = false;
                velocity.Y = (grip?.GetCarryVelocity().Y ?? 0.0) - GameDefaults.HangJumpSpeed;
                ApplyJumpReaction(player, grip, GameDefaults.HangJumpSpeed);
                player.SuppressHangUntilClear = true;
            }
            else if (!player.IsClambering && downPressedThisFrame)
            {
                // Already in the fully-stretched hang - a further Down lets go entirely, and the
                // debounce lock above prevents an instant re-grab while still overlapping the
                // same surface on the way down.
                player.IsHanging = false;
                player.SuppressHangUntilClear = true;
            }
        }
        _wasUpKeyDown = upKeyDown;
        _wasDownKeyDown = downKeyDown;
        _wasJumpKeyDown = jumpKeyDown;

        return stoodUpThisFrame;
    }

    /// <summary>
    /// Resolves the gripped surface (if any), the walk-motor's horizontal/vertical target
    /// velocities, and the player's raw move intent for facing.
    /// </summary>
    private static (Body2D? Gripped, double TargetVelocityX, double TargetVelocityY) ResolveMotorTargets(Player2D player, InputState input, Vector2D velocity)
    {
        var moveSpeed = player.Stance == "Walk" ? player.WalkSpeed : player.CrawlSpeed;

        // Horizontal movement is driven by a mass-scaled "motor" force converging the player's
        // velocity toward the target walk/crawl/climb/hang speed (see UpdateWalkForce and
        // IWalkForceBody), summed into the net force alongside gravity by
        // StepMovingBodyWithForces - consistent with every other body's force-based movement
        // (see DynamicObject2D.UpdatePatrolDirection) rather than a bespoke direct velocity-assignment path.
        // While climbing a ladder, horizontal input still applies (at a slower, deliberate side
        // speed) so the player can step off sideways onto an adjacent floor or ladder rather than
        // only ever being able to leave via a jump; while hanging from a pipe/bar, lateral
        // movement uses its own dedicated (and slower still while Clambering) speed rather than
        // reusing the ground Walk/Crawl speeds, since swinging/shimmying along a hangable surface
        // is its own distinct kind of locomotion. While swimming, lateral movement uses its own
        // dedicated horizontal swim speed for the same reason.
        var horizontalSpeed = player.IsClimbing ? GameDefaults.ClimbHorizontalSpeed
            : player.IsHanging ? (player.IsClambering ? GameDefaults.ClamberSpeed : GameDefaults.HangSpeed)
            : player.IsSwimming ? GameDefaults.SwimHorizontalSpeed
            : moveSpeed;
        // Target velocity is expressed relative to whatever solid the player is currently
        // grounded on (see GetGroundVelocityX) rather than an absolute world-frame speed: without
        // this, standing still on a moving platform would target world-frame velocity 0, and
        // WalkForce (scaled by WalkForceMultiplier, deliberately strong so input feels responsive)
        // would then fight CollisionSystem's friction-based drag pulling the player toward the
        // platform's own velocity every single frame - the two forces fighting is what made the
        // player appear unable to ride a horizontally moving platform at all. Climbing/hanging
        // are likewise relative to the gripped surface's carry velocity (a moving ladder/pipe, or
        // a conveyor/rope - see Body2D.GetCarryVelocity); swimming has no carry concept, so its
        // target speed stays absolute.
        var gripped = player.IsClimbing ? GetGrippedBody(player, ContactType.Climbable)
            : player.IsHanging ? GetGrippedBody(player, ContactType.Hangable)
            : null;
        var gripVelocity = gripped?.GetCarryVelocity() ?? default;
        var groundVelocityX = player.IsClimbing || player.IsHanging ? gripVelocity.X
            : player.IsSwimming ? 0.0
            : player.GetSurfaceVelocityX();
        var targetVelocityX = groundVelocityX;
        if (input.IsLeftKeyDown)
        {
            targetVelocityX -= horizontalSpeed;
        }
        if (input.IsRightKeyDown)
        {
            targetVelocityX += horizontalSpeed;
        }
        if (!player.IsGrounded && !player.IsClimbing && !player.IsHanging && !player.IsSwimming && !input.IsLeftKeyDown && !input.IsRightKeyDown)
        {
            // Airborne with no horizontal key held: target the player's own current velocity
            // rather than groundVelocityX (which is always 0 here - GetSurfaceVelocityX has
            // nothing to report once the grounded contact is gone), so UpdateWalkForce's
            // horizontal component collapses to exactly zero net force instead of still motoring
            // - at AirControlMultiplier strength - toward a standstill. Without this, releasing
            // the movement key mid-air (e.g. right after a diagonal jump's run-up) killed the
            // jump's horizontal momentum almost immediately regardless of how small
            // AirControlMultiplier was made, since even a weak motor still actively drags
            // velocity toward 0 given enough of the jump's short airborne time. Grounded
            // no-input deliberately keeps targeting groundVelocityX unchanged (decelerating to a
            // stop, or holding still relative to a moving platform) - that "stop on release"
            // ground feel is intentional; only the airborne case should behave as pure,
            // undriven momentum.
            targetVelocityX = player.Velocity.X;
        }
        // Raw move input intent for UpdatePose's facing (see Body2D.MoveIntentX) - deliberately
        // just Left/Right key state, with no platform-carry reference frame or velocity involved
        // at all, so facing reflects only what the player is actually trying to do this frame.
        player.MoveIntentX = input.IsLeftKeyDown ? -1.0 : input.IsRightKeyDown ? 1.0 : 0.0;
        // Raw up/down input intent for UpdatePose's climbing facing (see Body2D.MoveIntentY) -
        // the vertical counterpart to MoveIntentX above, for exactly the same reason: the climb
        // motor's velocity only ever asymptotically approaches zero, never reaching it exactly.
        player.MoveIntentY = input.IsUpKeyDown ? -1.0 : input.IsDownKeyDown ? 1.0 : 0.0;

        // Vertical target speed for the same mass-scaled motor force: straight up/down at a
        // fixed climb speed while IsClimbing, held at rest while IsHanging - both relative to the
        // gripped surface's carry velocity, with gravity still acting and carried by the grip
        // (see UpdateWalkForce) - and left equal to the player's own current Velocity.Y
        // everywhere else so the vertical component of WalkForce is simply zero and gravity/
        // collision response (falling, landing, jump arcs) are entirely unaffected by this motor.
        // Both climbing and hanging are sustained, ongoing locomotion for as long as they're
        // engaged - exactly like walking/crawling - so they use the same continuous
        // force-convergence model rather than a direct per-frame velocity assignment; only the
        // discrete jump-off/let-go moments below are true instantaneous impulses.
        double targetVelocityY;
        if (player.IsClimbing)
        {
            targetVelocityY = gripVelocity.Y;
            if (input.IsUpKeyDown)
            {
                targetVelocityY -= GameDefaults.ClimbVerticalSpeed;
            }
            if (input.IsDownKeyDown)
            {
                targetVelocityY += GameDefaults.ClimbVerticalSpeed;
            }
            // IsGrounded is derived from this frame's SurfaceBottom contact (see Body2D.IsGrounded);
            // clearing it immediately here (rather than waiting for the next collision pass) means
            // the player's pose/jump-gating reflects "climbing, not grounded" the same frame a
            // ladder is grabbed, not one frame late.
            player.RemoveContact(ContactType.SurfaceBottom);
        }
        else if (player.IsHanging)
        {
            targetVelocityY = gripVelocity.Y;
            player.RemoveContact(ContactType.SurfaceBottom);
        }
        else if (player.IsSwimming)
        {
            // Up/down while swimming is vertical thrust (depth control), not a distinct pose.
            // Gravity keeps acting while swimming, as it does while climbing/hanging (see
            // Player2D.GravityAffected) - but here buoyancy/drag (IMediumAffected, always-on)
            // offsets it rather than a grip, the same way it does for any other immersed body. With no Up/Down
            // held, the target is the player's own current velocity (motor contributes ~0 net
            // force), so gravity/buoyancy alone govern idle vertical drift - mirroring the
            // airborne "no horizontal key held" case above. With Up/Down held, the target is a
            // fixed absolute speed (0 +/- SwimVerticalSpeed) - like climbing's vertical target
            // above, but absolute since a fluid has no carry velocity - NOT relative to the current (already-moving) velocity, which would
            // recompute a moving goalpost every frame and never actually converge, snowballing
            // into runaway speed instead of the intended bounded SwimVerticalSpeed.
            targetVelocityY = velocity.Y;
            if (input.IsUpKeyDown)
            {
                targetVelocityY = -GameDefaults.SwimVerticalSpeed;
            }
            if (input.IsDownKeyDown)
            {
                targetVelocityY = GameDefaults.SwimVerticalSpeed;
            }
            player.RemoveContact(ContactType.SurfaceBottom);
        }
        else
        {
            // Falls through to here after a climb/hang jump-off above already applied its
            // instantaneous velocity.Y impulse this same frame (or for an ordinary airborne/
            // grounded frame with no state-machine transition at all) - targeting the velocity
            // that already exists is how a zero vertical WalkForce contribution is expressed,
            // leaving gravity/collision response entirely in charge of vertical motion outside
            // climbing/hanging.
            targetVelocityY = velocity.Y;
        }

        return (gripped, targetVelocityX, targetVelocityY);
    }

    /// <summary>Applies the standing jump impulse (and its reaction on a movable support) when allowed.</summary>
    private static void ApplyGroundJump(Player2D player, InputState input, bool stoodUpThisFrame, ref Vector2D velocity)
    {
        if (!player.IsClimbing && !player.IsHanging && input.IsJumpKeyDown && player.IsGrounded && player.Stance == "Walk" && !stoodUpThisFrame)
        {
            // A true impulse: an instantaneous Delta-v applied once per take-off, not a target the
            // player's velocity converges toward over subsequent frames. The condition tests the
            // held Jump state (not a fresh press), so holding Jump launches again on each landing;
            // the immediate IsGrounded clear below is what limits it to one impulse per take-off.
            // Same jump-off model as the ladder/hang variants above, just at the
            // strongest magnitude since it launches off solid ground with both legs planted.
            // The impulse is the same in every medium; a viscous medium (e.g. water) limits the
            // resulting height only through its passive quadratic drag in StepMovingBodyWithForces.
            // The kick is relative to the support's own vertical velocity (a jump off a rising
            // platform launches faster in world terms), and by Newton's third law the push-off
            // drives a finite-mass support downward with the equal and opposite momentum.
            var support = player.GetContactingBodies(ContactType.SurfaceBottom).FirstOrDefault();
            var supportVelocityY = support?.GetCarryVelocity().Y ?? 0.0;
            velocity.Y = supportVelocityY - GameDefaults.WalkJumpSpeed;
            if (support is { IsStatic: false } and IPhysicsBody movableSupport)
            {
                var playerMass = player.Mass > 0 ? player.Mass : 1.0;
                var supportMass = support.Mass > 0 ? support.Mass : 1.0;
                var supportVelocity = movableSupport.Velocity;
                supportVelocity.Y += GameDefaults.WalkJumpSpeed * playerMass / supportMass;
                support.Wake();
                movableSupport.Velocity = supportVelocity;
            }
            // Clears IsGrounded immediately so the jump's own frame already shows airborne (jump
            // pose, re-jump gated out) instead of waiting for the next collision pass to notice
            // the player has left the surface.
            player.RemoveContact(ContactType.SurfaceBottom);
        }
    }

    /// <summary>
    /// Moves every kinematic body and force-integrates every other awake physics body. A sleeping
    /// body is at rest with its forces balanced by its supports, so it is left untouched until
    /// something wakes it - see <see cref="SleepSystem"/>.
    /// </summary>
    private void IntegrateBodies(World2D world, double deltaSeconds)
    {
        foreach (var body in world.Objects)
        {
            if (body.IsSleeping)
            {
                continue;
            }

            switch (body)
            {
                case KinematicObject2D kinematicObject:
                    // Predefined motion (optionally per-axis patrol), no gravity/force
                    // integration - see KinematicObject2D.Move.
                    kinematicObject.Move(deltaSeconds);
                    break;

                case IGravityAffected gravityAffected:
                    StepMovingBodyWithForces(world, gravityAffected, gravityAffected.GravityAffected, deltaSeconds);
                    break;

                case IPhysicsBody physicsBody:
                    StepMovingBodyWithForces(world, physicsBody, gravityAffected: false, deltaSeconds);
                    break;
            }
        }
    }

    /// <summary>
    /// The climbable/hangable body the player is gripping (recorded as a grip contact of the
    /// given <paramref name="type"/> by <see cref="CollisionSystem"/>), or null if none.
    /// </summary>
    private static Body2D? GetGrippedBody(Player2D player, ContactType type) =>
        player.GetContactingBodies(type).FirstOrDefault();

    /// <summary>
    /// Equal and opposite reaction of a climb/hang jump-off on a finite-mass gripped body
    /// (mirroring the ground jump's push on its support). An immovable body simply absorbs it.
    /// </summary>
    private static void ApplyJumpReaction(Player2D player, Body2D? gripped, double jumpSpeed)
    {
        if (gripped is { IsStatic: false } and IPhysicsBody grippedBody)
        {
            var playerMass = player.Mass > 0 ? player.Mass : 1.0;
            var grippedMass = gripped.Mass > 0 ? gripped.Mass : 1.0;
            var grippedVelocity = grippedBody.Velocity;
            grippedVelocity.Y += jumpSpeed * playerMass / grippedMass;
            gripped.Wake();
            grippedBody.Velocity = grippedVelocity;
        }
    }

    /// <summary>
    /// Whether <paramref name="medium"/> is dense/viscous enough to swim in
    /// detection <see cref="World.ISwimmerBody"/> keys off (see <see cref="Step"/>'s swim
    /// engage/disengage logic), analogous to how <see cref="Body2D.IsClimbable"/>/
    /// <see cref="Body2D.IsHangable"/> key climbing/hanging off a specific surface instead. A
    /// medium qualifies if it clears either <see cref="PhysicsConstants.SwimMediumMinDensity"/>
    /// or <see cref="PhysicsConstants.SwimMediumMinViscosity"/> (not both) - see those constants'
    /// own doc comments for why an "either" rule was chosen.
    /// </summary>
    internal static bool IsSwimmableMedium(Assets.Material medium) =>
        medium.Density >= PhysicsConstants.SwimMediumMinDensity || medium.Viscosity >= PhysicsConstants.SwimMediumMinViscosity;

    /// <summary>
    /// Resolves the medium a body at <paramref name="body"/>'s current collision rectangles is
    /// immersed in this frame, together with how much of the body's own area overlaps that
    /// winning material - a value in <c>[0, 1]</c>, used to scale
    /// buoyancy/drag proportionally to how far the body is inside the medium (see <see cref="StepMovingBodyWithForces"/>)
    /// rather than applying full-body buoyancy the instant any single cell overlaps a medium
    /// volume at all. Applying full-body buoyancy on any overlap would give a body merely grazing
    /// a water surface the same upward force as one fully submerged, overshooting the equilibrium
    /// point every frame near the surface and producing a visible bounce (on a floating body, and
    /// as player pose flicker at the waterline). At fraction 1.0 (fully submerged) this is
    /// full-strength buoyancy/drag.
    ///
    /// The winning material is the densest overlapping volume (the first
    /// one placed on a tie), except that a placed volume of the same density as the Air fallback
    /// also displaces it, so an Air-material volume (e.g. a wind tunnel) can act as a medium.
    /// The overlap fraction is then computed only from candidates resolving to that same winning
    /// material: each such candidate's overlapped area against the body (via
    /// <see cref="Physics.Rect.OverlapArea"/>, over every rect pair - so a body's whole
    /// silhouette contributes, in both axes) is summed (same-material volumes are assumed not to
    /// overlap each other), then divided by the body's own total collision area.
    /// When the winning volume is a <see cref="World.FlowZone2D"/>, its current flow is returned
    /// too, scaled by that volume's own overlapped share of the body.
    /// When no placed medium volume overlaps at all, the body is resolved as fully immersed
    /// (fraction 1.0) in the default "Air" fallback rather than fraction 0.0 - Air is the ambient
    /// substance filling all otherwise-unoccupied space, not a bounded level-authored volume with
    /// edges a body can be only partially submerged past, so its own (low but nonzero) Viscosity
    /// drag keeps applying continuously everywhere outside a denser placed volume.
    /// </summary>
    private static (Assets.Material Medium, double OverlapFraction, Vector2D Flow) ResolveCurrentMedium(World2D world, IPhysicsBody body)
    {
        var resolved = world.Materials.Get("Air");
        Body2D? resolvedCandidate = null;
        foreach (var candidate in world.Objects)
        {
            if (!IsMediumVolume(candidate))
            {
                continue;
            }

            // Against the Air fallback a placed volume only has to match its density (so an
            // Air-material volume, e.g. a wind tunnel, can displace it); against an already
            // winning placed volume it must be strictly denser.
            var candidateMaterial = world.Materials.Get(candidate.MaterialName);
            if (resolvedCandidate is null ? candidateMaterial.Density < resolved.Density : candidateMaterial.Density <= resolved.Density)
            {
                continue;
            }

            if (Overlaps(body, candidate))
            {
                resolved = candidateMaterial;
                resolvedCandidate = candidate;
            }
        }

        if (resolvedCandidate is null)
        {
            // No placed medium volume overlaps at all, so resolved is still the "Air" fallback
            // assigned above - not a level-authored volume with edges a body can be partially
            // submerged past, but the ambient substance filling all otherwise-unoccupied space.
            // A body is therefore always fully immersed in it (fraction 1.0, not 0.0), so Air's
            // own Viscosity drag (see MaterialLibrary.ini) keeps applying continuously in open
            // air exactly as it always did, rather than silently vanishing outside any placed
            // medium volume's footprint. Still air has no flow.
            return (resolved, 1.0, new Vector2D(0, 0));
        }

        // Second pass: now that the winning material is known, gather every qualifying
        // candidate resolving to that same material (not just the single one that happened to
        // win the tie-break above) and add up the area each overlaps the body's rects, so the
        // overlap fraction reflects the body's true total overlapped extent in both axes.
        // Same-material volumes are assumed not to overlap each other (a level design rule), so
        // the sum is simply clamped to 1. The winning volume's own flow is scaled by its own
        // overlapped share of the body, so a body only partly inside a flowing zone is only
        // partly carried by it.
        var bodyArea = 0.0;
        foreach (var rect in body.CollisionRects)
        {
            bodyArea += rect.Width * rect.Height;
        }

        if (bodyArea <= 0)
        {
            return (resolved, 1.0, FlowOf(resolvedCandidate));
        }

        var overlappedArea = 0.0;
        var winnerOverlappedArea = 0.0;
        foreach (var candidate in world.Objects)
        {
            if (!IsMediumVolume(candidate))
            {
                continue;
            }

            var candidateMaterial = world.Materials.Get(candidate.MaterialName);
            if (!candidateMaterial.Equals(resolved) || !Overlaps(body, candidate))
            {
                continue;
            }

            foreach (var bodyRect in body.CollisionRects)
            {
                foreach (var candidateRect in candidate.CollisionRects)
                {
                    var area = bodyRect.OverlapArea(candidateRect);
                    overlappedArea += area;
                    if (candidate == resolvedCandidate)
                    {
                        winnerOverlappedArea += area;
                    }
                }
            }
        }

        var overlapFraction = Math.Clamp(overlappedArea / bodyArea, 0.0, 1.0);
        var flowFraction = Math.Clamp(winnerOverlappedArea / bodyArea, 0.0, 1.0);
        var flow = FlowOf(resolvedCandidate);
        return (resolved, overlapFraction, new Vector2D(flow.X * flowFraction, flow.Y * flowFraction));
    }

    /// <summary>The velocity of the medium filling <paramref name="volume"/>: its <see cref="World.FlowZone2D.Flow"/> if it is a flow zone, otherwise still (zero).</summary>
    private static Vector2D FlowOf(Body2D volume) =>
        volume is FlowZone2D flowZone ? flowZone.Flow : new Vector2D(0, 0);

    /// <summary>
    /// True if <paramref name="candidate"/> is a level-authored ambient-medium volume (e.g. a placed
    /// Water/BodyOfWater section): passable, static-for-collision plain terrain. Hazards and
    /// collectables act through contact, <see cref="World.EffectInstance2D"/> is purely cosmetic,
    /// and climbable/hangable terrain is structure to grip - none is a substance to be immersed
    /// in, and each would otherwise leak its own material into the medium resolution.
    /// </summary>
    private static bool IsMediumVolume(Body2D candidate) =>
        candidate.IsStatic
        && candidate.IsPassable
        && candidate.IsMediumCandidate
        && !candidate.IsClimbable
        && !candidate.IsHangable;

    /// <summary>Same overlap test
    private static bool Overlaps(IPhysicsBody a, Body2D b)
    {
        foreach (var rectA in a.CollisionRects)
        {
            foreach (var rectB in b.CollisionRects)
            {
                if (rectA.Overlaps(rectB))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Recomputes <see cref="Player2D.WalkForce"/> as a proportional "motor" force converging
    /// <paramref name="player"/>'s current velocity toward (<paramref name="targetVelocityX"/>,
    /// <paramref name="targetVelocityY"/>) - the walk/crawl/climb/hang speed the current input
    /// calls for - mirrors <see cref="DynamicObject2D.UpdatePatrolDirection"/>'s role for a
    /// patrolling hazard, but proportional to the remaining speed gap (scaled by
    /// <see cref="Player2D.WalkForceMultiplier"/>, reduced by <see cref="GameDefaults.AirControlMultiplier"/>
    /// while airborne) rather than a fixed-direction force, so the
    /// player still promptly reaches and then holds the target speed while grounded - the "no
    /// acceleration/friction" ground feel from the old direct-assignment model - instead of
    /// accelerating indefinitely or oscillating around it, while a jump instead carries over its
    /// launch horizontal speed and only gently steers thereafter. Applies to both axes uniformly: horizontally this
    /// drives walking/crawling/climbing's side-step/hanging's shimmy exactly as before, and
    /// vertically it now also drives climbing's up/down motion and holding position while
    /// hanging - both are sustained, ongoing locomotion for as long as they're engaged, just like
    /// walking, so they share the same continuous force model rather than a bespoke direct
    /// velocity assignment. Outside climbing/hanging, <paramref name="targetVelocityY"/> is passed
    /// in equal to the player's own current vertical velocity (see the call site in <see cref="Step"/>),
    /// which collapses the vertical component to exactly zero so gravity/collision response
    /// (falling, landing, jump arcs - all true impulses/forces of their own) are entirely
    /// unaffected by this motor. Mass-scaled like every other force here, so a body with no
    /// resolved material (<see cref="Body2D.Mass"/> of 0, treated as mass 1) still walks at the
    /// ordinary rate.
    /// </summary>
    private static void UpdateWalkForce(World2D world, Player2D player, Body2D? gripped, double targetVelocityX, double targetVelocityY, double deltaSeconds)
    {
        var mass = player.Mass > 0 ? player.Mass : 1.0;

        // Climbing/hanging: the force goes through the hands into the gripped ladder/pipe. Gravity
        // stays on, so the grip also carries the body's weight (feed-forward, so the hold doesn't
        // sag below the target). The whole grip force is limited by combined friction x weight
        // (see PhysicsConstants.GripTractionMultiplier) - beyond that the body slides along the
        // surface - and by Newton's third law pushes a finite-mass gripped body the other way.
        if (gripped is not null)
        {
            var gripForceX = (targetVelocityX - player.Velocity.X) * mass * player.WalkForceMultiplier;
            var gripForceY = (targetVelocityY - player.Velocity.Y) * mass * player.WalkForceMultiplier - mass * world.Gravity;
            var gripFriction = CollisionSystem.CombineFriction(player.Friction, gripped.Friction);
            var maxGrip = gripFriction * mass * Math.Abs(world.Gravity) * PhysicsConstants.GripTractionMultiplier;
            var gripMagnitude = Math.Sqrt(gripForceX * gripForceX + gripForceY * gripForceY);
            if (gripMagnitude > maxGrip)
            {
                var scale = gripMagnitude > 0 ? maxGrip / gripMagnitude : 0.0;
                gripForceX *= scale;
                gripForceY *= scale;
            }

            if (gripped is { IsStatic: false } and IPhysicsBody grippedBody)
            {
                var grippedMass = gripped.Mass > 0 ? gripped.Mass : 1.0;
                var grippedVelocity = grippedBody.Velocity;
                grippedVelocity.X -= gripForceX / grippedMass * deltaSeconds;
                grippedVelocity.Y -= gripForceY / grippedMass * deltaSeconds;
                gripped.Wake();
                grippedBody.Velocity = grippedVelocity;
            }

            player.WalkForce = new Vector2D(gripForceX, gripForceY);
            return;
        }
        // Horizontal strength is cut to AirControlMultiplier while airborne (see its own doc
        // comment) so a jump carries over whatever horizontal speed the player left the ground
        // with - including a moving platform's carried speed - rather than that speed being
        // snapped away toward the absolute walk-speed target within a single frame by the full
        // ground motor. Vertical strength is left untouched: it already targets the player's own
        // current Velocity.Y while airborne (see the call site in Step), so it contributes ~0
        // regardless and gravity/jump impulses remain entirely in charge of vertical motion.
        var horizontalMultiplier = player.IsGrounded ? player.WalkForceMultiplier : player.WalkForceMultiplier * GameDefaults.AirControlMultiplier;
        // Not scaled by the medium: a viscous medium resists motion only through the passive
        // drag in StepMovingBodyWithForces, which already grows with speed.
        var forceX = (targetVelocityX - player.Velocity.X) * mass * horizontalMultiplier;
        var forceY = (targetVelocityY - player.Velocity.Y) * mass * player.WalkForceMultiplier;

        // Walking/crawling pushes horizontally against the ground through friction: the force
        // can never exceed the combined friction coefficient times the body's weight (traction),
        // so a slippery floor limits acceleration and braking. By Newton's third law the same
        // force pushes the support the other way - a finite-mass support (e.g. a pushable block)
        // is shoved backward; an immovable one simply absorbs it.
        if (player.IsDrivingOnGround)
        {
            var support = player.GetContactingBodies(ContactType.SurfaceBottom).FirstOrDefault();
            if (support is not null)
            {
                var friction = CollisionSystem.CombineFriction(player.Friction, support.Friction);
                var maxTraction = friction * mass * Math.Abs(world.Gravity) * PhysicsConstants.GroundTractionMultiplier;
                forceX = Math.Clamp(forceX, -maxTraction, maxTraction);

                if (!support.IsStatic && support is IPhysicsBody supportBody)
                {
                    var supportMass = support.Mass > 0 ? support.Mass : 1.0;
                    var supportVelocity = supportBody.Velocity;
                    supportVelocity.X -= forceX / supportMass * deltaSeconds;
                    support.Wake();
                    supportBody.Velocity = supportVelocity;
                }
            }
        }

        player.WalkForce = new Vector2D(forceX, forceY);
    }

    /// <summary>
    /// Force-based integration step used for every non-player moving body (see the dispatch loop
    /// in <see cref="Step"/>): rather than adding a fixed velocity delta for gravity directly, this
    /// accumulates forces acting on the body this frame (gravity
    /// as a mass-scaled force - <c>F = mass * gravity</c>, matching how a real falling object's
    /// weight scales with its mass - plus, for an <see cref="IPatrolBody"/>, its own patrol force)
    /// into a net force, converts that to an acceleration via <c>a = F / mass</c>, and integrates
    /// that into velocity. For a single gravity-only force this reduces to
    /// <c>velocity.Y += gravity * dt</c> (mass cancels out of
    /// <c>F / mass = mass * gravity / mass = gravity</c>) - the accumulator's value is that any
    /// further force source (wind, thrust, a spring, etc.) can be summed in here alongside gravity
    /// before the single acceleration/integration step, rather
    /// than every force needing its own bespoke velocity-mutation code path. A body with no
    /// resolved material (<see cref="Body2D.Mass"/> of 0) is treated as mass 1 for this
    /// conversion, same rationale as <see cref="CollisionSystem"/>'s impulse math, so an
    /// unconfigured body still falls at the ordinary rate instead of the force/0 blowing up.
    ///
    /// There is deliberately no separate "normal force" term counteracting gravity here: rather
    /// than a full constraint solver that computes and applies an opposing normal force every
    /// frame a body rests on something, the existing grounded-contact response in
    /// <see cref="CollisionSystem"/> already achieves the same net effect pragmatically - each
    /// frame a resting body's downward velocity is zeroed/reduced by its resolved restitution
    /// right at the point of contact (see <c>CollisionSystem.ResolveAgainstSolid</c>/<c>CollisionSystem.ResolveAgainstOtherBody</c>),
    /// which is what actually stops gravity from accumulating unbounded downward velocity while
    /// grounded. <see cref="IPhysicsBody.IsGrounded"/> is that same contact signal surfaced for
    /// other systems (animation, jump gating) to read, not an input this method itself needs.
    /// </summary>
    private static void StepMovingBodyWithForces(World2D world, IPhysicsBody body, bool gravityAffected, double deltaSeconds)
    {
        var mass = body.Mass > 0 ? body.Mass : 1.0;

        var netForce = new Vector2D(0, 0);
        if (gravityAffected)
        {
            netForce.Y += mass * world.Gravity;
        }

        // Ambient medium (see docs/Decisions.md): resolved fresh every frame (mirroring
        // IsGrounded's "derived, not stored" philosophy, just needing an explicit recompute call
        // since it depends on spatial overlap rather than already-tracked contact state) and
        // exposed via Body2D.CurrentMedium for other systems (e.g. a future swim pose) to read.
        // body as Body2D is null for a non-Body2D IPhysicsBody (none currently exist, but the
        // cast stays defensive); such a body simply never receives any medium force below.
        var mediumBody = body as Body2D;
        var (medium, submergedFraction, flow) = mediumBody is not null
            ? ResolveCurrentMedium(world, body)
            : (Assets.MaterialLibrary.Undefined, 0.0, new Vector2D(0, 0));
        if (mediumBody is not null)
        {
            mediumBody.CurrentMedium = medium;
        }

        // Patrolling bodies (see IPatrolBody) contribute their own horizontal force here,
        // recomputed each frame from their current position relative to their patrol bounds;
        // any further force source (wind, thrust, etc.) would sum in here
        // the same way, alongside gravity, before the single acceleration/integration step below.
        if (body is IPatrolBody patrolBody)
        {
            patrolBody.UpdatePatrolDirection(world.Gravity);
            netForce += patrolBody.PatrolForce;
        }

        // The player's ground-level walk/crawl (and climb/hang side-step) locomotion contributes
        // its own horizontal motor force here - see IWalkForceBody and UpdateWalkForce, called
        // earlier this frame by Step before this integration runs.
        if (body is IWalkForceBody walkForceBody)
        {
            netForce += walkForceBody.WalkForce;
        }

        if (mediumBody is not null && body is IMediumAffected { MediumAffected: true })
        {
            // Buoyancy: an upward (gravity-opposing) force equal to the weight of medium
            // displaced by this body's own volume - Archimedes' principle. Volume is
            // recovered from mass/density (mass = density * volume), using this body's own
            // resolved density (falling back to 1.0 for an unconfigured body, same rationale
            // as the mass fallback above) to avoid a divide-by-zero for a massless/density-
            // less body.
            // Both buoyancy and drag below are scaled by submergedFraction (see
            // ResolveCurrentMedium's own doc comment) so a body only grazing a medium's
            // surface - rather than fully immersed in it - receives proportionally less of
            // each force, instead of the previous full-strength-on-any-overlap behavior that
            // caused surface-floating bodies to visibly bounce.
            var bodyDensity = mediumBody.Density > 0 ? mediumBody.Density : 1.0;
            var volume = mass / bodyDensity;
            netForce.Y -= medium.Density * volume * world.Gravity * submergedFraction;

            // Drag: a quadratic (velocity-squared) velocity-opposing force scaled by the
            // medium's Viscosity and this body's own frontal area facing the direction of
            // travel - the physically accurate model for fluid drag at ordinary (non-creeping)
            // speeds, unlike a linear model which only holds for very slow motion through a
            // thick medium. Scaling with the square of speed rather than speed itself means a
            // fast impact (e.g. a body falling into water) sheds far more of its momentum than
            // the same body drifting slowly - which is what keeps a body from significantly
            // overshooting/bouncing back out after a hard entry. Scaling with frontal area
            // (Size.Y facing horizontal travel, Size.X facing vertical travel - the 2D analog
            // of cross-sectional area) means a bigger body of the same material displaces and
            // pushes against more of the medium and so feels proportionally more drag than a
            // smaller one, not just more buoyancy from its larger volume. Applied per-axis
            // using each axis's own speed/area (not the combined velocity magnitude/a single
            // area) to keep the two axes independent, consistent with every other force here.
            // Continuous while immersed, independent of any solid contact, distinct from
            // Coulomb friction (which only acts at contact time). Measured against the medium's own
            // velocity (flow, zero in still media - see FlowZone2D), so a flowing medium drags a
            // body toward its own speed, and a body moving with the flow feels less drag than one
            // moving against it.
            var relativeVelocityX = body.Velocity.X - flow.X;
            var relativeVelocityY = body.Velocity.Y - flow.Y;
            netForce.X -= medium.Viscosity * body.Size.Y * relativeVelocityX * Math.Abs(relativeVelocityX) * submergedFraction;
            netForce.Y -= medium.Viscosity * body.Size.X * relativeVelocityY * Math.Abs(relativeVelocityY) * submergedFraction;
        }

        var acceleration = new Vector2D(netForce.X / mass, netForce.Y / mass);

        var velocity = body.Velocity;
        velocity.X += acceleration.X * deltaSeconds;
        velocity.Y += acceleration.Y * deltaSeconds;
        body.Velocity = velocity;

        body.Position = new Vector2D(
            body.Position.X + velocity.X * deltaSeconds,
            body.Position.Y + velocity.Y * deltaSeconds);
        if (body is Body2D frictionBody)
        {
            frictionBody.FrictionUndoBudgetX = velocity.X * deltaSeconds;
        }

        // Pose (see IPosedBody) is resolved last, after velocity/position are both finalized for
        // this frame, so a velocity-derived facing (e.g. DynamicHazard2D's) reflects this frame's
        // actual resolved motion rather than a pre-integration estimate.
        if (body is IPosedBody posedBody)
        {
            posedBody.UpdatePose();
        }
    }
}
