using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Input;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Applies horizontal movement input to the player, gravity to any <see cref="IGravityAffected"/>
/// body, and integrates position from velocity for every <see cref="IPhysicsBody"/> in
/// <see cref="World2D.Objects"/> each frame. Kinematic bodies move at a constant, predefined
/// velocity and never receive gravity or input. The player still moves via direct velocity
/// assignment (see the TODO in <see cref="Step"/>); every other moving body integrates via a
/// per-frame mass-scaled force accumulator instead (see <see cref="StepMovingBodyWithForces"/>).
/// </summary>
public class PhysicsSystem
{
    private const double WalkSpeed = 12.0;
    private const double CrawlSpeed = 6.0;
    private const double ClimbVerticalSpeed = 10.0;
    private const double ClimbHorizontalSpeed = 8.0;
    private const double HangSpeed = 8.0;
    private const double ClamberSpeed = 5.0;
    private const double WalkJumpSpeed = 18.0;
    private const double ClimbJumpSpeed = 15.0;
    private const double HangJumpSpeed = 12.0;

    private bool _wasUpKeyDown;
    private bool _wasDownKeyDown;
    private bool _wasJumpKeyDown;

    /// <summary>
    /// Set the instant the player jumps off a ladder (see the stance ladder in <see cref="Step"/>),
    /// and held until <see cref="IClimberBody.IsTouchingClimbable"/> goes false again. Without
    /// this, <see cref="ClimbJumpSpeed"/> is slow enough that the player is still both overlapping
    /// the same ladder and holding Up/Down on the very next frame or two, which would otherwise
    /// immediately re-engage <see cref="IClimberBody.IsClimbing"/> before the jump is even visible
    /// - mirroring <see cref="IHangerBody.SuppressHangUntilClear"/> for the same underlying reason.
    /// </summary>
    private bool _suppressClimbUntilClear;

    public void Step(World2D world, InputState input, double deltaSeconds)
    {
        var player = world.Player;
        var velocity = player.Velocity;

        // Climbing/hanging: CollisionSystem set IsTouchingClimbable/IsTouchingHangable last frame
        // from the player's current overlap (and snap-speed check) against Body2D.IsClimbable/
        // IsHangable terrain, but that alone doesn't engage either state - merely brushing
        // past/through a passable ladder must never lock movement. Climbing requires a deliberate
        // up/down press while touching one (mirroring the old ConsoleGame2D reference's explicit
        // Climb() trigger) - including mid-jump, there is no grounded requirement, so a ladder can
        // be grabbed out of the air. Crawling can't climb directly (too low a stance to reach a
        // rung) - engaging climb requires Stance == "Walk" already, so a crawling player must
        // first explicitly stand up (the ordinary Crawl->Walk stance toggle below, its own
        // separate key press) before a later press can grab the ladder; there is no combined
        // "stand up and grab on" shortcut. Hanging engages automatically as soon as the surface is
        // touched from underneath, same as that reference project. Both disengage the moment the
        // player is no longer touching the corresponding surface; climbing additionally yields to
        // solid ground (landing on a real floor always takes priority over still nominally
        // overlapping a passable ladder, and a Jump press while climbing lets go and launches -
        // see the climbing movement block below), and hanging additionally yields to an explicit
        // "let go" or a Jump press (see the hang stance ladder below).
        if (player.IsClimbing && (!player.IsTouchingClimbable || player.IsGrounded))
        {
            player.IsClimbing = false;
        }
        else if (!player.IsClimbing && player.IsTouchingClimbable && player.Stance == "Walk" && !_suppressClimbUntilClear && (input.IsUpPressed || input.IsDownPressed))
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
            // Reaching a pipe/rope while already crawling grabs on in the compact clamber
            // pose (hands and feet both on it) instead of the regular fully-stretched hang -
            // matching whichever pose the player's silhouette already was in the instant before
            // grabbing on, rather than always defaulting to one or the other.
            player.IsClambering = player.Stance == "Crawl";
        }

        // Once the player is no longer touching the hangable surface at all (having actually
        // fallen clear of it after an explicit "let go" below), the debounce lock is released so
        // a later approach can grab on again normally.
        if (!player.IsTouchingHangable)
        {
            player.SuppressHangUntilClear = false;
        }

