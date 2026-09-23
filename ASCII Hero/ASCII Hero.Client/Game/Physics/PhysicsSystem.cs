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
    /// <summary>
    /// Tuning constant for <see cref="ResolveMediumForceScale"/>'s reciprocal falloff - larger
    /// values make a given <see cref="Assets.Material.Viscosity"/> dampen self-generated force
    /// more aggressively. Applied to raw <c>Viscosity</c> directly (see
    /// <see cref="ResolveMediumForceScale"/>'s own doc comment for why), so even <c>Air</c>'s own
    /// small authored baseline (0.02, see <c>Global/MaterialLibrary.ini</c>) applies a slight
    /// land-side penalty; <see cref="GameDefaults.WalkJumpSpeed"/> (and other jump/walk force
    /// constants in <see cref="GameDefaults"/>) are tuned to compensate so on-land feel stays
    /// close to its pre-damping baseline, while <c>Water</c>'s much larger 0.15 still comes out
    /// clearly, noticeably weaker. Moved to <see cref="PhysicsConstants.MediumForceScaleFalloff"/>.
    /// </summary>

    private bool _wasUpKeyDown;
    private bool _wasDownKeyDown;
    private bool _wasJumpKeyDown;

    /// <summary>
    /// Set the instant the player jumps off a ladder (see the pose ladder in <see cref="Step"/>),
    /// and held until <see cref="IClimberBody.IsTouchingClimbable"/> goes false again. Without
    /// this, <see cref="GameDefaults.ClimbJumpSpeed"/> is slow enough that the player is still both overlapping
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

        // Swim: medium-based, not reach-based (see ISwimmerBody's own doc comment) - there is no
        // "touching" flag to consult, since Body2D.CurrentMedium is already resolved fresh every
        // frame for every body regardless. Mirrors climbing's deliberate-press engagement (not
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
            && (input.IsLeftPressed || input.IsRightPressed || input.IsUpPressed || input.IsDownPressed))
        {
            player.IsSwimming = true;
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
                velocity.Y = -GameDefaults.ClimbJumpSpeed * ResolveMediumForceScale(player.CurrentMedium);
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
                velocity.Y = -GameDefaults.HangJumpSpeed * ResolveMediumForceScale(player.CurrentMedium);
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
        // player appear unable to ride a horizontally moving platform at all. Climbing/hanging/
        // swimming have no such platform-carry concept, so their target speed stays absolute.
        var groundVelocityX = player.IsClimbing || player.IsHanging || player.IsSwimming ? 0.0 : player.GetSurfaceVelocityX();
        var targetVelocityX = groundVelocityX;
        if (input.IsLeftPressed)
        {
            targetVelocityX -= horizontalSpeed;
        }
        if (input.IsRightPressed)
        {
            targetVelocityX += horizontalSpeed;
        }
        if (!player.IsGrounded && !player.IsClimbing && !player.IsHanging && !player.IsSwimming && !input.IsLeftPressed && !input.IsRightPressed)
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
        player.MoveIntentX = input.IsLeftPressed ? -1.0 : input.IsRightPressed ? 1.0 : 0.0;
        // Raw up/down input intent for UpdatePose's climbing facing (see Body2D.MoveIntentY) -
        // the vertical counterpart to MoveIntentX above, for exactly the same reason: the climb
        // motor's velocity only ever asymptotically approaches zero, never reaching it exactly.
        player.MoveIntentY = input.IsUpPressed ? -1.0 : input.IsDownPressed ? 1.0 : 0.0;

        // Vertical target speed for the same mass-scaled motor force: straight up/down at a
        // fixed climb speed while IsClimbing (gravity already suspended via
        // Player2D.GravityAffected), held at exactly zero while IsHanging (gravity likewise
        // suspended, so a zero-velocity target is all it takes to hold position - there is no
        // opposing force it needs to fight), and left equal to the player's own current Velocity.Y
        // everywhere else so the vertical component of WalkForce is simply zero and gravity/
        // collision response (falling, landing, jump arcs) are entirely unaffected by this motor.
        // Both climbing and hanging are sustained, ongoing locomotion for as long as they're
        // engaged - exactly like walking/crawling - so they use the same continuous
        // force-convergence model rather than a direct per-frame velocity assignment; only the
        // discrete jump-off/let-go moments below are true instantaneous impulses.
        double targetVelocityY;
        if (player.IsClimbing)
        {
            targetVelocityY = 0;
            if (input.IsUpPressed)
            {
                targetVelocityY -= GameDefaults.ClimbVerticalSpeed;
            }
            if (input.IsDownPressed)
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
            targetVelocityY = 0;
            player.RemoveContact(ContactType.SurfaceBottom);
        }
        else if (player.IsSwimming)
        {
            // Up/down while swimming is vertical thrust (depth control), not a distinct pose.
            // Unlike climbing/hanging, gravity is NOT suspended while swimming (see
            // Player2D.GravityAffected) - buoyancy/drag (IMediumAffected, always-on) already
            // offsets it, the same way it does for any other immersed body. With no Up/Down
            // held, the target is the player's own current velocity (motor contributes ~0 net
            // force), so gravity/buoyancy alone govern idle vertical drift - mirroring the
            // airborne "no horizontal key held" case above. With Up/Down held, the target is a
            // fixed absolute speed (0 +/- SwimVerticalSpeed), exactly like climbing's vertical
            // target above - NOT relative to the current (already-moving) velocity, which would
            // recompute a moving goalpost every frame and never actually converge, snowballing
            // into runaway speed instead of the intended bounded SwimVerticalSpeed.
            targetVelocityY = velocity.Y;
            if (input.IsUpPressed)
            {
                targetVelocityY = -GameDefaults.SwimVerticalSpeed;
            }
            if (input.IsDownPressed)
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
        UpdateWalkForce(player, targetVelocityX, targetVelocityY);

        if (!player.IsClimbing && !player.IsHanging && input.IsJumpPressed && player.IsGrounded && player.Pose == "Walk" && !stoodUpThisFrame)
        {
            // A true impulse: an instantaneous Delta-v applied once, on the frame the jump is
            // pressed, not a target the player's velocity converges toward over subsequent
            // frames - the same jump-off model as the ladder/hang variants above, just at the
            // strongest magnitude since it launches off solid ground with both legs planted.
            // Scaled by the current medium's viscosity (see ResolveMediumForceScale) so a
            // push-off through a viscous medium (e.g. water) can't launch as high as the same
            // push-off through air, even though buoyancy has already cancelled most of gravity.
            velocity.Y = -GameDefaults.WalkJumpSpeed * ResolveMediumForceScale(player.CurrentMedium);
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
    /// Resolves the material a body at <paramref name="body"/>'s current collision rectangles is
    /// immersed in this frame: <see cref="Assets.MaterialLibrary.Undefined"/>'s <c>Air</c>-like
    /// default unless <paramref name="body"/> overlaps one or more static <see cref="Body2D.IsPassable"/>
    /// volumes, in which case the highest-<see cref="Assets.Material.Density"/> overlapping
    /// volume's own resolved material wins (see docs/Plans/AmbientMedium.md's tie-break rule).
    /// Deliberately a plain linear scan over <paramref name="world"/>'s passable statics, mirroring
    /// <see cref="CollisionSystem"/>'s existing per-frame climbable/hangable overlap scans, rather
    /// than adding a new broad-phase spatial structure just for this.
    /// </summary>
    /// <summary>
    /// Maps a medium's <see cref="Assets.Material.Viscosity"/> to a multiplier in
    /// <c>[MinMediumForceScale, 1.0]</c>, applied to a body's own actively-generated
    /// force/impulse (walk/patrol motor force, jump-off impulses - see
    /// <see cref="UpdateWalkForce"/>, <see cref="MovingEnemy2D.UpdatePatrolDirection"/>, and the
    /// jump-off sites in <see cref="Step"/>) - never the passive buoyancy/drag forces already
    /// computed in <see cref="StepMovingBodyWithForces"/>, which remain solely density/viscosity
    /// driven as before. Deliberately keyed on <see cref="Assets.Material.Viscosity"/> alone, not
    /// <see cref="Assets.Material.Density"/>: density already fully does its own job via
    /// buoyancy (Archimedes' principle) - reusing it here too would double-count the same
    /// property for two unrelated physical effects. Viscosity is the property that actually
    /// resists a body's own stride/push-off against a surrounding fluid (the same property
    /// already driving the existing quadratic drag term), so it is the physically appropriate
    /// basis for damping self-generated force the way a swimmer's stroke or a push-off through
    /// water is inherently less effective than the same effort on/through open air. Deliberately
    /// uses the medium's raw <c>Viscosity</c> directly (not relative to any other medium's own
    /// value) for pure, literal physical accuracy - even <c>Air</c>'s own small authored
    /// <c>Viscosity</c> (see <c>Global/MaterialLibrary.ini</c>) applies a (very slight, by design)
    /// penalty here, same as it already does for the existing passive drag term, rather than
    /// treating whichever medium happens to be the ambient default as an artificial zero-point.
    /// Falls off reciprocally toward <see cref="PhysicsConstants.MinMediumForceScale"/>, which is never crossed so
    /// a body is never fully unable to move under its own power, however viscous the medium.
    /// </summary>
    internal static double ResolveMediumForceScale(Assets.Material medium)
    {
        var scale = 1.0 / (1.0 + medium.Viscosity * PhysicsConstants.MediumForceScaleFalloff);
        return Math.Max(scale, PhysicsConstants.MinMediumForceScale);
    }

    /// <summary>
    /// Whether <paramref name="medium"/> is dense/viscous enough to swim in - the medium-based
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
    /// immersed in this frame, together with how much of the body's own vertical extent is
    /// actually submerged in that winning material - a value in <c>[0, 1]</c>, used to scale
    /// buoyancy/drag proportionally to submersion depth (see <see cref="StepMovingBodyWithForces"/>)
    /// rather than applying full-body buoyancy the instant any single cell overlaps a medium
    /// volume at all. Without this, a body merely grazing a water surface received exactly the
    /// same upward force as one fully submerged, wildly overshooting the true equilibrium point
    /// every frame near the surface and producing a visible bounce (observed both on a floating
    /// ball and, once swimming existed, as player pose flicker at the waterline). At fraction 1.0
    /// (fully submerged) this reduces to exactly the previous full-strength behavior, so
    /// already-submerged locomotion (walking/swimming underwater) is unaffected.
    ///
    /// The winning material is still chosen exactly as before (highest-<see cref="Assets.Material.Density"/>
    /// overlapping volume wins on a tie, deterministic regardless of placement order). The
    /// submerged fraction is then computed only from candidates resolving to that same winning
    /// material: each such candidate's actual overlapped vertical extent against the body (via
    /// <see cref="Physics.Rect.VerticalOverlap"/>, over every rect pair - so a body's whole
    /// silhouette, not just its topmost/bottommost point, contributes) is merged into a single
    /// covered-interval union (so several stacked/adjacent placements of the same medium don't
    /// double-count their overlap), then divided by the body's own overall bounding-box height.
    /// When no placed medium volume overlaps at all, the body is resolved as fully immersed
    /// (fraction 1.0) in the default "Air" fallback rather than fraction 0.0 - Air is the ambient
    /// substance filling all otherwise-unoccupied space, not a bounded level-authored volume with
    /// edges a body can be only partially submerged past, so its own (low but nonzero) Viscosity
    /// drag keeps applying continuously everywhere outside a denser placed volume, exactly as it
    /// always did before submerged-fraction scaling was introduced.
    /// </summary>
    private static (Assets.Material Medium, double SubmergedFraction) ResolveCurrentMedium(World2D world, IPhysicsBody body)
    {
        var resolved = world.Materials.Get("Air");
        Body2D? resolvedCandidate = null;
        foreach (var candidate in world.Objects)
        {
            if (!candidate.IsStatic || !candidate.IsPassable)
            {
                continue;
            }

            // EffectInstance2D sets IsPassable purely so a cosmetic effect (e.g. a killed
            // enemy's persisting "crumble" husk) never blocks movement - it is not a
            // level-design ambient-medium volume the way a placed Water/BodyOfWater section
            // is, and never should be treated as one just because it happens to satisfy the
            // same IsStatic/IsPassable check. Without this exclusion, a killable hazard's own
            // (often fairly dense, e.g. Plant) material would make its leftover husk act as a
            // dense medium the moment the player merely overlaps its footprint, producing
            // unintended buoyancy (e.g. an oddly high jump) that has nothing to do with the
            // husk's purely decorative role.
            if (candidate is World.EffectInstance2D)
            {
                continue;
            }

            // Likewise, IsClimbable/IsHangable terrain (a ladder's rungs, a pipe/bar) is
            // structural - something gripped/hung from - not a substance the player is ever
            // meant to be immersed in, regardless of whatever material it's been given (e.g.
            // for its render color, or simply because a future asset's DefaultMaterial isn't
            // deliberately set to a zero-density placeholder the way Ladder_settings.ini's
            // currently is). Excluded explicitly rather than relying on that being density-0
            // by accident, which would silently break again the moment any climbable/hangable
            // asset is given a real (denser-than-Air) material for an unrelated reason.
            if (candidate.IsClimbable || candidate.IsHangable)
            {
                continue;
            }

            var candidateMaterial = world.Materials.Get(candidate.MaterialName);
            if (candidateMaterial.Density <= resolved.Density)
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
            // medium volume's footprint.
            return (resolved, 1.0);
        }

        // Second pass: now that the winning material is known, gather every qualifying
        // candidate resolving to that same material (not just the single one that happened to
        // win the tie-break above) and merge their vertical overlap against the body into one
        // covered-interval union, so the submerged fraction reflects the body's true total
        // overlapped extent rather than just one arbitrarily-chosen candidate's.
        var bodyTop = double.MaxValue;
        var bodyBottom = double.MinValue;
        foreach (var rect in body.CollisionRects)
        {
            bodyTop = Math.Min(bodyTop, rect.Top);
            bodyBottom = Math.Max(bodyBottom, rect.Bottom);
        }

        var bodyHeight = bodyBottom - bodyTop;
        if (bodyHeight <= 0)
        {
            return (resolved, 1.0);
        }

        var coveredIntervals = new List<(double Top, double Bottom)>();
        foreach (var candidate in world.Objects)
        {
            if (!candidate.IsStatic || !candidate.IsPassable || candidate is World.EffectInstance2D
                || candidate.IsClimbable || candidate.IsHangable)
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
                    var overlapTop = Math.Max(Math.Max(bodyRect.Top, candidateRect.Top), bodyTop);
                    var overlapBottom = Math.Min(Math.Min(bodyRect.Bottom, candidateRect.Bottom), bodyBottom);
                    if (overlapBottom > overlapTop)
                    {
                        coveredIntervals.Add((overlapTop, overlapBottom));
                    }
                }
            }
        }

        var submergedHeight = MergeIntervalLength(coveredIntervals);
        var submergedFraction = Math.Clamp(submergedHeight / bodyHeight, 0.0, 1.0);
        return (resolved, submergedFraction);
    }

    /// <summary>
    /// Merges a set of possibly-overlapping <c>[Top, Bottom)</c> vertical intervals and returns
    /// the total length they cover, without double-counting overlapping regions - used by
    /// <see cref="ResolveCurrentMedium"/> so several stacked/adjacent placements of the same
    /// medium material don't inflate the submerged fraction beyond the body's own true covered
    /// extent.
    /// </summary>
    private static double MergeIntervalLength(List<(double Top, double Bottom)> intervals)
    {
        if (intervals.Count == 0)
        {
            return 0.0;
        }

        intervals.Sort((a, b) => a.Top.CompareTo(b.Top));
        var totalLength = 0.0;
        var currentTop = intervals[0].Top;
        var currentBottom = intervals[0].Bottom;
        for (var i = 1; i < intervals.Count; i++)
        {
            var (top, bottom) = intervals[i];
            if (top <= currentBottom)
            {
                currentBottom = Math.Max(currentBottom, bottom);
            }
            else
            {
                totalLength += currentBottom - currentTop;
                currentTop = top;
                currentBottom = bottom;
            }
        }

        totalLength += currentBottom - currentTop;
        return totalLength;
    }

    /// <summary>Same overlap test used by <see cref="CollisionSystem"/> - true if any of <paramref name="a"/>'s collision rects overlap any of <paramref name="b"/>'s.</summary>
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
    /// calls for - mirrors <see cref="MovingEnemy2D.UpdatePatrolDirection"/>'s role for a
    /// patrolling enemy, but proportional to the remaining speed gap (scaled by
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
    private static void UpdateWalkForce(Player2D player, double targetVelocityX, double targetVelocityY)
    {
        var mass = player.Mass > 0 ? player.Mass : 1.0;
        // Horizontal strength is cut to AirControlMultiplier while airborne (see its own doc
        // comment) so a jump carries over whatever horizontal speed the player left the ground
        // with - including a moving platform's carried speed - rather than that speed being
        // snapped away toward the absolute walk-speed target within a single frame by the full
        // ground motor. Vertical strength is left untouched: it already targets the player's own
        // current Velocity.Y while airborne (see the call site in Step), so it contributes ~0
        // regardless and gravity/jump impulses remain entirely in charge of vertical motion.
        var horizontalMultiplier = player.IsGrounded ? player.WalkForceMultiplier : player.WalkForceMultiplier * GameDefaults.AirControlMultiplier;
        // Also scaled by the current medium's viscosity (see ResolveMediumForceScale) - a stride
        // through a viscous medium is inherently less effective than the same muscular effort on
        // solid ground/open air, independent of the passive buoyancy/drag already applied
        // elsewhere in StepMovingBodyWithForces.
        var mediumScale = ResolveMediumForceScale(player.CurrentMedium);
        player.WalkForce = new Vector2D(
            (targetVelocityX - player.Velocity.X) * mass * horizontalMultiplier * mediumScale,
            (targetVelocityY - player.Velocity.Y) * mass * player.WalkForceMultiplier * mediumScale);
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

        // Ambient medium (see docs/Plans/AmbientMedium.md): resolved fresh every frame (mirroring
        // IsGrounded's "derived, not stored" philosophy, just needing an explicit recompute call
        // since it depends on spatial overlap rather than already-tracked contact state) and
        // exposed via Body2D.CurrentMedium for other systems (e.g. a future swim pose) to read.
        // Resolved up front (rather than after patrol/walk force below, as originally) so this
        // same frame's medium - not a stale value from before this body's own contacts/position
        // were last updated - is available for ResolveMediumForceScale to dampen this frame's own
        // patrol/walk motor force by, below. body as Body2D is null for a non-Body2D IPhysicsBody
        // (none currently exist, but the cast stays defensive); such a body simply never receives
        // any medium-based scaling/force below.
        var mediumBody = body as Body2D;
        var (medium, submergedFraction) = mediumBody is not null
            ? ResolveCurrentMedium(world, body)
            : (Assets.MaterialLibrary.Undefined, 0.0);
        if (mediumBody is not null)
        {
            mediumBody.CurrentMedium = medium;
        }

        // Patrolling bodies (see IPatrolBody) contribute their own horizontal force here,
        // recomputed each frame from their current position relative to their patrol bounds -
        // this is the "future force source" extension point this comment used to describe before
        // one actually existed; any further force source (wind, thrust, etc.) would sum in here
        // the same way, alongside gravity, before the single acceleration/integration step below.
        // Scaled by ResolveMediumForceScale (see its own doc comment) so a patrolling body's own
        // "muscle power" is dampened the same way the player's walk/jump forces are while immersed
        // in a viscous medium - only meaningful for a body that is also IMediumAffected, since an
        // unaffected body's CurrentMedium is still resolved above but never meant to influence it.
        if (body is IPatrolBody patrolBody)
        {
            patrolBody.UpdatePatrolDirection(world.Gravity);
            var patrolForce = patrolBody.PatrolForce;
            if (mediumBody is not null && body is IMediumAffected { MediumAffected: true })
            {
                var patrolMediumScale = ResolveMediumForceScale(medium);
                patrolForce = new Vector2D(patrolForce.X * patrolMediumScale, patrolForce.Y * patrolMediumScale);
            }
            netForce += patrolForce;
        }

        // The player's ground-level walk/crawl (and climb/hang side-step) locomotion contributes
        // its own horizontal motor force here - see IWalkForceBody and UpdateWalkForce, called
        // earlier this frame by Step before this integration runs. UpdateWalkForce already applies
        // its own medium scaling using the player's CurrentMedium as of the *previous* frame's
        // resolution (the only value available at the point in Step it runs); the small one-frame
        // lag this implies is inconsequential given the medium rarely changes frame-to-frame.
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
            // Coulomb friction (which only acts at contact time).
            netForce.X -= medium.Viscosity * body.Size.Y * body.Velocity.X * Math.Abs(body.Velocity.X) * submergedFraction;
            netForce.Y -= medium.Viscosity * body.Size.X * body.Velocity.Y * Math.Abs(body.Velocity.Y) * submergedFraction;
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
