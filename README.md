# Ultimate Car Racing

![Game Server](https://img.shields.io/badge/Game%20Server-Racing-brightgreen)
![Unity](https://img.shields.io/badge/Unity-2021.3%2B-blue)
![License](https://img.shields.io/badge/License-MIT-yellow)

## Overview

Ultimate Car Racing is a multiplayer racing game built with Unity that allows players to race against each other in real-time. The game uses a client-server architecture with a dedicated relay server to handle both reliable (TCP) and fast, frequent (UDP) data, ensuring smooth gameplay even under varying network conditions.

## Features

- **Multiplayer Racing**: Compete in real-time races against friends or other online players.
- **Lobby and Room System**: Create, join, and manage game rooms in a dedicated lobby.
- **Physics-Based Driving**: Experience realistic car physics with acceleration, braking, and responsive steering.
- **Network Synchronization**: Smooth interpolation of player positions, rotations, and inputs using state and input data.
- **Spawn & Recovery**: Intelligent spawn point management with recovery mechanisms for missing players.
- **Latency Monitoring**: On-screen network latency display and debug info to track connection health.
- **Multi-Platform Support**: Play on Windows, macOS, or Linux.

## Getting Started

### Prerequisites

- **Unity**: Version 2021.3 or higher.
- **.NET Framework**: 4.7.2 or higher.
- A relay server for networked play (can run locally for testing).

### Installation

1. **Clone the Repository**
   ```bash
   git clone https://github.com/yourusername/Ultimate-Car-Racing.git
   ```
2. **Open the Project in Unity**
   - Launch Unity Hub and open the cloned project.

3. **Network Configuration**
   - Verify the relay server settings in `NetworkManager.cs` (IP address, TCP/UDP ports) to match your network setup.

4. **Run the Game**
   - Open the starting scene (typically the lobby) and play.

## How to Play

1. **Lobby System**
   - Enter your player name.
   - Host a new game room or join an existing one from the lobby.
   - Once in the room, wait for the host to start the race.

2. **In-Game Controls**
   - **Accelerate/Reverse**: W / Up Arrow, S / Down Arrow.
   - **Steer**: A / Left Arrow, D / Right Arrow.
   - **Brake/Handbrake**: Spacebar.

3. **Race Objectives**
   - Finish laps in the best time while managing realistic physics and avoiding obstacles.

## Network Architecture

- **TCP Communication**:  
  Used for reliable data transfers such as lobby management, chat, and game state updates.
  
- **UDP Communication**:  
  Handles frequent updates like player positions, rotations, and input synchronization.
  
- **Heartbeat & Ping**:  
  Periodic heartbeat and ping messages monitor connection health and adjust for latency.

- **State Interpolation & Prediction**:  
  Remote player positions and inputs are interpolated for smooth gameplay, reducing the visual impact of network latency.

## Development

### Project Structure

- **Assets/Scripts**
  - **GameBootstrap.cs**: Bootstraps key systems by instantiating network and scene managers.
  - **NetworkManager.cs**: Manages TCP/UDP connections, message processing, and latency measurement.
  - **UnityMainThreadDispatcher.cs**: Safely queues actions for execution on Unity's main thread.
  - **SceneTransitionManager.cs**: Asynchronously loads scenes with a loading screen and ensures necessary objects (e.g., GameManager) are instantiated.
  - **PlayerController.cs**: Controls car physics, player input, synchronization, and manages camera activation.
  - **SerializedData.cs**: Provides serializable data structures (e.g., `SerializableVector3`) for network communication.
  - **LobbyController.cs**: Handles lobby UI, server list management, room creation, joining, and network messaging.
  - **GameManager.cs**: Coordinates player spawning, state and input synchronization, and game event handling.
  - **NetworkLatencyDisplay.cs**: Displays current network latency with color-coded indicators for debugging.
  - **ServerListEntry.cs**: Represents individual game room entries in the lobby UI.

### Code Highlights

- **Networking & Synchronization**
  - The game uses separate TCP and UDP channels to balance reliability and speed.
  - `PlayerController.cs` implements interpolation (with teleportation fallback) to handle significant desyncs.
- **Scene Management**
  - `SceneTransitionManager.cs` ensures smooth scene transitions with a minimum loading time and proper cleanup.
- **Spawning & Recovery**
  - `GameManager.cs` dynamically spawns players at designated spawn points or calculated fallback positions.
  - Built-in recovery mechanisms re-spawn missing players (e.g., via key commands for testing).
- **Debug Tools**
  - On-screen network statistics and detailed logging in various scripts assist with debugging and performance tuning.

## Future Improvements

- Additional car models, customization options, and track designs.
- Enhanced AI opponents and dynamic environmental challenges.
- Expanded analytics and debugging tools.
- Support for mobile platforms.

## Contributing

Contributions are welcome! To contribute:

1. Fork the repository.
2. Create your feature branch (`git checkout -b feature/your-feature`).
3. Commit your changes (`git commit -m 'Add user-friendly feature'`).
4. Push to your branch (`git push origin feature/your-feature`).
5. Open a Pull Request with a clear description of your changes.

## License

This project is licensed under the [MIT License](LICENSE).

## Support

For bugs, feature requests, or other issues, please open an issue on the GitHub repository.