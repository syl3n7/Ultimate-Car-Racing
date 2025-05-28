# Core Game Scripts

This directory contains the core gameplay scripts that handle the fundamental game mechanics.

## Files

### GameManager.cs
- **Purpose**: Central game state management and coordination
- **Features**:
  - Game state control (menu, playing, paused, etc.)
  - Player management
  - Game session coordination
  - Score and statistics tracking
- **Usage**: Attach to a main GameManager GameObject (usually one per scene)

### CarController.cs
- **Purpose**: Local player car physics and control
- **Features**:
  - Vehicle physics simulation
  - Input handling (acceleration, braking, steering)
  - Car behavior and dynamics
  - Local player movement
- **Usage**: Attach to player car GameObject

### CameraFollow.cs
- **Purpose**: Camera system that follows the player
- **Features**:
  - Smooth camera following
  - Camera positioning and rotation
  - Dynamic camera behavior
  - Player tracking
- **Usage**: Attach to camera GameObject

### RemotePlayerController.cs
- **Purpose**: Remote player car representation and synchronization
- **Features**:
  - Network position synchronization
  - Remote player movement interpolation
  - Multiplayer car representation
  - Network state handling
- **Usage**: Attach to remote player car GameObjects

## Architecture

These scripts form the core gameplay loop:
1. **GameManager** orchestrates the overall game state
2. **CarController** handles local player input and physics
3. **CameraFollow** provides smooth camera experience
4. **RemotePlayerController** synchronizes multiplayer players

## Dependencies

- Unity Physics system for car dynamics
- Input System for player controls
- Network system for multiplayer functionality
- Transform system for positioning and movement
