using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Input;
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
    /// <summary>Default target ground speed while standing/walking - see <see cref="Player2D.WalkSpeed"/>.</summary>
    public const double DefaultWalkSpeed = 12.0;

    /// <summary>Default target ground speed while crouched/crawling - see <see cref="Player2D.CrawlSpeed"/>.</summary>
    public const double DefaultCrawlSpeed = 6.0;

    /// <summary>
    /// Default magnitude of the mass-scaled horizontal "motor" force applied to converge the
    /// player's <see cref="Player2D.Velocity"/>.X toward the current target walk/crawl speed (see
    /// <see cref="UpdateWalkForce"/>) - same name/role as <see cref="MovingEnemy2D.DefaultPatrolForceMultiplier"/>
    /// for a patrolling enemy, proportional to the remaining speed gap so the player accelerates
    /// promptly yet still settles at exactly the target speed rather than overshooting it every
    /// frame. See <see cref="Player2D.WalkForceMultiplier"/>.
    /// </summary>
    public const double DefaultWalkForceMultiplier = 40.0;

    private const double ClimbHorizontalSpeed = 8.0;
    private const double ClimbVerticalSpeed = 10.0;
    private const double HangSpeed = 8.0;
    private const double ClamberSpeed = 5.0;
    private const double WalkJumpSpeed = 22.0;
    private const double ClimbJumpSpeed = 18.0;
    private const double HangJumpSpeed = 14.0;

    private bool _wasUpKeyDown;
    private bool _wasDownKeyDown;
    private bool _wasJumpKeyDown;

    /// <summary>
    /// Set the instant the player jumps off a ladder (see the pose ladder in <see cref="Step"/>),
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
        // be grabbed out of the air. Crawling can't climb directly (too low a pose to reach a
        // rung) - engaging climb requires Pose == "Walk" already, so a crawling player must
        // first explicitly stand up (the ordinary Crawl->Walk pose toggle below, its own
        // separate key press) before a later press can grab the ladder; there is no combined
        // "stand up and grab on" shortcut. Hanging engages automatically as soon as the surface is
        // touched from underneath, same as that reference project. Both disengage the moment the
        // player is no longer touching the corresponding surface; climbing additionally yields to
        // solid ground (landing on a real floor always takes priority over still nominally
        // overlapping a passable ladder, and a Jump press while climbing lets go and launches -
        // see the climbing movement block below), and hanging additionally yields to an explicit
        // "let go" or a Jump press (see the hang pose ladder below).
        if (player.IsClimbing && (!player.IsTouchingClimbable || player.IsGrounded))
        {
            player.IsClimbing = false;
        }
        else if (!player.IsClimbing && player.IsTouchingClimbable && player.Pose == "Walk" && !_suppressClimbUntilClear && (input.IsUpPressed || input.IsDownPressed))
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
            player.IsClambering = player.Pose == "Crawl";
        }

        // Once the player is no longer touching the hangable surface at all (having actually
        // fallen clear of it after an explicit "let go" below), the debounce lock is released so
        // a later approach can grab on again normally.
        if (!player.IsTouchingHangable)
        {
            player.SuppressHangUntilClear = false;
        }

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
            if (player.Pose == "Walk" && downPressedThisFrame)
            {
                player.Pose = "Crawl";
            }
            else if (player.Pose == "Crawl" && upPressedThisFrame)
            {
                player.Pose = "Walk";
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
                // reusing whichever horizontal speed the player's current pose already grants
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

        var moveSpeed = player.Pose == "Walk" ? player.WalkSpeed : player.CrawlSpeed;

        // Horizontal movement is driven by a mass-scaled "motor" force converging the player's
        // velocity toward the target walk/crawl/climb/hang speed (see UpdateWalkForce and
        // IWalkForceBody), summed into the net force alongside gravity by
        // StepMovingBodyWithForces - consistent with every other body's force-based movement
        // (see MovingEnemy2D.PatrolForce) rather than a bespoke direct velocity-assignment path.
        // While climbing a ladder, horizontal input still applies (at a slower, deliberate side
        // speed) so the player can step off sideways onto an adjacent floor or ladder rather than
        // only ever being able to leave via a jump; while hanging from a pipe/bar, lateral
        // movement uses its own dedicated (and slower still while Clambering) speed rather than
        // reusing the ground Walk/Crawl speeds, since swinging/shimmying along a hangable surface
        // is its own distinct kind of locomotion.
        var horizontalSpeed = player.IsClimbing ? ClimbHorizontalSpeed
            : player.IsHanging ? (player.IsClambering ? ClamberSpeed : HangSpeed)
            : moveSpeed;
        // Target velocity is expressed relative to whatever solid the player is currently
        // grounded on (see GetGroundVelocityX) rather than an absolute world-frame speed: without
        // this, standing still on a moving platform would target world-frame velocity 0, and
        // WalkForce (scaled by WalkForceMultiplier, deliberately strong so input feels responsive)
        // would then fight CollisionSystem's friction-based drag pulling the player toward the
        // platform's own velocity every single frame - the two forces fighting is what made the
        // player appear unable to ride a horizontally moving platform at all. Climbing/hanging
        // have no such platform-carry concept, so their target speed stays absolute.
        var groundVelocityX = player.IsClimbing || player.IsHanging ? 0.0 : GetGroundVelocityX(player);
        var targetVelocityX = groundVelocityX;
        if (input.IsLeftPressed)
        {
            targetVelocityX -= horizontalSpeed;
        }
        if (input.IsRightPressed)
        {
            targetVelocityX += horizontalSpeed;
        }
        UpdateWalkForce(player, targetVelocityX);

        if (player.IsClimbing)
        {
            // Straight up/down movement at a fixed climb speed, gravity already suspended via
            // Player2D.GravityAffected while IsClimbing is set. Jump-off (letting go of the
            // ladder entirely) is handled above, alongside the other pose-ladder transitions -
            // this only runs for an ordinary climb with no exit this frame. Deliberately still a
            // direct velocity assignment, not a force - climbing/hanging are discrete
            // state-machine locomotion modes (like the jump-off below), not the continuous
            // ground-level walk/crawl movement WalkForce drives.
            velocity.Y = 0;
            if (input.IsUpPressed)
            {
                velocity.Y -= ClimbVerticalSpeed;
            }
            if (input.IsDownPressed)
            {
                velocity.Y += ClimbVerticalSpeed;
            }
            // IsGrounded is derived from this frame's SurfaceBottom contact (see Body2D.IsGrounded);
            // clearing it immediately here (rather than waiting for the next collision pass) means
            // the player's pose/jump-gating reflects "climbing, not grounded" the same frame a
            // ladder is grabbed, not one frame late.
            player.RemoveContact(ContactType.SurfaceBottom);
        }
        else if (player.IsHanging)
        {
            // Held in place vertically (gravity suspended via Player2D.GravityAffected). Letting
            // go downward is an explicit step of the hang pose ladder above (a second Down
            // press from the fully-stretched pose); a Jump press from that same pose instead
            // jumps/swings off (see above) and has already cleared IsHanging and set velocity.Y
            // by the time this runs, so this branch only still applies to an ordinary hang with
            // no exit this frame.
            velocity.Y = 0;
            player.RemoveContact(ContactType.SurfaceBottom);
        }
        else if (input.IsJumpPressed && player.IsGrounded && player.Pose == "Walk" && !stoodUpThisFrame)
        {
            velocity.Y = -WalkJumpSpeed;
            // Clears IsGrounded immediately so the jump's own frame already shows airborne (jump
            // pose, re-jump gated out) instead of waiting for the next collision pass to notice
            // the player has left the surface.
            player.RemoveContact(ContactType.SurfaceBottom);
        }

        player.Velocity = velocity;

        foreach (var body in world.Objects)
        {
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
    /// The horizontal velocity of whichever solid <paramref name="player"/> currently rests on
    /// top of (<see cref="ContactType.SurfaceBottom"/>), or 0 if not grounded or resting on
    /// stationary terrain - the reference frame <see cref="UpdateWalkForce"/>'s target velocity is
    /// expressed relative to, so standing still on a moving platform doesn't fight the platform's
    /// own carry (see the remarks where this is called in <see cref="Step"/>). Picks the first
    /// grounded contact that is itself an <see cref="IPhysicsBody"/> with a real velocity (e.g.
    /// <see cref="KinematicObject2D"/>); ordinary stationary terrain has none, so this falls back
    /// to 0 exactly as before this fix.
    /// </summary>
    private static double GetGroundVelocityX(Player2D player)
    {
        foreach (var solid in player.GetContactingBodies(ContactType.SurfaceBottom))
        {
            if (solid is IPhysicsBody solidBody)
            {
                return solidBody.Velocity.X;
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Recomputes <see cref="Player2D.WalkForce"/> as a proportional "motor" force converging
    /// <paramref name="player"/>'s current horizontal velocity toward <paramref name="targetVelocityX"/>
    /// (the current walk/crawl/climb-side/hang-side speed the input this frame calls for) -
    /// mirrors <see cref="MovingEnemy2D.UpdatePatrolDirection"/>'s role for a patrolling enemy, but
    /// proportional to the remaining speed gap (scaled by <see cref="Player2D.WalkForceMultiplier"/>) rather than
    /// a fixed-direction force, so the player still promptly reaches and then holds the target
    /// speed - the "no acceleration/friction" ground feel from the old direct-assignment model -
    /// instead of accelerating indefinitely or oscillating around it. Mass-scaled like every other
    /// force here, so a body with no resolved material (<see cref="Body2D.Mass"/> of 0, treated as
    /// mass 1) still walks at the ordinary rate.
    /// </summary>
    private static void UpdateWalkForce(Player2D player, double targetVelocityX)
    {
        var mass = player.Mass > 0 ? player.Mass : 1.0;
        player.WalkForce = new Vector2D((targetVelocityX - player.Velocity.X) * mass * player.WalkForceMultiplier, 0);
    }

    /// <summary>
    /// Force-based counterpart to <see cref="StepMovingBody"/>, used for every non-player moving
    /// body (see the dispatch loop in <see cref="Step"/>): rather than adding a fixed velocity
    /// delta for gravity directly, this accumulates forces acting on the body this frame (gravity
    /// as a mass-scaled force - <c>F = mass * gravity</c>, matching how a real falling object's
    /// weight scales with its mass - plus, for an <see cref="IPatrolBody"/>, its own patrol force)
    /// into a net force, converts that to an acceleration via <c>a = F / mass</c>, and integrates
    /// that into velocity. For a single gravity-only force this reduces to the exact same
    /// <c>velocity.Y += gravity * dt</c> as before (mass cancels out of
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

        // Patrolling bodies (see IPatrolBody) contribute their own horizontal force here,
        // recomputed each frame from their current position relative to their patrol bounds -
        // this is the "future force source" extension point this comment used to describe before
        // one actually existed; any further force source (wind, thrust, etc.) would sum in here
        // the same way, alongside gravity, before the single acceleration/integration step below.
        if (body is IPatrolBody patrolBody)
        {
            patrolBody.UpdatePatrolDirection();
            netForce += patrolBody.PatrolForce;
        }

        // The player's ground-level walk/crawl (and climb/hang side-step) locomotion contributes
        // its own horizontal motor force here - see IWalkForceBody and UpdateWalkForce, called
        // earlier this frame by Step before this integration runs.
        if (body is IWalkForceBody walkForceBody)
        {
            netForce += walkForceBody.WalkForce;
        }

        var acceleration = new Vector2D(netForce.X / mass, netForce.Y / mass);

        var velocity = body.Velocity;
        velocity.X += acceleration.X * deltaSeconds;
        velocity.Y += acceleration.Y * deltaSeconds;
        body.Velocity = velocity;

        body.Position = new Vector2D(
            body.Position.X + velocity.X * deltaSeconds,
            body.Position.Y + velocity.Y * deltaSeconds);

        // Pose (see IPosedBody) is resolved last, after velocity/position are both finalized for
        // this frame, so a velocity-derived facing (e.g. MovingEnemy2D's) reflects this frame's
        // actual resolved motion rather than a pre-integration estimate.
        if (body is IPosedBody posedBody)
        {
            posedBody.UpdatePose();
        }
    }
}
