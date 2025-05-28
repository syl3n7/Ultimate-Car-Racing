# User Interface Scripts

This directory contains all user interface related scripts for the Ultimate Car Racing game.

## Files

### UIManager.cs
- **Purpose**: Main UI management and coordination
- **Features**: 
  - Central UI state management
  - Menu navigation
  - UI element coordination
- **Usage**: Attach to a main UI GameObject

### LoadingScreenManager.cs
- **Purpose**: Loading screen functionality and management
- **Features**:
  - Loading progress display
  - Scene transition handling
  - Loading state management
- **Usage**: Attach to loading screen GameObject

### LoadingScreenSetup.cs
- **Purpose**: Initial setup and configuration for loading screens
- **Features**:
  - Loading screen initialization
  - Progress bar setup
  - Animation configuration
- **Usage**: Attach to loading screen setup GameObject

### RoomListItem.cs
- **Purpose**: Individual room list item representation
- **Features**:
  - Room information display
  - Room selection handling
  - Dynamic room list updates
- **Usage**: Attach to room list item prefabs

## Structure

These scripts work together to provide a complete UI system:
1. UIManager orchestrates overall UI behavior
2. LoadingScreenManager handles transitions between scenes
3. LoadingScreenSetup configures loading screen appearance
4. RoomListItem manages multiplayer room listings

## Dependencies

These scripts may depend on:
- Unity UI system (Canvas, Button, Text, etc.)
- NetworkManager for multiplayer functionality
- Scene management for loading screens
