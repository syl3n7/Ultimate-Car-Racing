# Ultimate Car Racing - Server

![Game Server](https://img.shields.io/badge/Game%20Server-Racing-brightgreen)
![Python](https://img.shields.io/badge/Python-3.7%2B-blue)
![License](https://img.shields.io/badge/License-MIT-yellow)

## Overview

The Ultimate Car Racing Server is a high-performance networking server designed to facilitate multiplayer racing games. It provides both TCP and UDP communication channels for game clients, handling player connections, game room management, position synchronization, and message relaying between players.

## Features

- **Dual Protocol Support**: Simultaneous TCP (reliable) and UDP (fast) communication
- **Game Room Management**: Create, join, and manage multiplayer game rooms
- **Player Tracking**: Monitor player positions, latencies, and connection statuses
- **Admin Console**: Real-time server monitoring and management
- **Stress Testing**: Built-in tools for performance testing
- **Spawn Position Management**: Automatic assignment of starting positions

## Installation

### Prerequisites

- Python 3.7 or higher
- pip package manager

### Setup

1. Clone or download the repository
2. Install required dependencies:
   ```bash
   pip install tabulate
   ```

## Running the Server

Execute the server with:
```bash
python relay.py
```

The server will start listening on:
- TCP port 7777
- UDP port 7778

## Admin Console Commands

Once the server is running, you can use these commands in the admin console:

| Command       | Description                                  | Example                     |
|---------------|----------------------------------------------|-----------------------------|
| `players`     | List all connected players                  | `players`                   |
| `rooms`       | List all active game rooms                  | `rooms`                     |
| `kick`        | Kick a player                               | `kick client_5`             |
| `broadcast`   | Send message to all players                 | `broadcast Server restart!` |
| `stats`       | Show server statistics                      | `stats`                     |
| `resetpos`    | Reset a player's position                   | `resetpos client_3`         |
| `clear`       | Clear the console screen                    | `clear`                     |
| `exit`        | Shut down the server                        | `exit`                      |

## Client Protocol Documentation

### Message Types

#### TCP Messages

| Type                | Direction       | Description                                  |
|---------------------|-----------------|----------------------------------------------|
| `REGISTERED`        | Server → Client | Sent when client first connects             |
| `HEARTBEAT`         | Client → Server | Keep-alive message                          |
| `HEARTBEAT_ACK`     | Server → Client | Response to heartbeat                       |
| `HOST_GAME`         | Client → Server | Request to host a new game room             |
| `GAME_HOSTED`       | Server → Client | Confirmation of game hosting                |
| `LIST_GAMES`        | Client → Server | Request list of available rooms             |
| `GAME_LIST`         | Server → Client | Response with room list                     |
| `JOIN_GAME`         | Client → Server | Request to join a game room                 |
| `JOINED_GAME`       | Server → Client | Confirmation of successful room join        |
| `JOIN_FAILED`       | Server → Client | Notification of failed join attempt         |
| `PLAYER_JOINED`     | Server → Client | Notification of new player in room          |
| `RELAY_MESSAGE`     | Both            | Relay a message to other clients            |
| `PING`             | Client → Server | Measure latency                            |
| `PING_RESPONSE`    | Server → Client | Response to ping                           |
| `PLAYER_INFO`      | Client → Server | Send player name and info                  |
| `POSITION_UPDATE`  | Client → Server | Update player position                     |
| `LEAVE_ROOM`       | Client → Server | Leave current game room                    |
| `START_GAME`       | Client → Server | Host starts the game                       |
| `GAME_STARTED`     | Server → Client | Notification that game has started         |
| `PLAYER_DISCONNECTED` | Server → Client | Notification of player leaving            |
| `SERVER_MESSAGE`   | Server → Client | Broadcast message from admin               |
| `KICKED`           | Server → Client | Notification of being kicked               |
| `RESET_POSITION`   | Server → Client | Force player to reset position             |

#### UDP Messages

| Type                | Description                                  |
|---------------------|----------------------------------------------|
| `POSITION_UPDATE`  | Frequent position updates                   |
| `GAME_DATA`        | Game-specific data relay                    |

## Configuration

You can customize the server ports by modifying the constructor:

```python
server = RelayServer(tcp_port=7777, udp_port=7778)  # Change ports as needed
```

## Performance Considerations

- The server uses threading for concurrent client handling
- UDP is used for high-frequency position updates
- TCP is used for reliable game state synchronization
- Client timeouts are set to 60 seconds of inactivity

## Stress Testing

The server includes built-in stress testing tools:

1. Start a stress test:
   ```json
   {
     "type": "STRESS_TEST_START",
     "bot_count": 50
   }
   ```

2. Stop the stress test:
   ```json
   {
     "type": "STRESS_TEST_STOP"
   }
   ```

## License

This project is open-source and available for use under the MIT License.

## Support

For issues or feature requests, please open an issue on the project repository.
