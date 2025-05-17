# MP-Server Protocol Documentation

## 1. Overview
MP-Server is a simple TCP/UDP racing‐game server.  
Clients connect over TCP (for commands, room management, chat) and send/receive UDP packets (for real‐time position updates).

Ports (defaults):
- TCP: 7777
- UDP: 7778

## 2. Prerequisites
- .NET 9.0 runtime
- A TCP‐capable socket library (e.g. `System.Net.Sockets.TcpClient` in .NET, `net` module in Node.js, or BSD sockets in C/C++)
- A UDP socket library for state updates
- UTF-8 support for text commands
- JSON parser (server uses System.Text.Json for all command processing)

## 3. TCP Protocol

### 3.1 Connection & Framing
1. Client opens a TCP connection to server:  
   `tcpClient.Connect("server.address", 7777)`
2. Server immediately responds with a welcome message terminated by `\n`:  
   ```
   CONNECTED|<sessionId>\n
   ```
3. All subsequent messages are **newline‐delimited JSON**:
   ```
   {"command":"COMMAND_NAME","param1":"value1","param2":"value2"}\n
   ```

### 3.2 Supported Commands

| Command        | Direction    | Payload (JSON)                                    | Response (JSON)                         |
| -------------- | ------------ | ------------------------------------------------- | --------------------------------------- |
| `NAME`         | Client → Srv | `{"command":"NAME","name":"playerName"}`        | `{"command":"NAME_OK","name":"playerName"}` |
| `CREATE_ROOM`  | Client → Srv | `{"command":"CREATE_ROOM","name":"roomName"}`   | `{"command":"ROOM_CREATED","roomId":"id","name":"roomName"}` |
| `JOIN_ROOM`    | Client → Srv | `{"command":"JOIN_ROOM","roomId":"id"}`         | `{"command":"JOIN_OK","roomId":"id"}` or `{"command":"ERROR","message":"Failed to join room. Room may be full or inactive."}` |
| `LEAVE_ROOM`   | Client → Srv | `{"command":"LEAVE_ROOM"}`                      | `{"command":"LEAVE_OK","roomId":"id"}` or `{"command":"ERROR","message":"Cannot leave room. No room joined."}` |
| `PING`         | Client → Srv | `{"command":"PING"}`                            | `{"command":"PONG"}`                  |
| `LIST_ROOMS`   | Client → Srv | `{"command":"LIST_ROOMS"}`                     | `{"command":"ROOM_LIST","rooms":[{"id":"id","name":"roomName","playerCount":0,"isActive":false}]}` |
| `GET_ROOM_PLAYERS` | Client → Srv | `{"command":"GET_ROOM_PLAYERS"}`          | `{"command":"ROOM_PLAYERS","roomId":"id","players":[{"id":"playerId","name":"playerName"}]}` or `{"command":"ERROR","message":"Cannot get players. No room joined."}` |
| `RELAY_MESSAGE` | Client → Srv | `{"command":"RELAY_MESSAGE","targetId":"playerId","message":"text"}` | `{"command":"RELAY_OK","targetId":"playerId"}` or `{"command":"ERROR","message":"Target player not found."}` |
| `PLAYER_INFO`  | Client → Srv | `{"command":"PLAYER_INFO"}`                    | `{"command":"PLAYER_INFO","playerInfo":{"id":"id","name":"playerName","currentRoomId":"roomId"}}` |
| `START_GAME`   | Client → Srv | `{"command":"START_GAME"}`                     | `{"command":"GAME_STARTED","roomId":"roomId"}` or `{"command":"ERROR","message":"Cannot start game. Only the host can start the game."}` |
| `BYE`          | Client → Srv | `{"command":"BYE"}`                            | `{"command":"BYE_OK"}` |
| Any other      | Client → Srv | e.g. `{"command":"FOO"}`                        | `{"command":"UNKNOWN_COMMAND","originalCommand":"FOO"}` |

#### Server-to-Client Messages
The server may also send these messages without a direct client request:

| Message | Purpose | Format |
| ------- | ------- | ------ |
| `RELAYED_MESSAGE` | Message relayed from another player | `{"command":"RELAYED_MESSAGE","senderId":"id","senderName":"name","message":"text"}` |

