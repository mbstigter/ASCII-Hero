using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Client.Game.Physics;

/// <summary>
/// Puts groups of touching bodies to sleep once all of them have been at rest for a while, and
/// wakes them again when anything disturbs the group. A group ("island") is every finite-mass body
/// connected through surface contacts; static terrain does not connect islands, so two separate
/// stacks on the same floor sleep and wake independently. An island only sleeps as a whole, so a
/// body is never frozen while something it touches is still moving, and it only sleeps if every
/// member is passive (<see cref="Body2D.CanSleep"/>) and none rests on a moving surface.
/// </summary>
/// <remarks>
/// Run once per physics step, at the end of <see cref="CollisionSystem.Resolve"/>, so it sees the
/// step's final velocities and freshly resolved contacts. A sleeping body keeps its last contacts,
/// so it still reports grounded and still links its island. It is woken by: an awake body making
/// contact with it (see <see cref="CollisionSystem"/>), any velocity applied to it from outside
/// (see <see cref="PhysicsSystem"/>), removal of a body it touches (see
/// <see cref="World2D.ApplyPendingRemovals"/>), its support starting to move, or any other member
/// of its island waking.
/// </remarks>
public class SleepSystem
{
    private readonly List<Body2D> _bodies = [];
    private readonly Dictionary<Body2D, int> _indices = new(ReferenceEqualityComparer.Instance);
    private readonly List<int> _parents = [];
    private readonly Dictionary<int, bool> _islandCanSleep = [];

    private static readonly ContactType[] SurfaceContactTypes =
        [ContactType.SurfaceTop, ContactType.SurfaceBottom, ContactType.SurfaceLeft, ContactType.SurfaceRight];

    /// <summary>Updates rest timers and puts to sleep or wakes every island for one step of <paramref name="stepSeconds"/>.</summary>
    public void Update(IReadOnlyList<IPhysicsBody> movingBodies, double stepSeconds)
    {
        _bodies.Clear();
        _indices.Clear();
        _parents.Clear();
        _islandCanSleep.Clear();

        foreach (var movingBody in movingBodies)
        {
            var body = (Body2D)movingBody;
            _indices[body] = _bodies.Count;
            _bodies.Add(body);
            _parents.Add(_parents.Count);
            UpdateRestTime(body, movingBody, stepSeconds);
        }

        for (var i = 0; i < _bodies.Count; i++)
        {
            foreach (var type in SurfaceContactTypes)
            {
                foreach (var other in _bodies[i].GetContactingBodies(type))
                {
                    if (_indices.TryGetValue(other, out var otherIndex))
                    {
                        Union(i, otherIndex);
                    }
                }
            }
        }

        for (var i = 0; i < _bodies.Count; i++)
        {
            var root = Find(i);
            var canSleep = _islandCanSleep.GetValueOrDefault(root, true);
            _islandCanSleep[root] = canSleep && IsReadyToSleep(_bodies[i]);
        }

        for (var i = 0; i < _bodies.Count; i++)
        {
            var body = _bodies[i];
            if (_islandCanSleep[Find(i)])
            {
                if (!body.IsSleeping)
                {
                    body.Sleep();
                }
            }
            else if (body.IsSleeping)
            {
                body.Wake();
            }
        }
    }

    /// <summary>
    /// Accumulates how long an awake, passive body has stayed put. Rest is judged by actual
    /// displacement from where the rest period began, not by the body's velocity alone: in a stack
    /// the iterative solver leaves a small residual velocity that the contacts cancel again every
    /// step, so a movable object held perfectly still can still report one. Measuring against a fixed
    /// anchor (rather than step-to-step motion) also catches a slow creep that never exceeds the
    /// tolerance in any single step. A velocity above the sleep threshold still counts as motion,
    /// so a body momentarily stationary at the top of a bounce or toss is never put to sleep.
    /// </summary>
    private static void UpdateRestTime(Body2D body, IPhysicsBody movingBody, double stepSeconds)
    {
        if (body.IsSleeping)
        {
            return;
        }

        var velocity = movingBody.Velocity;
        var speedSquared = velocity.X * velocity.X + velocity.Y * velocity.Y;
        var dx = body.Position.X - body.RestAnchor.X;
        var dy = body.Position.Y - body.RestAnchor.Y;
        var speedLimit = PhysicsConstants.SleepVelocityThreshold;
        var distanceLimit = PhysicsConstants.ContactSlopCells;
        if (body.CanSleep && body.RestSeconds > 0.0
            && speedSquared <= speedLimit * speedLimit
            && dx * dx + dy * dy <= distanceLimit * distanceLimit)
        {
            body.RestSeconds += stepSeconds;
            return;
        }

        body.RestAnchor = body.Position;
        body.RestSeconds = body.CanSleep && speedSquared <= speedLimit * speedLimit ? stepSeconds : 0.0;
    }

    /// <summary>
    /// Whether this one body would allow its island to sleep: it is asleep already, or passive and
    /// at rest long enough, and in both cases not touching a moving surface.
    /// </summary>
    private static bool IsReadyToSleep(Body2D body)
    {
        if (!body.CanSleep)
        {
            return false;
        }

        if (!body.IsSleeping && body.RestSeconds < PhysicsConstants.TimeToSleepSeconds)
        {
            return false;
        }

        foreach (var type in SurfaceContactTypes)
        {
            foreach (var other in body.GetContactingBodies(type))
            {
                // A moving platform or conveyor keeps whatever touches it in motion (or about to
                // be), even at a moment when it happens to be standing still.
                if (other.IsStatic && (other is IPhysicsBody || other.ConveyorVelocity.X != 0.0 || other.ConveyorVelocity.Y != 0.0))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private int Find(int index)
    {
        while (_parents[index] != index)
        {
            _parents[index] = _parents[_parents[index]];
            index = _parents[index];
        }

        return index;
    }

    private void Union(int a, int b)
    {
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA != rootB)
        {
            _parents[rootB] = rootA;
        }
    }
}
