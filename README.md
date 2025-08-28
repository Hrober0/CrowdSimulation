# Crowd simulation for Unity

This project is a **Burst-accelerated navigation mesh (NavMesh) system for Unity**, designed with **RTS-style crowd simulation** in mind.  
Unlike Unity’s built-in NavMesh, this implementation focuses on **fast dynamic updates**, **low GC pressure**, and **scalable pathfinding**.

## What is a NavMesh?

A **navigation mesh (NavMesh)** is a collection of polygons (usually triangles) that defines the walkable areas in a game world.  
It enables agents (units, NPCs, crowds) to find paths around obstacles and reach their targets efficiently.

In real-time strategy and large-scale simulations, traditional NavMesh systems often struggle with performance when updating or handling hundreds of units.  
This project tackles that problem head-on.

## Visulization
- [Navmesh update](https://youtu.be/uCZhevX9qrY?si=YffUNqXb-7onxPsE)

## Project Structure

The project is split into tree main parts:

### 1. Navigation
- Core **NavMesh generation and updates**  
- **Node-based pathfinding** with smart links  
- Optimized with **Unity Burst** for high performance

### 2. Avoidance
- Job-based implementation of **RVO2 (Reciprocal Velocity Obstacles)**  [RVO2](https://gamma.cs.unc.edu/RVO2)
- Real-time **crowd avoidance** to prevent unit collisions  
- Scales well for **hundreds of agents moving simultaneously**

### 3. Crowd Simulation
- Integration of navigation + avoidance + attacks  
- Designed for **RTS-style games**, swarms, and large agent counts

## Tech Highlights
- Built with **DOTS** (Entities + Jobs + Burst)
- Use burst triangulation by [andywiecko](https://github.com/andywiecko/BurstTriangulator)
