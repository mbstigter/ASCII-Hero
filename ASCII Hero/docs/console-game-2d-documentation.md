# ConsoleGame2D Documentation

## Table of Contents
- [Overall Design](#overall-design)
- [Core Systems](#core-systems)
  - [Rendering System](#rendering-system)
  - [Game Components and Layering](#game-components-and-layering)
- [Physics System](#physics-system)
  - [Force Types](#force-types)
  - [Gravity Implementation](#gravity-implementation)
  - [Normal Force Implementation](#normal-force-implementation)
- [Environment System](#environment-system)
- [Collision System](#collision-system)
- [Creating Game Elements](#creating-game-elements)
  - [Creating Game Bodies](#creating-game-bodies)
  - [Creating Environment Zones](#creating-environment-zones)
- [Physics Behavior by Object Type](#physics-behavior-by-object-type)
- [Advanced Physics Features](#advanced-physics-features)
- [Conclusion](#conclusion)

## Overall Design

ConsoleGame2D is a 2D physics-based game engine that runs in the console window. It features a component-based architecture with a robust physics simulation, collision detection, environmental effects, and optimized console rendering.

The engine follows these core design principles:
- Separation of concerns with dedicated systems for physics, collisions, environments, and rendering
- Component-based game objects with inheritance hierarchies
- Frame-rate independent physics using elapsed time
- Character-based graphics with color support
- Viewport system with camera tracking

## Core Systems

### Rendering System

The rendering system uses a double-buffering approach to optimize console output:

The `ConsoleBuffer2D` class implements a buffered approach to console rendering. It maintains separate character, foreground color, and background color buffers, along with a "dirty" flag for each cell.

Key optimization features:
- Only cells marked as "dirty" (changed since last render) are redrawn to minimize console API calls
- Support for transparent colors to create layered effects
- Character-based representation with foreground and background colors
- Direct use of Windows console APIs for better performance than Console.Write methods

### Game Components and Layering

The game world consists of multiple layers rendered in back-to-front order:

1. **Background Objects**: Simple decorative elements like stars without physics
2. **Environment Zones**: Areas with specific environmental properties (air, water, etc.)
3. **Game Bodies**: Physical objects that interact with physics and collision systems
4. **Information Display**: HUD elements and debug information

#### Background Objects

Background objects are simple visual elements defined by position, character representation, and colors. They don't participate in physics simulations but are rendered as part of the scene background.

#### Environment Zones

Environment zones represent areas with specific physical properties:

Each environment is defined by properties like density (affecting buoyancy), viscosity (affecting drag), flow direction and strength, and visual appearance.

The engine provides factory methods for common environments like `CreateVacuum()`, `CreateAir()`, `CreateWater()`, `CreateWind()`, and `CreateRiver()`.

#### Game Bodies

All game bodies inherit from the `Body2D` base class and have properties like:

- Density: Affects mass and buoyancy
- Friction: Controls sliding behavior (0.0 = no friction, 1.0 = maximum friction)
- Bounciness: Controls rebound behavior (0.0 = no bounce, 1.0 = perfect bounce)
- Visual properties: Default character, foreground color, and background color

Types of game bodies:
- **Static Objects**: Immovable objects like platforms and walls
- **Dynamic Objects**: Objects affected by all physics forces
- **Kinematic Objects**: Objects that move according to predefined paths
- **Player**: User-controlled character with special movement abilities
- **Moving Enemies**: AI-controlled characters that patrol or chase
- **Static Enemies**: Non-moving hazards
- **Collectables**: Items the player can gather

## Physics System

### Force Types

The physics system handles several types of forces:

1. **Environmental Forces**:
   - Gravity: A constant downward force (GRAVITY = 9.81f)
   - Buoyancy: Upward force proportional to submerged volume and environment density
   - Drag: Resistance force proportional to velocity squared and environment viscosity
   - Flow: Directional force from wind or water currents

2. **Body-Specific Forces**:
   - Movement forces: Applied in response to user input with direction and power parameters
   - Jump forces: Vertical impulses with strength varying by context (ground, hanging, water)
   - Persistent forces: Forces that remain active until changed (thrusters, magnetic attractions, buoyancy)

3. **Collision Forces**:
   - Normal force: Counteracts penetration during collisions
   - Impulse force: Calculated based on relative velocity, mass, and material bounciness to resolve collisions
   - Friction force: Applied tangentially to collision surface based on relative velocity and material friction coefficient

Forces can be categorized by duration:
- **Impulse Forces**: Applied instantaneously (e.g., collisions, jumps)
- **Persistent Forces**: Applied continuously until removed (e.g., gravity, buoyancy, thrusters)

And by contact type:
- **Contact Forces**: Require physical contact (e.g., normal force, friction)
- **Non-Contact Forces**: Act at a distance (e.g., gravity, magnetic forces)

### Gravity Implementation

Gravity is implemented as a constant downward force proportional to an object's mass (F = mg). It's applied in the `ApplyEnvironmentalForces` method of the `Body2D` class unless the object is in contact with the ground.

### Normal Force Implementation

When an object is in contact with a surface, a normal force is applied that exactly counters gravity, preventing the object from penetrating the surface. This is implemented in the `ApplyEnvironmentalForces` method.

## Environment System

The environment system manages how objects interact with different environments like air, water, or custom zones:

1. **Environment Detection**:
   - For each body, calculate overlap with environment zones
   - Apply default environment (usually air) if no other environment applies

2. **Effect Application**:
   - For each overlap, apply environmental effects proportional to overlap percentage
   - Effects include buoyancy, drag, and flow forces

3. **State Tracking**:
   - Provide methods to query which environments an object is in and their percentages

## Collision System

The collision system handles detecting and resolving collisions between game objects:

1. **Broad Phase**:
   - Quick AABB (Axis-Aligned Bounding Box) tests to identify potential collisions

2. **Narrow Phase**:
   - Calculate overlap region between objects
   - Check for character-level collisions within overlap region
   - Determine collision normal and penetration depth

3. **Contact Update**:
   - Update contact states based on collision normals (top, bottom, left, right)

4. **Collision Resolution**:
   - Position correction to prevent objects from sticking together
   - Calculate relative velocity and impact speed
   - Calculate restitution (bounciness) based on material properties and impact speed
   - Apply impulse forces to resolve the collision
   - Apply friction forces based on material properties

## Creating Game Elements

### Creating Game Bodies

There are multiple ways to create game bodies:

1. **Using Factory Methods**:
   ```csharp
   // Create a brick platform
   StaticObject2D platform = new StaticObject2D(
       name: "Platform1",
       position: new Vector2D(5, 16),
       width: 40,
       height: 2,
       properties: BodyProperties2D.CreateBrick()
   );
   ```

2. **With Custom Properties**:
   ```csharp
   // Create a custom object with specific properties
   DynamicObject2D customBall = new DynamicObject2D(
       name: "CustomBall",
       position: new Vector2D(10, 5),
       properties: new BodyProperties2D(
           density: 1200f,
           friction: 0.5f,
           bounciness: 0.7f
       )
   );
   ```

3. **With Visual Overrides**:
   ```csharp
   // Create an object with custom visual appearance
   StaticObject2D customWall = new StaticObject2D(
       name: "Wall1",
       position: new Vector2D(35, 19),
       width: 2,
       height: 3,
       properties: BodyProperties2D.CreateBrick(),
       overrideChar: 'X',
       overrideForeColor: ConsoleColor.Yellow,
       overrideBackColor: ConsoleColor.Blue
   );
   ```

4. **With Initial Forces**:
   ```csharp
   // Create a dynamic object with initial velocity and persistent forces
   DynamicObject2D projectile = new DynamicObject2D(
       name: "Projectile1",
       position: new Vector2D(5, 5),
       properties: BodyProperties2D.CreateDefault(),
       persistentForce: new Vector2D(0, -9.81f), // Anti-gravity
       initialMovementForce: new Vector2D(2000, -1000) // Initial launch
   );
   ```

### Creating Environment Zones

Environment zones can be created using factory methods or custom properties:

```csharp
// Create a water pool using factory method
Environment2D waterPool = new Environment2D(
    name: "WaterPool",
    position: new Vector2D(20, 50),
    width: 40,
    height: 10,
    properties: EnvironmentProperties2D.CreateWater()
);

// Create a wind zone with custom properties
Environment2D windZone = new Environment2D(
    name: "WindZone",
    position: new Vector2D(0, 0),
    width: 80,
    height: 20,
    properties: EnvironmentProperties2D.CreateWind(
        direction: new Vector2D(-1, 0),
        strength: 2.0f
    )
);
```

## Physics Behavior by Object Type

Different types of game bodies have different physics behaviors:

1. **Static Objects** (`StaticObject2D`):
   - Not affected by any forces or physics
   - Used for platforms, walls, and other immovable elements
   - Can still cause collisions with other objects

2. **Dynamic Objects** (`DynamicObject2D`):
   - Fully affected by physics, including gravity, collisions, and environmental forces
   - Can have initial velocity and persistent forces
   - Typically used for projectiles, debris, and other non-controlled moving objects

3. **Kinematic Objects** (`KinematicObject2D`):
   - Move along predefined paths between specified points
   - Not affected by physics forces but can cause collisions
   - Used for moving platforms, elevators, and other predetermined movement patterns

4. **Player** (`Player2D`):
   - Controlled by user input with specialized movement abilities
   - Affected by all physics, plus additional "muscle forces" for movement
   - Has special states like walking, jumping, crawling, and hanging
   - Can apply movement forces with different strengths based on state

5. **Moving Enemies** (`MovingEnemy2D`):
   - AI-controlled with patrol behavior between points
   - Affected by physics but with simplified movement patterns
   - Can be either ground-based or flying based on persistent forces

## Advanced Physics Features

The engine includes several advanced physics features:

1. **Velocity-Dependent Bounciness**:
   - Objects become less bouncy at higher impact speeds, simulating energy loss

2. **Rest Detection**:
   - Objects below a velocity threshold receive extra damping to come to rest faster

3. **Drag Based on Speed**:
   - Drag forces increase with the square of velocity
   - Surface contacts increase drag coefficients

4. **Mixed Material Collisions**:
   - Collision elasticity is calculated based on the ratio of material properties

5. **Character-Level Collision**:
   - Collision detection includes checking character overlaps, not just bounding boxes

## Conclusion

ConsoleGame2D provides a robust foundation for creating physics-based games in the console environment. The engine's component-based architecture allows for easy extension and customization, while its optimized rendering and physics systems enable smooth gameplay even with complex scenes.

The separation of environment, collision, and physics systems allows for a wide range of game mechanics from platformers to puzzle games. The various body types and force models provide flexibility in creating diverse game elements with different behaviors.