#### Error Handling
- Malformed JSON commands return `{"command":"ERROR","message":"Invalid JSON format"}`.
- Unrecognized commands return `{"command":"UNKNOWN_COMMAND","originalCommand":"cmd"}`.
- If server detects inactivity (>60 s without messages), it will close the TCP socket.
- Common error messages include:
  - `"Room not found."`
  - `"Cannot start game. No room joined."`
  - `"Cannot start game. Room not found."`
  - `"Cannot start game. You are not in this room."`
  - `"Cannot start game. Only the host can start the game."`
  - `"Cannot leave room. No room joined."`
  - `"Cannot leave room. Room not found."`
  - `"Cannot leave room. You are not in this room."`
  - `"Invalid message relay request. Missing target or message."`
  - `"Target player not found."`

## 4. UDP Protocol

### 4.1 Purpose
The UDP protocol provides low-latency communication for:
- Position and rotation updates
- Input control commands (steering, throttle, brake)
- Game state synchronization between clients

All UDP communication happens after the client has joined or created a room via TCP.

### 4.2 Packet Format (JSON-based)
The server supports two primary types of UDP packets, both based on JSON:

#### Position Updates
```json
{
  "command": "UPDATE",
  "sessionId": "id",
  "position": {"x": 0.0, "y": 0.0, "z": 0.0},
  "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0}
}
```

#### Input Controls
```json
{
  "command": "INPUT",
  "sessionId": "id",
  "roomId": "roomId",
  "input": {
    "steering": 0.0,
    "throttle": 0.0,
    "brake": 0.0,
    "timestamp": 123.456
  },
  "client_id": "id"
}
```

### 4.3 Required Fields

#### For Position Updates:
- `command`: Must be "UPDATE"
- `sessionId`: Your TCP session ID (received during connection)
- `position`: A Vector3 object with:
  - `x`: X coordinate (float)
  - `y`: Y coordinate (float)
  - `z`: Z coordinate (float)
- `rotation`: A Quaternion object with:
  - `x`: X component (float)
  - `y`: Y component (float)
  - `z`: Z component (float)
  - `w`: W component (float, default 1.0)

#### For Input Controls:
- `command`: Must be "INPUT"
- `sessionId`: Your TCP session ID (received during connection)
- `roomId`: The ID of the room you're currently in
- `input`: An object containing control values:
  - `steering`: Steering input (-1.0 to 1.0, where -1 is full left, 1 is full right)
  - `throttle`: Acceleration input (0.0 to 1.0)
  - `brake`: Brake input (0.0 to 1.0)
  - `timestamp`: Game time in seconds when this input was recorded
- `client_id`: Your client ID (should match sessionId)

### 4.4 UDP Communication Flow

1. **Endpoint Registration**:
   - The server automatically associates a client's UDP endpoint (IP:port) with their session ID
   - This mapping happens when the first UDP packet is received from a client
   - Each player must send at least one UDP packet to register their endpoint

2. **Broadcasting**:
   - Position updates are sent only to players in the same room
   - Input commands are broadcast to all clients in the room except the sender
   - The exact format of received messages matches what was sent (preserving all fields)
   - Players without a registered UDP endpoint won't receive any broadcasts

3. **Reliability**:
   - UDP does not guarantee delivery - clients should implement their own reliability mechanisms
   - High-frequency updates (like position) should be sent periodically regardless of changes
   - Input commands should be sent when input values change or at a consistent rate

### 4.5 Example (C# send)
```csharp
using var udp = new UdpClient();
var posUpdate = new { 
    command = "UPDATE", 
    sessionId = sessionId, 
    position = new { x = posX, y = posY, z = posZ },
    rotation = new { x = rotX, y = rotY, z = rotZ, w = rotW }
};
var json = JsonSerializer.Serialize(posUpdate) + "\n";
var bytes = Encoding.UTF8.GetBytes(json);
await udp.SendAsync(bytes, bytes.Length, serverHost, 7778);
```

### 4.6 Example (C# receive)
```csharp
using var udpClient = new UdpClient(localPort); // Local port to listen on
var endpoint = new IPEndPoint(IPAddress.Any, 0);

while (true)
{
    var result = await udpClient.ReceiveAsync();
    var json = Encoding.UTF8.GetString(result.Buffer);
    var update = JsonSerializer.Deserialize<JsonElement>(json);
    
    // Process by command type
    if (update.TryGetProperty("command", out var cmdElement))
    {
        string command = cmdElement.GetString();
        
        if (command == "UPDATE")
        {
            // Process position update
            var sessionId = update.GetProperty("sessionId").GetString();
            var position = update.GetProperty("position");
            var rotation = update.GetProperty("rotation");
            
            float posX = position.GetProperty("x").GetSingle();
            float posY = position.GetProperty("y").GetSingle();
            float posZ = position.GetProperty("z").GetSingle();
            
            float rotX = rotation.GetProperty("x").GetSingle();
            float rotY = rotation.GetProperty("y").GetSingle();
            float rotZ = rotation.GetProperty("z").GetSingle();
            float rotW = rotation.GetProperty("w").GetSingle();
            
            // Use position and rotation to update game state
            // ...
        }
        else if (command == "INPUT")
        {
            // Process input update
            var sessionId = update.GetProperty("sessionId").GetString();
            var input = update.GetProperty("input");
            
            float steering = input.GetProperty("steering").GetSingle();
            float throttle = input.GetProperty("throttle").GetSingle();
            float brake = input.GetProperty("brake").GetSingle();
            float timestamp = input.GetProperty("timestamp").GetSingle();
            
            // Apply input to the relevant player's vehicle
            // ...
        }
    }
}
```

