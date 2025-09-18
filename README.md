# Crowd simulation for Unity

A **Burst-optimized** agent navigation system built for RTS-style crowd simulation.
Unlike Unity’s built-in AI Navigation, this solution is designed for **scalable pathfinding**, **dynamic updates**, and **low memory overhead** — enabling hundreds of agents to move fluidly in real time.

## What is wrong with Unit's navigation system?
Unity’s navigation system works for basic AI, but it has key drawbacks for large-scale crowd simulation:

1. NavMesh updates are costly
- Unity’s NavMesh is static by default.
- Updating it requires expensive full or partial rebakes.
- Raycast-based queries are not efficient at runtime.

2. Limited path modifiers
- Only supports cost multipliers per agent type.
- Changing the walkable area requires placing obstacles, which affect all NavMesh layers, not just the one you want.

3. Not built for large crowds
- Lacks fine control over group movement.
- Performance degrades significantly with many agents.

## Design goals
- **Performance-first** — Fully Burst-compiled for SIMD acceleration.
- **Modular** — Swap in new movement systems or pathfinding heuristics easily.
- **Flexible NavMesh** — Support for efficient local updates without rebaking the entire map.
- **Crowd-aware** — Scales gracefully to hundreds (or thousands) of agents.
- **Customizable** — Extend behavior via generic **node attributes** and **path seeker**.

## Visulization
- [Navmesh update explanation](https://youtu.be/uCZhevX9qrY?si=YffUNqXb-7onxPsE)
- [Navmesh update performance test](https://www.youtube.com/watch?v=FILGhMOsSlo)
- [Pathfinding on Navmesh explained](https://www.youtube.com/watch?v=Et-tu-1pM7k)

## Project Structure

The project is split into tree main parts:

### 1. Navigation
- **NavMesh** is build with triangle **NavNodes** covering it all area
- **NavNodes** are connected by common edges, creates a graph for A* pathfining
- **NavNodes** have generic **attributes** to be use for evaluate a cost of path by generic **seeker**
- **NavObstacle** is a collection of polygons with **attributes**
- **NavMesh** can be updated at given area to bake **attributes** and shape of **NavObstacle** on that area

### 2. Avoidance
- Real-time **crowd avoidance** to prevent agents collisions
- Agents generting avoidance vectors to avoid contact before it heppend
- Supports both **dynamic** circle agents and **static** polygon obstacles
- Built on a job-based implementation of **RVO2 (Reciprocal Velocity Obstacles)**  [RVO2](https://gamma.cs.unc.edu/RVO2)

### 3. Crowd Simulation
- Seamless integration of navigation and avoidance into a unified simulation loop.
- Efficient querying of agents and obstacles for decision-making.
- High-level APIs to order agents to move as individuals or groups.

## Tech Highlights
- Unity 6.2
- Built with **DOTS** (Entities + Jobs + Burst)
- Use burst triangulation by [andywiecko](https://github.com/andywiecko/BurstTriangulator)