        // A single, structured up/down stance ladder, deliberately mirroring floor and hanging
        // as inverses of each other rather than two unrelated sets of key handling:
        //   Floor:    Up -> Walk (stand up from Crawl); Down -> Crawl (crouch down from Walk)
        //   Hanging:  Up -> Clamber (pull knees up from Hang); Down -> Hang (from Clamber);
        //             Down again (already Hang) -> let go entirely; Jump while Hang -> swing/jump
        //             off entirely instead
        // On the ground, Up always means "become more upright" (Crawl -> Walk) and Down always
        // means "become more compact" (Walk -> Crawl) - the ordinary stance toggle. Suspended from
        // a pipe/rope, the sense of "up"/"down" is deliberately inverted to match the player's arm
        // position rather than screen direction: Up pulls the knees up into the compact
        // clamber pose (mirroring Crawl), Down extends back out into the normal fully-stretched
        // hang (mirroring Walk) - and, since fully stretched is already the "least attached" pose,
        // a second Down from there means letting go entirely and dropping. Up and Jump are
        // deliberately distinct inputs everywhere (see InputState.IsJumpPressed) - Up is always a
        // directional/posture action, Jump is always a distinct explicit action - because Hang
        // genuinely needs both at the same time (Up pulls into Clamber, Jump swings off), and
        // keeping the same rule on the ground and while climbing avoids a special case. All three
        // latches are edge-triggered (only the frame the key is first pressed) so holding a
        // direction doesn't repeatedly cycle through every step in one press.
        var upKeyDown = input.IsUpPressed;
        var downKeyDown = input.IsDownPressed;
        var jumpKeyDown = input.IsJumpPressed;
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
                // ladder before the jump is ever visible.
                player.IsClimbing = false;
                velocity.Y = -ClimbJumpSpeed;
                _suppressClimbUntilClear = true;
            }
        }
        else if (player.IsHanging && wasHanging)
        {
            if (player.IsClambering && downPressedThisFrame)
            {
                player.IsClambering = false;
            }
            else if (!player.IsClambering && upPressedThisFrame)
            {
                player.IsClambering = true;
            }
            else if (!player.IsClambering && jumpPressedThisFrame)
            {
                // Only the fully-stretched Hang (not the compact Clamber grip) can jump/swing
                // off - representing letting go of a pipe/rope while swinging from it, which only
                // makes sense from the stretched-out pose. Jump alone swings straight upward (e.g.
                // onto a pipe a little higher); combined with Left/Right it's a diagonal swing,
                // reusing whichever horizontal speed the player's current stance already grants
                // (see the ordinary horizontal-movement block below) rather than a separate one.
                // Same debounce as the explicit let-go below, so the player can't instantly
                // re-grab the exact surface they just launched off.
                player.IsHanging = false;
                velocity.Y = -HangJumpSpeed;
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

        var moveSpeed = player.Stance == "Walk" ? WalkSpeed : CrawlSpeed;

        // TODO: Player movement is currently driven directly by velocity assignment from input,
        // unlike every other body (DynamicObject2D/MovingEnemy2D), which now move via a mass-scaled
        // force accumulator (see StepMovingBodyWithForces). Revisit this to apply player movement
        // as a force causing acceleration instead, consistent with the rest of the physics model,
        // as part of a future physics engine refinement - deliberately deferred/out of scope for
        // now (see docs/Decisions.md).

        // Horizontal movement is directly driven by input (no acceleration/friction for milestone 1).
        // While climbing a ladder, horizontal input still applies (at a slower, deliberate side
        // speed) so the player can step off sideways onto an adjacent floor or ladder rather than
        // only ever being able to leave via a jump; while hanging from a pipe/bar, lateral
        // movement uses its own dedicated (and slower still while Clambering) speed rather than
        // reusing the ground Walk/Crawl speeds, since swinging/shimmying along a hangable surface
        // is its own distinct kind of locomotion.
        var horizontalSpeed = player.IsClimbing ? ClimbHorizontalSpeed
            : player.IsHanging ? (player.IsClambering ? ClamberSpeed : HangSpeed)
            : moveSpeed;
        velocity.X = 0;
        if (input.IsLeftPressed)
        {
            velocity.X -= horizontalSpeed;
        }
        if (input.IsRightPressed)
        {
            velocity.X += horizontalSpeed;
        }

        if (player.IsClimbing)
        {
            // Straight up/down movement at a fixed climb speed, gravity already suspended via
            // Player2D.GravityAffected while IsClimbing is set. Jump-off (letting go of the
            // ladder entirely) is handled above, alongside the other stance-ladder transitions -
            // this only runs for an ordinary climb with no exit this frame.
            velocity.Y = 0;
            if (input.IsUpPressed)
            {
                velocity.Y -= ClimbVerticalSpeed;
            }
            if (input.IsDownPressed)
            {
                velocity.Y += ClimbVerticalSpeed;
            }
            player.IsGrounded = false;
        }
        else if (player.IsHanging)
        {
            // Held in place vertically (gravity suspended via Player2D.GravityAffected). Letting
            // go downward is an explicit step of the hang stance ladder above (a second Down
            // press from the fully-stretched pose); a Jump press from that same pose instead
            // jumps/swings off (see above) and has already cleared IsHanging and set velocity.Y
            // by the time this runs, so this branch only still applies to an ordinary hang with
            // no exit this frame.
            velocity.Y = 0;
            player.IsGrounded = false;
        }
        else if (input.IsJumpPressed && player.IsGrounded && player.Stance == "Walk" && !stoodUpThisFrame)
        {
            velocity.Y = -WalkJumpSpeed;
            player.IsGrounded = false;
        }

        player.Velocity = velocity;

        // "Jump" is a visual-only pose, not a distinct stance the player can be toggled into/out
        // of like Crawl - it's simply what's shown while airborne, regardless of which stance
        // (Walk or Crawl) the player was in when they left the ground (e.g. crawling off a ledge
        // still assumes the jump pose mid-air). Stance itself stays "Walk"/"Crawl" throughout;
        // only the resolved pose swaps to the Jump stance's clips while not grounded. Climbing/
        // hanging take priority over both: they're their own dedicated stances ("Climb" and
        // "Hang"/"Clamber" depending on IsClambering), shown regardless of IsGrounded.
        //
        // Facing selects which of a stance's clips to show, and is resolved along whichever axis
        // that stance actually moves on (see Facing's own doc comment) - horizontal (Left/Right,
        // from velocity.X) for every ground/air/hang stance, but vertical (Up/Down, from the
        // climb input directly rather than velocity.Y, since climbing sets velocity.Y itself
        // below) for Climb, whose idle-vs-arm-over-arm distinction is a movement direction, not a
        // sideways-facing one. This replaces the previous separate "ClimbMoving" stance - climbing
        // now has exactly one stance with three clips (Idle/Up/Down), symmetric with every other
        // stance instead of being a special case.
        var poseStance = player.IsClimbing ? "Climb"
            : player.IsHanging ? (player.IsClambering ? "Clamber" : "Hang")
            : !player.IsGrounded ? "Jump"
            : player.Stance;
        var facing = player.IsClimbing
            ? (input.IsUpPressed ? Facing.Up : input.IsDownPressed ? Facing.Down : Facing.Idle)
            : velocity.X < 0 ? Facing.Left : velocity.X > 0 ? Facing.Right : Facing.Idle;
        player.SetPose(player.Sprite, poseStance, facing);

        foreach (var body in world.Objects)
        {
            switch (body)
            {
                case KinematicObject2D kinematicObject:
                    // Predefined constant motion, no gravity/force integration.
                    kinematicObject.Position = new Vector2D(
                        kinematicObject.Position.X + kinematicObject.Velocity.X * deltaSeconds,
                        kinematicObject.Position.Y + kinematicObject.Velocity.Y * deltaSeconds);
                    break;

                // The player keeps its existing direct velocity-assignment model (see the TODO
                // above and docs/Decisions.md) rather than the force accumulator below - it is
                // deliberately excluded from this round of force-based movement, so it is matched
                // first, ahead of the generic IGravityAffected/IPhysicsBody cases every other body
                // falls into.
                case Player2D playerBody:
                    StepMovingBody(world, playerBody, playerBody.GravityAffected, deltaSeconds);
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

    private static void StepMovingBody(World2D world, IPhysicsBody body, bool gravityAffected, double deltaSeconds)
    {
        var velocity = body.Velocity;

        if (gravityAffected)
        {
            velocity.Y += world.Gravity * deltaSeconds;
        }

        body.Velocity = velocity;

        body.Position = new Vector2D(
            body.Position.X + velocity.X * deltaSeconds,
            body.Position.Y + velocity.Y * deltaSeconds);
    }

    /// <summary>
    /// Force-based counterpart to <see cref="StepMovingBody"/>, used for every non-player moving
    /// body (see the dispatch loop in <see cref="Step"/>): rather than adding a fixed velocity
    /// delta for gravity directly, this accumulates forces acting on the body this frame (today,
    /// just gravity as a mass-scaled force - <c>F = mass * gravity</c>, matching how a real
    /// falling object's weight scales with its mass) into a net force, converts that to an
    /// acceleration via <c>a = F / mass</c>, and integrates that into velocity. For a single
    /// gravity-only force this reduces to the exact same <c>velocity.Y += gravity * dt</c> as
    /// before (mass cancels out of <c>F / mass = mass * gravity / mass = gravity</c>) - the
    /// accumulator's value is that any future force source (wind, thrust, a spring, etc.) can be
    /// summed in here alongside gravity before the single acceleration/integration step, rather
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
    /// right at the point of contact (see <c>ResolveRectAgainstSolid</c>/<c>ResolveBodyPair</c>),
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

        // Extension point: a future force source (e.g. a hypothetical IThrustBody capability for
        // a moving enemy that flies/hovers via its own upward force) would sum its contribution
        // into netForce here, alongside gravity, before the single acceleration/integration step
        // below - not add a separate bespoke velocity mutation. No such capability exists yet.

        var acceleration = new Vector2D(netForce.X / mass, netForce.Y / mass);

        var velocity = body.Velocity;
        velocity.X += acceleration.X * deltaSeconds;
        velocity.Y += acceleration.Y * deltaSeconds;
        body.Velocity = velocity;

        body.Position = new Vector2D(
            body.Position.X + velocity.X * deltaSeconds,
            body.Position.Y + velocity.Y * deltaSeconds);
    }
}