### 4.7 Best Practices
- Send position updates at a consistent rate (10-30 Hz recommended)
- Keep UDP packet size under 1200 bytes to avoid fragmentation
- Handle networking jitter with client-side interpolation
- Include timestamps in input commands to help with lag compensation
- Apply basic smoothing or prediction for missing updates

## 5. Room Management

### 5.1 Room Properties
- Each room has a unique ID, name, and a host player
- Rooms have a maximum player limit (default: 20)
- Rooms can be active (game started) or inactive (lobby)
- Creation timestamp is recorded
- When the host leaves a room:
  - If the room is empty, it is automatically removed
  - If other players are present, host status is transferred to another player

### 5.2 Room Operations
- Create room: A player can create a new room and becomes its host
- Join room: Players can join rooms that are not active and not full
- Start game: Only the host can start the game, which marks the room as active
- List rooms: Get all available rooms with their basic information
- Get room players: Get the list of players in the current room

## 6. Example TCP Client (C#)

```csharp
using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class RacingClient
{
    private TcpClient _tcp;
    private NetworkStream _stream;
    private string _sessionId;

    public async Task RunAsync(string host, int port)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port);
        _stream = _tcp.GetStream();

        // Read welcome
        var reader = new StreamReader(_stream, Encoding.UTF8);
        var welcome = await reader.ReadLineAsync();
        Console.WriteLine(welcome); // "CONNECTED|<sessionId>"
        _sessionId = welcome.Split('|')[1];

        // Set name
        await SendJsonAsync(new { command = "NAME", name = "Speedy" });
        var response = await reader.ReadLineAsync();
        Console.WriteLine(response); // {"command":"NAME_OK","name":"Speedy"}

        // Create a room
        await SendJsonAsync(new { command = "CREATE_ROOM", name = "FastTrack" });
        response = await reader.ReadLineAsync();
        Console.WriteLine(response); // {"command":"ROOM_CREATED","roomId":"<id>","name":"FastTrack"}

        // ... then start your UDP updates …
    }

    private async Task SendJsonAsync<T>(T data)
    {
        var json = JsonSerializer.Serialize(data) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await _stream.WriteAsync(bytes, 0, bytes.Length);
    }
}
```

## 7. Player Session Management

### 7.1 Session Lifecycle
- Sessions are created when a client connects via TCP
- Each session has a unique ID that's shared with the client
- Inactivity timeout: Sessions with no activity for > 60 seconds are disconnected
- Sessions track player name, current room, and last activity time

### 7.2 Position Updates
- Player position and rotation are tracked via the PlayerInfo record
- Position is represented as Vector3 (x, y, z)
- Rotation is represented as Quaternion (x, y, z, w)

### 7.3 Message Relay
- Players can relay messages to other players using their session ID
- Messages are delivered to the target player via TCP
- The receiving player gets a `RELAYED_MESSAGE` command with the sender's ID, name, and message

## 8. Host Transfer Mechanism
When a host player leaves a room:
- If the room is empty, it is automatically removed
- If other players remain in the room, host status transfers to another player
- The new host has the authority to start the game
- No specific notification is sent when host status changes (clients should track this if needed)

## 9. Logging & Debug
- TCP events (connect, disconnect, commands) are logged at INFO level
- UDP packet receipt and processing are logged at DEBUG level
- JSON parsing errors are caught and logged
- Use console logger to trace flow:
  ```bash
  dotnet run --verbosity normal
  ```

## 10. Next Steps
- Add authentication/tokens for secure player identification
- Implement chat functionality
- Add race-specific features like lap counting and race timing
- Add server admin commands for room management
- Optimize UDP broadcast for large player counts
- Implement game state synchronization for deterministic physics

---

With this guide and the samples above, you can implement a client in your language of choice, manage TCP commands, and stream position updates over UDP. Happy racing!
