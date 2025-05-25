# MP-Server Protocol Documentation

## 1. Overview
MP-Server is a secure TCP/UDP racing‐game server with TLS/SSL encryption.  
Clients connect over TLS-encrypted TCP (for commands, room management, chat) and send/receive encrypted UDP     await udp.SendAsync(bytes, bytes.Length, serverHost, 443);ackets (for real‐time position updates).

Ports (defaults):
- TCP: 443 (TLS/SSL encrypted) - Uses standard HTTPS port for firewall traversal
- UDP: 443 (AES encrypted for authenticated users) - Same port as TCP 
- Dashboard Web UI: 8080

## 1.1 Security Features
- **TLS/SSL TCP encryption**: All command traffic is encrypted using TLS 1.2/1.3
- **Self-signed certificate generation**: Server automatically generates certificates if none provided
- **UDP AES encryption**: Position and input data encrypted with session-specific keys
- **Player authentication**: Password-based authentication with hashed storage
- **Session isolation**: Each player gets unique encryption keys

## 2. Prerequisites
- .NET 9.0 runtime
- A TLS-capable TCP socket library
- A UDP socket library for state updates  
- UTF-8 support for text commands
- JSON parser (server uses System.Text.Json for all command processing)
- AES encryption support for UDP packets (authenticated users only)

## 3. TCP Protocol

### 3.1 Connection & Framing
1. Client opens a **TLS-encrypted** TCP connection to server:  
   ```csharp
   var client = new TcpClient();
   await client.ConnectAsync("server.address", 443);
   var sslStream = new SslStream(client.GetStream());
   await sslStream.AuthenticateAsClientAsync("server.address");
   ```
2. Server immediately responds with a welcome message terminated by `\n`:  
   ```
   CONNECTED|<sessionId>\n
   ```
3. All subsequent messages are **newline‐delimited JSON**:
   ```
   {"command":"COMMAND_NAME","param1":"value1","param2":"value2"}\n
   ```

**Important**: The server automatically generates and uses a self-signed certificate. For production use, clients should either:
- Accept self-signed certificates (for local/LAN use)
- Provide a proper CA-signed certificate to the server
- Implement certificate validation callbacks

### 3.2 Authentication
The server supports a simple authentication system to protect player identities:

1. **During Registration** (first time using a username):
   - When setting a player name for the first time, a password can be provided
   - Example: `{"command":"NAME","name":"playerName","password":"secretPassword"}`

2. **During Login** (using an existing username):
   - When connecting with a previously used name, password verification is required
   - Example: `{"command":"NAME","name":"playerName","password":"secretPassword"}`

3. **Separate Authentication**:
   - Players can also set their name first, then authenticate separately
   - Example: `{"command":"AUTHENTICATE","password":"secretPassword"}`

4. **Command Restrictions**:
   - Unauthenticated players can only use: NAME, AUTHENTICATE, PING, BYE, PLAYER_INFO, LIST_ROOMS
   - All other commands require authentication
   - Attempting restricted commands without auth returns an error

5. **UDP Encryption Setup**:
   - Once authenticated via TCP, players receive unique UDP encryption keys
   - UDP packets from authenticated players are automatically encrypted with AES-256
   - The server can handle both encrypted and plain-text UDP packets for backward compatibility

### 3.3 Supported Commands

| Command        | Direction    | Payload (JSON)                                    | Response (JSON)                         | Requires Auth |
| -------------- | ------------ | ------------------------------------------------- | --------------------------------------- | ------------- |
| `NAME`         | Client → Srv | `{"command":"NAME","name":"playerName","password":"secret"}` | `{"command":"NAME_OK","name":"playerName","authenticated":true,"udpEncryption":true}` or `{"command":"AUTH_FAILED","message":"Invalid password for this player name."}` | No |
| `AUTHENTICATE` | Client → Srv | `{"command":"AUTHENTICATE","password":"secret"}` | `{"command":"AUTH_OK","name":"playerName"}` or `{"command":"AUTH_FAILED","message":"Invalid password."}` | No |
| `CREATE_ROOM`  | Client → Srv | `{"command":"CREATE_ROOM","name":"roomName"}`   | `{"command":"ROOM_CREATED","roomId":"id","name":"roomName"}` | Yes |
| `JOIN_ROOM`    | Client → Srv | `{"command":"JOIN_ROOM","roomId":"id"}`         | `{"command":"JOIN_OK","roomId":"id"}` or `{"command":"ERROR","message":"Failed to join room. Room may be full or inactive."}` | Yes |
| `LEAVE_ROOM`   | Client → Srv | `{"command":"LEAVE_ROOM"}`                      | `{"command":"LEAVE_OK","roomId":"id"}` or `{"command":"ERROR","message":"Cannot leave room. No room joined."}` | Yes |
| `PING`         | Client → Srv | `{"command":"PING"}`                            | `{"command":"PONG"}`                  | No |
| `LIST_ROOMS`   | Client → Srv | `{"command":"LIST_ROOMS"}`                     | `{"command":"ROOM_LIST","rooms":[{"id":"id","name":"roomName","playerCount":0,"isActive":false,"hostId":"hostId"}]}` | No |
| `GET_ROOM_PLAYERS` | Client → Srv | `{"command":"GET_ROOM_PLAYERS"}`          | `{"command":"ROOM_PLAYERS","roomId":"id","players":[{"id":"playerId","name":"playerName"}]}` or `{"command":"ERROR","message":"Cannot get players. No room joined."}` | Yes |
| `RELAY_MESSAGE` | Client → Srv | `{"command":"RELAY_MESSAGE","targetId":"playerId","message":"text"}` | `{"command":"RELAY_OK","targetId":"playerId"}` or `{"command":"ERROR","message":"Target player not found."}` | Yes |
| `PLAYER_INFO`  | Client → Srv | `{"command":"PLAYER_INFO"}`                    | `{"command":"PLAYER_INFO","playerInfo":{"id":"id","name":"playerName","currentRoomId":"roomId"}}` | No |
| `START_GAME`   | Client → Srv | `{"command":"START_GAME"}`                     | `{"command":"GAME_STARTED","roomId":"roomId","spawnPositions":{"playerId1":{"x":66,"y":-2,"z":0.8},"playerId2":{"x":60,"y":-2,"z":0.8}}}` or `{"command":"ERROR","message":"Cannot start game. Only the host can start the game."}` | Yes |
| `BYE`          | Client → Srv | `{"command":"BYE"}`                            | `{"command":"BYE_OK"}` | No |
| Any other      | Client → Srv | e.g. `{"command":"FOO"}`                        | `{"command":"UNKNOWN_COMMAND","originalCommand":"FOO"}` | - |

#### Server-to-Client Messages
The server may also send these messages without a direct client request:

| Message | Purpose | Format |
| ------- | ------- | ------ |
| `RELAYED_MESSAGE` | Message relayed from another player | `{"command":"RELAYED_MESSAGE","senderId":"id","senderName":"name","message":"text"}` |
| `GAME_STARTED` | Notification that a game has started | `{"command":"GAME_STARTED","roomId":"roomId","hostId":"hostId","spawnPositions":{"playerId1":{"x":66,"y":-2,"z":0.8},"playerId2":{"x":60,"y":-2,"z":0.8}}}` |
| `AUTH_FAILED` | Authentication failure notification | `{"command":"AUTH_FAILED","message":"Invalid password for this player name."}` |

#### Error Handling
- Malformed JSON commands return `{"command":"ERROR","message":"Invalid JSON format"}`.
- Unrecognized commands return `{"command":"UNKNOWN_COMMAND","originalCommand":"cmd"}`.
- Authentication errors return `{"command":"ERROR","message":"Authentication required. Please use NAME command with password."}` or `{"command":"AUTH_FAILED","message":"Invalid password."}`.
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

### 4.2 Encryption
**For Authenticated Players:**
- UDP packets are automatically encrypted using AES-256-CBC
- Each session gets unique encryption keys derived from the session ID
- Packet format: `[4-byte length header][encrypted JSON data]`
- The server handles encryption/decryption transparently

**For Unauthenticated Players:**
- UDP packets are sent as plain-text JSON (backward compatibility)
- Limited functionality compared to authenticated users

### 4.3 Packet Format (JSON-based)
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

### 4.5 Example (C# send with encryption)
```csharp
// For authenticated players - packets are automatically encrypted
using var udp = new UdpClient();
var posUpdate = new { 
    command = "UPDATE", 
    sessionId = sessionId, 
    position = new { x = posX, y = posY, z = posZ },
    rotation = new { x = rotX, y = rotY, z = rotZ, w = rotW }
};

// If you have UDP encryption enabled (authenticated player)
if (udpCrypto != null)
{
    var encryptedPacket = udpCrypto.CreatePacket(posUpdate);
    await udp.SendAsync(encryptedPacket, encryptedPacket.Length, serverHost, 443);
}
else
{
    // Fallback to plain text for unauthenticated users
    var json = JsonSerializer.Serialize(posUpdate) + "\n";
    var bytes = Encoding.UTF8.GetBytes(json);
    await udp.SendAsync(bytes, bytes.Length, serverHost, 8443);
}
```

### 4.6 Example (C# receive with decryption)
```csharp
using var udpClient = new UdpClient(localPort); // Local port to listen on
var endpoint = new IPEndPoint(IPAddress.Any, 0);

while (true)
{
    var result = await udpClient.ReceiveAsync();
    var packetData = result.Buffer;
    
    JsonElement update;
    
    // Try to decrypt if this looks like an encrypted packet
    if (packetData.Length >= 4 && udpCrypto != null)
    {
        var parsedData = udpCrypto.ParsePacket<JsonElement>(packetData);
        if (parsedData.ValueKind != JsonValueKind.Undefined)
        {
            update = parsedData;
        }
        else
        {
            // Fallback to plain text parsing
            var json = Encoding.UTF8.GetString(packetData);
            update = JsonSerializer.Deserialize<JsonElement>(json);
        }
    }
    else
    {
        // Plain text packet
        var json = Encoding.UTF8.GetString(packetData);
        update = JsonSerializer.Deserialize<JsonElement>(json);
    }
    
    // Process by command type
    if (update.TryGetProperty("command", out var cmdElement))
    {
        string command = cmdElement.GetString();
        
        if (command == "UPDATE")
        {
            // Process position update (same as before)
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
            // Process input update (same as before)
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

## 6. Example TCP Client (C# with TLS)

```csharp
using System;
using System.Net.Sockets;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography.X509Certificates;

class RacingClient
{
    private TcpClient _tcp;
    private SslStream _sslStream;
    private string _sessionId;

    public async Task RunAsync(string host, int port)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port);
        
        // Setup TLS connection
        _sslStream = new SslStream(_tcp.GetStream(), false, ValidateServerCertificate);
        await _sslStream.AuthenticateAsClientAsync(host);

        // Read welcome
        var reader = new StreamReader(_sslStream, Encoding.UTF8);
        var welcome = await reader.ReadLineAsync();
        Console.WriteLine(welcome); // "CONNECTED|<sessionId>"
        _sessionId = welcome.Split('|')[1];

        // Set name with password
        await SendJsonAsync(new { command = "NAME", name = "Speedy", password = "secret123" });
        var response = await reader.ReadLineAsync();
        Console.WriteLine(response); // {"command":"NAME_OK","name":"Speedy","authenticated":true,"udpEncryption":true}

        // Create a room
        await SendJsonAsync(new { command = "CREATE_ROOM", name = "FastTrack" });
        response = await reader.ReadLineAsync();
        Console.WriteLine(response); // {"command":"ROOM_CREATED","roomId":"<id>","name":"FastTrack"}

        // ... then start your UDP updates with encryption …
    }

    private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // For development/LAN use, you might accept self-signed certificates
        if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            return true; // Accept self-signed certificates

        return sslPolicyErrors == SslPolicyErrors.None;
    }

    private async Task SendJsonAsync<T>(T data)
    {
        var json = JsonSerializer.Serialize(data) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sslStream.WriteAsync(bytes, 0, bytes.Length);
    }
}
```

## 7. Player Session Management

### 7.1 Session Lifecycle
- Sessions are created when a client connects via TCP
- Each session has a unique ID that's shared with the client
- Inactivity timeout: Sessions with no activity for > 60 seconds are disconnected
- Sessions track player name, authentication status, current room, and last activity time

### 7.2 Authentication Process
1. **Initial Connection**: A player connects and receives a session ID
2. **Name Registration**: 
   - New players set their name and password using the NAME command
   - The server stores a hash of the password for future verification
3. **Authentication**:
   - When a player reconnects with a previously used name, they must provide the correct password
   - Authentication can happen in the NAME command or separately with AUTHENTICATE
   - Successfully authenticated players can access all game features
   - Unauthenticated players can only use basic commands like listing rooms

### 7.3 Player Information Structure
The server maintains player information using the `PlayerInfo` record with the following properties:
- `Id`: Unique player identifier (matches session ID)
- `Name`: Display name of the player
- `UdpEndpoint`: The IP:Port combination for UDP communication
- `Position`: Player's 3D position in the game world as a Vector3
- `Rotation`: Player's orientation in the game world as a Quaternion

This structure is used to track player state and share it with other players in the same room.

### 7.4 Spawn Positions and Game Start
When a host starts a game:
1. The server assigns a spawn position to each player in the room
2. The spawn positions are sent to all players as part of the `GAME_STARTED` notification
3. Each player is assigned a unique garage position along the track (see predefined positions below)
4. Clients should place each player's vehicle at their assigned spawn position

Predefined spawn positions on the track:
```
Position 0:  (66, -2, 0.8)
Position 1:  (60, -2, 0.8)
Position 2:  (54, -2, 0.8)
Position 3:  (47, -2, 0.8)
Position 4:  (41, -2, 0.8)
Position 5:  (35, -2, 0.8)
Position 6:  (28, -2, 0.8)
Position 7:  (22, -2, 0.8)
Position 8:  (16, -2, 0.8)
Position 9:  (9, -2, 0.8)
Position 10: (3, -2, 0.8)
Position 11: (-3, -2, 0.8)
Position 12: (-9, -2, 0.8)
Position 13: (-15, -2, 0.8)
Position 14: (-22, -2, 0.8)
Position 15: (-28, -2, 0.8)
Position 16: (-34, -2, 0.8)
Position 17: (-41, -2, 0.8)
Position 18: (-47, -2, 0.8)
Position 19: (-54, -2, 0.8)
```

The order of assignment depends on the order in which players joined the room, with earlier players getting lower position indices.

### 7.4 Position and Rotation Updates
Position updates use the following data flow:
1. Client sends a position update via UDP
2. Server associates the update with a player session
3. Server updates the player's information in their current room
4. Server broadcasts the update to all other players in the same room
5. Clients receive updates for all other players and update their local game state

The position is represented as a 3D vector (x, y, z) and rotation as a quaternion (x, y, z, w).

### 7.4 Message Relay
- Players can relay messages to other players using their session ID
- Messages are delivered to the target player via TCP
- The receiving player gets a `RELAYED_MESSAGE` command with the sender's ID, name, and message
- This can be used for in-game chat, custom events, or game-specific commands

## 8. Host Transfer Mechanism
When a host player leaves a room:
- If the room is empty, it is automatically removed
- If other players remain in the room, host status transfers to another player
- The new host has the authority to start the game
- No specific notification is sent when host status changes (clients should track this if needed)

## 9. Real-time Data Synchronization
The server supports two types of real-time data that are synchronized between players:

### 9.1 Position/Rotation Updates
- Sent via UDP with the "UPDATE" command
- Contains full position (Vector3) and rotation (Quaternion) data
- Updated whenever the client sends a new position update
- Distributed to all other players in the same room

### 9.2 Input Controls
- Sent via UDP with the "INPUT" command
- Contains steering, throttle, brake values, and a timestamp
- Used to synchronize vehicle control inputs between players
- Can be used for physics prediction or deterministic simulation
- The timestamp helps with lag compensation and input ordering

## 10. Logging & Debug
- TCP events (connect, disconnect, commands) are logged at INFO level
- UDP packet receipt and processing are logged at DEBUG level
- JSON parsing errors are caught and logged
- Use console logger to trace flow:
  ```bash
  dotnet run --verbosity normal
  ```

## 11. Dashboard Web Interface

### 11.1 Overview
The server includes a web-based dashboard interface for monitoring and administering the server. The dashboard is accessible via HTTP on port 8080.

### 11.2 Dashboard Features
- Real-time monitoring of server statistics:
  - Server uptime
  - Active player sessions
  - Room count and status
  - Player counts
- Room management:
  - View all active rooms
  - View player distribution across rooms
  - See room status (lobby or active game)
  - Age of each room
- Player session management:
  - Monitor active player connections
  - Check which room each player is in
  - Track player activity

### 11.3 Admin Controls
The dashboard includes administrative controls for server management. These controls do not require authentication as the dashboard is intended for LAN-only access and not exposed to the internet.

#### Room Admin Controls
- **Close Room**: Remove a specific room and return all players to the lobby
- **Close All Rooms**: Remove all rooms and reset all player room associations

#### Player Admin Controls
- **Disconnect Player**: Forcibly disconnect a specific player session
- **Disconnect All Players**: Disconnect all active player sessions

#### Admin Actions Process
When an admin action is performed:
1. The action is processed on the server
2. Affected players receive appropriate notifications
3. Resources are cleaned up (players removed from rooms, etc.)
4. Host status is transferred if necessary
5. The dashboard refreshes to show updated state

### 11.4 Accessing the Dashboard
- The dashboard is available at `http://server-ip:8080`
- It auto-refreshes every 10 seconds, with an option for manual refresh
- No login is required as it's designed for LAN-only access

## 12. Next Steps
- Implement certificate pinning for enhanced security
- Add rate limiting for UDP packets
- Implement server-side physics validation
- Add race-specific features like lap counting and race timing
- Optimize UDP broadcast for large player counts
- Implement game state synchronization for deterministic physics
- Add comprehensive logging and monitoring

## 13. Security Implementation Details

### 13.1 TLS/SSL Configuration
- **Certificate Management**: Server automatically generates self-signed certificates if none provided
- **Certificate Storage**: Certificates are saved as `server.pfx` in the application directory
- **TLS Versions**: Supports TLS 1.2 and TLS 1.3
- **Cipher Suites**: Uses modern cipher suites with forward secrecy
- **Certificate Validation**: Clients should implement proper certificate validation for production use

### 13.2 UDP Encryption Details
- **Algorithm**: AES-256-CBC encryption
- **Key Derivation**: Session-specific keys derived from session ID + shared secret
- **Packet Format**: `[4-byte length][encrypted data]`
- **Backward Compatibility**: Server accepts both encrypted and plain-text UDP packets
- **Session Isolation**: Each authenticated player gets unique encryption keys

### 13.3 Password Security
- **Hashing**: SHA-256 (should be upgraded to bcrypt/scrypt for production)
- **Storage**: Only password hashes are stored, never plain text
- **First-time Setup**: First connection with a username sets the password
- **Verification**: Subsequent connections require correct password

### 13.4 Security Best Practices for Clients
1. **Certificate Validation**: Implement proper certificate validation for production
2. **Connection Security**: Always use TLS for TCP connections
3. **Key Management**: Securely store UDP encryption keys
4. **Error Handling**: Handle authentication failures gracefully
5. **Rate Limiting**: Implement client-side rate limiting for UDP packets

### 13.5 Server Security Features
- **Session Isolation**: Each player session is completely isolated
- **Automatic Cleanup**: Inactive sessions are automatically removed
- **Certificate Rotation**: Certificates can be replaced without server restart
- **Encryption Negotiation**: Server automatically detects encrypted vs plain UDP packets
- **Admin Interface**: Dashboard is designed for LAN-only access (no authentication required)

## 13.6 Certificate Handling with Unity Clients

### 13.6.1 Certificate Validation in Unity
Unity clients might encounter certificate validation issues when connecting to the server:
```
Certificate validation failed: RemoteCertificateNameMismatch, RemoteCertificateChainErrors
```

This happens because:
1. The server uses a self-signed certificate
2. The client attempts to validate the certificate against trusted roots (which fails)
3. The hostname or IP the client connects to might not match what's in the certificate

### 13.6.2 Solution Options

#### Option 1: Bypass Certificate Validation (Development Only)
For development and testing, you can bypass certificate validation by implementing a custom validation callback:

```csharp
// In your Unity NetworkManager class
public bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                      X509Chain chain, SslPolicyErrors sslPolicyErrors)
{
    Debug.LogWarning($"Certificate validation: {sslPolicyErrors}");
    
    // Accept any certificate from our server
    // IMPORTANT: Only use this for your specific racing server, not general web traffic
    return true;
}

// When creating your SslStream
var sslStream = new SslStream(networkStream, false, ValidateServerCertificate);
await sslStream.AuthenticateAsClientAsync("your-server-hostname-or-ip");
```

#### Option 2: Export and Include the Server Certificate
For better security, you can include the server's public certificate in your game:

1. Export the server's public certificate (without private key)
2. Include it as a resource in your Unity game
3. Load and verify against this specific certificate:

```csharp
public class CertificateHandler
{
    private readonly X509Certificate2 _serverCert;
    
    public CertificateHandler()
    {
        // Load server certificate from resources
        TextAsset certAsset = Resources.Load<TextAsset>("server-cert");
        _serverCert = new X509Certificate2(certAsset.bytes);
    }
    
    public bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                          X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // Only accept our specific server certificate
        return certificate.GetCertHashString() == _serverCert.GetCertHashString();
    }
}
```

### 13.6.3 Exporting the Server Certificate
To export the server's public certificate for client validation:

1. From the server machine:
```bash
# Export PFX to PEM (public part only)
openssl pkcs12 -in server.pfx -clcerts -nokeys -out server-public.pem
```

2. Copy the `server-public.pem` file to your Unity project's Resources folder

### 13.6.4 Certificate Generation Details
The server automatically generates a certificate with:
- The server's public IP (obtained from `SERVER_PUBLIC_IP` environment variable or defaults to "89.114.116.19")
- The server's internal IP addresses
- The server hostname (`SERVER_HOSTNAME` environment variable or "racing-server")
- All common network interfaces
- Common special IPs (loopback, Any)

You can customize the certificate by setting these environment variables before starting the server:

```bash
# Linux/macOS
export SERVER_HOSTNAME="my-racing-server"
export SERVER_PUBLIC_IP="your-public-ip"

# Windows
set SERVER_HOSTNAME=my-racing-server
set SERVER_PUBLIC_IP=your-public-ip
```

## 14. Authentication System

### 14.1 Purpose
The authentication system provides a simple identity protection mechanism where:
- Players can claim and protect their unique usernames
- Re-connecting with the same name requires password verification
- Only basic commands are available without authentication
- Game-related features require successful authentication

### 14.2 Password Handling
The server uses SHA-256 hashing for password storage:
- Passwords are never stored in plain text
- Each player name has its own associated password hash
- The first time a name is used, the provided password is stored
- Subsequent connections must use the same password

### 14.3 Client Implementation
Client applications should:
1. Store the player's name and password locally
2. Send the password with the NAME command on reconnection
3. Handle AUTH_FAILED responses appropriately:
   - Show an error message to the user
   - Request the correct password
   - Offer to use a different name

### 14.4 Authentication States
Players can be in one of two authentication states:
1. **Unauthenticated**: Limited to basic commands
2. **Authenticated**: Full access to all game features

### 14.5 Recommended Client Workflow
```
1. Connect to server → Receive session ID
2. Ask user for name and password
3. Send NAME command with credentials
4. If AUTH_FAILED, prompt user again or choose new name
5. Once authenticated (NAME_OK with authenticated=true), proceed with game
```

## 15. Unity Client Implementation Guide

### 15.1 Overview
This section provides a complete guide for implementing a secure Unity client that connects to the MP-Server. The implementation covers TLS-encrypted TCP for commands and AES-encrypted UDP for real-time updates.

### 15.2 Unity Project Setup

#### 15.2.1 Required Assemblies
Add these assemblies to your Unity project (in the Player Settings → Configuration):
```
System.Net.Security
System.Security.Cryptography
System.Text.Json
```

#### 15.2.2 Required Using Statements
```csharp
using System;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine;
using System.Net;
```

### 15.3 Complete Unity NetworkManager Implementation

```csharp
using System;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine;
using System.Net;
using System.IO;

public class RacingNetworkManager : MonoBehaviour
{
    [Header("Connection Settings")]
    public string serverHost = "localhost";
    public int serverPort = 443;
    
    [Header("Player Settings")]
    public string playerName = "Player";
    public string playerPassword = "secret123";
    
    [Header("Debug")]
    public bool enableDebugLogs = true;
    
    // Networking components
    private TcpClient _tcpClient;
    private SslStream _sslStream;
    private UdpClient _udpClient;
    private StreamReader _tcpReader;
    private bool _isConnected = false;
    private bool _isAuthenticated = false;
    
    // Session data
    private string _sessionId;
    private string _currentRoomId;
    private UdpEncryption _udpCrypto;
    
    // UDP endpoint
    private IPEndPoint _serverUdpEndpoint;
    
    // Events
    public System.Action<bool> OnConnectionChanged;
    public System.Action<bool> OnAuthenticationChanged;
    public System.Action<string> OnRoomJoined;
    public System.Action<PlayerUpdate> OnPlayerUpdate;
    public System.Action<string, string> OnMessageReceived;
    
    void Start()
    {
        DontDestroyOnLoad(gameObject);
        _serverUdpEndpoint = new IPEndPoint(IPAddress.Parse(serverHost), serverPort);
    }
    
    #region Connection Management
    
    public async Task<bool> ConnectToServer()
    {
        try
        {
            Log("Connecting to server...");
            
            // Create TCP connection
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(serverHost, serverPort);
            
            // Setup TLS encryption
            _sslStream = new SslStream(_tcpClient.GetStream(), false, ValidateServerCertificate);
            await _sslStream.AuthenticateAsClientAsync(serverHost);
            
            // Setup reader for responses
            _tcpReader = new StreamReader(_sslStream, Encoding.UTF8);
            
            // Read welcome message
            string welcome = await _tcpReader.ReadLineAsync();
            Log($"Server welcome: {welcome}");
            
            if (welcome != null && welcome.StartsWith("CONNECTED|"))
            {
                _sessionId = welcome.Split('|')[1];
                _isConnected = true;
                OnConnectionChanged?.Invoke(true);
                
                // Start listening for TCP messages
                StartCoroutine(ListenForTcpMessages());
                
                // Authenticate player
                await AuthenticatePlayer();
                
                return true;
            }
            
            throw new Exception("Invalid welcome message from server");
        }
        catch (Exception ex)
        {
            LogError($"Failed to connect: {ex.Message}");
            await Disconnect();
            return false;
        }
    }
    
    private bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                          X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // For development: Accept self-signed certificates
        // For production: Implement proper certificate validation
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;
            
        if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors ||
            sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
        {
            Log($"Accepting self-signed certificate: {sslPolicyErrors}");
            return true; // Accept for development
        }
        
        LogError($"Certificate validation failed: {sslPolicyErrors}");
        return false;
    }
    
    private async Task AuthenticatePlayer()
    {
        try
        {
            var nameCommand = new
            {
                command = "NAME",
                name = playerName,
                password = playerPassword
            };
            
            await SendTcpMessage(nameCommand);
        }
        catch (Exception ex)
        {
            LogError($"Authentication failed: {ex.Message}");
        }
    }
    
    public async Task Disconnect()
    {
        _isConnected = false;
        _isAuthenticated = false;
        
        try
        {
            if (_sslStream != null)
            {
                await SendTcpMessage(new { command = "BYE" });
                _sslStream.Close();
            }
            
            _tcpClient?.Close();
            _udpClient?.Close();
        }
        catch (Exception ex)
        {
            LogError($"Error during disconnect: {ex.Message}");
        }
        
        OnConnectionChanged?.Invoke(false);
        OnAuthenticationChanged?.Invoke(false);
    }
    
    #endregion
    
    #region TCP Communication
    
    private async Task SendTcpMessage<T>(T message)
    {
        if (!_isConnected || _sslStream == null)
        {
            LogError("Not connected to server");
            return;
        }
        
        try
        {
            string json = JsonSerializer.Serialize(message) + "\n";
            byte[] data = Encoding.UTF8.GetBytes(json);
            await _sslStream.WriteAsync(data, 0, data.Length);
            
            Log($"Sent: {json.Trim()}");
        }
        catch (Exception ex)
        {
            LogError($"Failed to send TCP message: {ex.Message}");
        }
    }
    
    private IEnumerator ListenForTcpMessages()
    {
        while (_isConnected && _tcpReader != null)
        {
            Task<string> readTask = _tcpReader.ReadLineAsync();
            
            // Wait for the task to complete
            while (!readTask.IsCompleted)
            {
                yield return null;
            }
            
            if (readTask.Result != null)
            {
                ProcessTcpMessage(readTask.Result);
            }
            else
            {
                // Connection lost
                Log("TCP connection lost");
                break;
            }
        }
        
        // Connection ended
        _ = Disconnect();
    }
    
    private void ProcessTcpMessage(string message)
    {
        try
        {
            Log($"Received: {message}");
            
            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;
            
            if (root.TryGetProperty("command", out var commandElement))
            {
                string command = commandElement.GetString();
                
                switch (command)
                {
                    case "NAME_OK":
                        HandleNameOk(root);
                        break;
                        
                    case "AUTH_FAILED":
                        HandleAuthFailed(root);
                        break;
                        
                    case "ROOM_CREATED":
                    case "JOIN_OK":
                        HandleRoomJoined(root);
                        break;
                        
                    case "GAME_STARTED":
                        HandleGameStarted(root);
                        break;
                        
                    case "RELAYED_MESSAGE":
                        HandleRelayedMessage(root);
                        break;
                        
                    case "ERROR":
                        HandleError(root);
                        break;
                        
                    default:
                        Log($"Unhandled command: {command}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to process TCP message: {ex.Message}");
        }
    }
    
    private void HandleNameOk(JsonElement root)
    {
        if (root.TryGetProperty("authenticated", out var authElement) && authElement.GetBoolean())
        {
            _isAuthenticated = true;
            OnAuthenticationChanged?.Invoke(true);
            
            // Setup UDP encryption if available
            if (root.TryGetProperty("udpEncryption", out var udpElement) && udpElement.GetBoolean())
            {
                _udpCrypto = new UdpEncryption(_sessionId);
                SetupUdpClient();
            }
            
            Log("Successfully authenticated with UDP encryption enabled");
        }
    }
    
    private void HandleAuthFailed(JsonElement root)
    {
        if (root.TryGetProperty("message", out var messageElement))
        {
            LogError($"Authentication failed: {messageElement.GetString()}");
        }
        _isAuthenticated = false;
        OnAuthenticationChanged?.Invoke(false);
    }
    
    private void HandleRoomJoined(JsonElement root)
    {
        if (root.TryGetProperty("roomId", out var roomIdElement))
        {
            _currentRoomId = roomIdElement.GetString();
            OnRoomJoined?.Invoke(_currentRoomId);
            Log($"Joined room: {_currentRoomId}");
        }
    }
    
    private void HandleGameStarted(JsonElement root)
    {
        // Handle spawn positions and game start logic
        if (root.TryGetProperty("spawnPositions", out var spawnElement))
        {
            // Process spawn positions for each player
            Log("Game started! Processing spawn positions...");
            // Implementation depends on your game logic
        }
    }
    
    private void HandleRelayedMessage(JsonElement root)
    {
        if (root.TryGetProperty("senderName", out var senderElement) &&
            root.TryGetProperty("message", out var messageElement))
        {
            OnMessageReceived?.Invoke(senderElement.GetString(), messageElement.GetString());
        }
    }
    
    private void HandleError(JsonElement root)
    {
        if (root.TryGetProperty("message", out var messageElement))
        {
            LogError($"Server error: {messageElement.GetString()}");
        }
    }
    
    #endregion
    
    #region UDP Communication
    
    private void SetupUdpClient()
    {
        try
        {
            _udpClient = new UdpClient();
            StartCoroutine(ListenForUdpMessages());
            Log("UDP client setup complete");
        }
        catch (Exception ex)
        {
            LogError($"Failed to setup UDP client: {ex.Message}");
        }
    }
    
    public async Task SendPositionUpdate(Vector3 position, Quaternion rotation)
    {
        if (!_isAuthenticated || _udpClient == null || string.IsNullOrEmpty(_currentRoomId))
            return;
        
        try
        {
            var update = new
            {
                command = "UPDATE",
                sessionId = _sessionId,
                position = new { x = position.x, y = position.y, z = position.z },
                rotation = new { x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w }
            };
            
            byte[] data;
            
            if (_udpCrypto != null)
            {
                // Send encrypted packet
                data = _udpCrypto.CreatePacket(update);
            }
            else
            {
                // Fallback to plain text
                string json = JsonSerializer.Serialize(update);
                data = Encoding.UTF8.GetBytes(json);
            }
            
            await _udpClient.SendAsync(data, data.Length, _serverUdpEndpoint);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send position update: {ex.Message}");
        }
    }
    
    public async Task SendInputUpdate(float steering, float throttle, float brake)
    {
        if (!_isAuthenticated || _udpClient == null || string.IsNullOrEmpty(_currentRoomId))
            return;
        
        try
        {
            var input = new
            {
                command = "INPUT",
                sessionId = _sessionId,
                roomId = _currentRoomId,
                input = new
                {
                    steering = steering,
                    throttle = throttle,
                    brake = brake,
                    timestamp = Time.time
                },
                client_id = _sessionId
            };
            
            byte[] data;
            
            if (_udpCrypto != null)
            {
                data = _udpCrypto.CreatePacket(input);
            }
            else
            {
                string json = JsonSerializer.Serialize(input);
                data = Encoding.UTF8.GetBytes(json);
            }
            
            await _udpClient.SendAsync(data, data.Length, _serverUdpEndpoint);
        }
        catch (Exception ex)
        {
            LogError($"Failed to send input update: {ex.Message}");
        }
    }
    
    private IEnumerator ListenForUdpMessages()
    {
        while (_isConnected && _udpClient != null)
        {
            Task<UdpReceiveResult> receiveTask = _udpClient.ReceiveAsync();
            
            while (!receiveTask.IsCompleted)
            {
                yield return null;
            }
            
            if (receiveTask.IsCompletedSuccessfully)
            {
                ProcessUdpMessage(receiveTask.Result.Buffer);
            }
        }
    }
    
    private void ProcessUdpMessage(byte[] data)
    {
        try
        {
            JsonElement update;
            
            // Try to decrypt if possible
            if (_udpCrypto != null && data.Length >= 4)
            {
                var parsedData = _udpCrypto.ParsePacket<JsonElement>(data);
                if (parsedData.ValueKind != JsonValueKind.Undefined)
                {
                    update = parsedData;
                }
                else
                {
                    // Fallback to plain text
                    string json = Encoding.UTF8.GetString(data);
                    update = JsonSerializer.Deserialize<JsonElement>(json);
                }
            }
            else
            {
                // Plain text packet
                string json = Encoding.UTF8.GetString(data);
                update = JsonSerializer.Deserialize<JsonElement>(json);
            }
            
            // Process the update
            if (update.TryGetProperty("command", out var cmdElement))
            {
                string command = cmdElement.GetString();
                
                if (command == "UPDATE")
                {
                    HandlePositionUpdate(update);
                }
                else if (command == "INPUT")
                {
                    HandleInputUpdate(update);
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to process UDP message: {ex.Message}");
        }
    }
    
    private void HandlePositionUpdate(JsonElement update)
    {
        try
        {
            var sessionId = update.GetProperty("sessionId").GetString();
            var position = update.GetProperty("position");
            var rotation = update.GetProperty("rotation");
            
            var playerUpdate = new PlayerUpdate
            {
                SessionId = sessionId,
                Position = new Vector3(
                    position.GetProperty("x").GetSingle(),
                    position.GetProperty("y").GetSingle(),
                    position.GetProperty("z").GetSingle()
                ),
                Rotation = new Quaternion(
                    rotation.GetProperty("x").GetSingle(),
                    rotation.GetProperty("y").GetSingle(),
                    rotation.GetProperty("z").GetSingle(),
                    rotation.GetProperty("w").GetSingle()
                )
            };
            
            OnPlayerUpdate?.Invoke(playerUpdate);
        }
        catch (Exception ex)
        {
            LogError($"Failed to handle position update: {ex.Message}");
        }
    }
    
    private void HandleInputUpdate(JsonElement update)
    {
        // Handle input updates from other players
        // Implementation depends on your game's input handling system
    }
    
    #endregion
    
    #region Room Management
    
    public async Task CreateRoom(string roomName)
    {
        if (!_isAuthenticated) return;
        
        await SendTcpMessage(new { command = "CREATE_ROOM", name = roomName });
    }
    
    public async Task JoinRoom(string roomId)
    {
        if (!_isAuthenticated) return;
        
        await SendTcpMessage(new { command = "JOIN_ROOM", roomId = roomId });
    }
    
    public async Task StartGame()
    {
        if (!_isAuthenticated || string.IsNullOrEmpty(_currentRoomId)) return;
        
        await SendTcpMessage(new { command = "START_GAME" });
    }
    
    public async Task SendMessage(string targetId, string message)
    {
        if (!_isAuthenticated) return;
        
        await SendTcpMessage(new { command = "RELAY_MESSAGE", targetId = targetId, message = message });
    }
    
    #endregion
    
    #region Logging
    
    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[RacingNetwork] {message}");
        }
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[RacingNetwork] {message}");
    }
    
    #endregion
    
    void OnDestroy()
    {
        _ = Disconnect();
    }
}

// Data structures
[System.Serializable]
public struct PlayerUpdate
{
    public string SessionId;
    public Vector3 Position;
    public Quaternion Rotation;
}
```

### 15.4 UDP Encryption Implementation for Unity

```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class UdpEncryption
{
    private readonly byte[] _key;
    private readonly byte[] _iv;
    
    public UdpEncryption(string sessionId, string sharedSecret = "RacingServerUDP2024!")
    {
        // Generate deterministic key and IV from session ID and shared secret
        using var sha256 = SHA256.Create();
        var keySource = Encoding.UTF8.GetBytes(sessionId + sharedSecret);
        var keyHash = sha256.ComputeHash(keySource);
        
        _key = new byte[32]; // AES-256 key
        _iv = new byte[16];   // AES IV
        
        Array.Copy(keyHash, 0, _key, 0, 32);
        Array.Copy(keyHash, 16, _iv, 0, 16);
    }
    
    public byte[] Encrypt(string jsonData)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        var dataBytes = Encoding.UTF8.GetBytes(jsonData);
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);
    }
    
    public string Decrypt(byte[] encryptedData)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    public byte[] CreatePacket(object data)
    {
        var json = JsonSerializer.Serialize(data);
        var encryptedData = Encrypt(json);
        
        // Create packet with length header
        var packet = new byte[4 + encryptedData.Length];
        BitConverter.GetBytes(encryptedData.Length).CopyTo(packet, 0);
        encryptedData.CopyTo(packet, 4);
        
        return packet;
    }
    
    public T ParsePacket<T>(byte[] packetData)
    {
        if (packetData.Length < 4)
            return default(T);
        
        var length = BitConverter.ToInt32(packetData, 0);
        if (length != packetData.Length - 4 || length <= 0)
            return default(T);
        
        var encryptedData = new byte[length];
        Array.Copy(packetData, 4, encryptedData, 0, length);
        
        var json = Decrypt(encryptedData);
        if (string.IsNullOrEmpty(json))
            return default(T);
        
        return JsonSerializer.Deserialize<T>(json);
    }
}
```

### 15.5 Unity Usage Example

```csharp
public class GameController : MonoBehaviour
{
    [Header("Network")]
    public RacingNetworkManager networkManager;
    
    [Header("Player")]
    public Transform playerCar;
    
    [Header("Other Players")]
    public GameObject playerCarPrefab;
    
    private Dictionary<string, GameObject> _otherPlayers = new Dictionary<string, GameObject>();
    
    async void Start()
    {
        // Setup network events
        networkManager.OnConnectionChanged += OnConnectionChanged;
        networkManager.OnAuthenticationChanged += OnAuthenticationChanged;
        networkManager.OnPlayerUpdate += OnPlayerUpdate;
        
        // Connect to server
        bool connected = await networkManager.ConnectToServer();
        if (connected)
        {
            Debug.Log("Connected to racing server!");
        }
    }
    
    void Update()
    {
        // Send position updates for our player
        if (networkManager._isAuthenticated && playerCar != null)
        {
            _ = networkManager.SendPositionUpdate(playerCar.position, playerCar.rotation);
            
            // Send input if the car is being controlled
            float steering = Input.GetAxis("Horizontal");
            float throttle = Input.GetAxis("Vertical");
            float brake = Input.GetKey(KeyCode.Space) ? 1.0f : 0.0f;
            
            _ = networkManager.SendInputUpdate(steering, throttle, brake);
        }
    }
    
    private void OnConnectionChanged(bool connected)
    {
        Debug.Log($"Connection status: {connected}");
    }
    
    private void OnAuthenticationChanged(bool authenticated)
    {
        Debug.Log($"Authentication status: {authenticated}");
        
        if (authenticated)
        {
            // Auto-create or join a room
            _ = networkManager.CreateRoom("My Race Room");
        }
    }
    
    private void OnPlayerUpdate(PlayerUpdate update)
    {
        // Update other player positions
        if (!_otherPlayers.ContainsKey(update.SessionId))
        {
            // Create new player car
            GameObject newPlayer = Instantiate(playerCarPrefab);
            _otherPlayers[update.SessionId] = newPlayer;
        }
        
        // Update position and rotation
        var playerObject = _otherPlayers[update.SessionId];
        playerObject.transform.position = update.Position;
        playerObject.transform.rotation = update.Rotation;
    }
}
```

### 15.6 Security Best Practices for Unity

#### 15.6.1 Certificate Handling
- **Development**: Use the provided certificate validation bypass for testing
- **Production**: Implement proper certificate validation or bundle the server's public certificate
- **Never** accept all certificates in production builds

#### 15.6.2 Password Security
- Store passwords securely using Unity's PlayerPrefs with encryption or a secure vault
- Consider implementing a proper registration/login UI
- Use strong passwords for player accounts

#### 15.6.3 Network Security
- Always validate incoming data before using it
- Implement client-side rate limiting for UDP packets
- Handle network errors gracefully

#### 15.6.4 Error Handling
```csharp
private void HandleNetworkError(Exception ex)
{
    Debug.LogError($"Network error: {ex.Message}");
    
    // Attempt reconnection
    StartCoroutine(ReconnectAfterDelay(5.0f));
}

private IEnumerator ReconnectAfterDelay(float delay)
{
    yield return new WaitForSeconds(delay);
    
    if (!networkManager._isConnected)
    {
        bool reconnected = await networkManager.ConnectToServer();
        if (!reconnected)
        {
            // Show error to user or try again
            StartCoroutine(ReconnectAfterDelay(10.0f));
        }
    }
}
```

### 15.7 Testing Your Unity Client

1. **Local Testing**: Connect to `localhost` or `127.0.0.1`
2. **LAN Testing**: Connect to the server's local IP address
3. **Internet Testing**: Connect to the server's public IP address

#### 15.7.1 Debug Console Commands
Add these debug commands to test your client:

```csharp
void OnGUI()
{
    if (GUI.Button(new Rect(10, 10, 100, 30), "Connect"))
    {
        _ = networkManager.ConnectToServer();
    }
    
    if (GUI.Button(new Rect(10, 50, 100, 30), "Create Room"))
    {
        _ = networkManager.CreateRoom("Test Room");
    }
    
    if (GUI.Button(new Rect(10, 90, 100, 30), "Start Game"))
    {
        _ = networkManager.StartGame();
    }
}
```

### 15.8 Performance Optimization

#### 15.8.1 Update Frequency
- Position updates: 10-20 Hz (every 50-100ms)
- Input updates: 30-60 Hz (every 16-33ms)
- Use Unity's `InvokeRepeating` or coroutines for consistent timing

#### 15.8.2 Network Interpolation
Implement client-side interpolation for smooth movement:

```csharp
public class NetworkPlayerController : MonoBehaviour
{
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private float _interpolationSpeed = 10f;
    
    public void UpdateNetworkTransform(Vector3 position, Quaternion rotation)
    {
        _targetPosition = position;
        _targetRotation = rotation;
    }
    
    void Update()
    {
        transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * _interpolationSpeed);
        transform.rotation = Quaternion.Lerp(transform.rotation, _targetRotation, Time.deltaTime * _interpolationSpeed);
    }
}
```

---

With this complete Unity implementation guide, you can now create a secure racing game client that properly connects to the MP-Server with full TLS encryption for commands and AES encryption for real-time updates. The implementation handles all security aspects while providing a robust foundation for your multiplayer racing game. Happy racing!



# MP-Server Client Implementation Guide

This comprehensive guide provides everything needed to implement a secure client for the MP-Server racing platform, covering TLS connections, authentication, UDP encryption, and complete implementation examples.

## Table of Contents

1. [Overview](#1-overview)
2. [Security Architecture](#2-security-architecture)
3. [Client Requirements](#3-client-requirements)
4. [TLS Connection Setup](#4-tls-connection-setup)
5. [Authentication Flow](#5-authentication-flow)
6. [UDP Encryption](#6-udp-encryption)
7. [Complete Implementation Examples](#7-complete-implementation-examples)
8. [Platform-Specific Guides](#8-platform-specific-guides)
9. [Troubleshooting](#9-troubleshooting)
10. [Performance Optimization](#10-performance-optimization)
11. [Testing and Deployment](#11-testing-and-deployment)

## 1. Overview

The MP-Server is a secure multiplayer racing server that uses:
- **TLS-encrypted TCP** on port 443 for commands and room management
- **AES-encrypted UDP** on port 443 for real-time position/input updates
- **Password-based authentication** for player identity protection
- **Self-signed certificates** with automatic generation including public IP support

### 1.1 Connection Flow

```
1. Client connects via TLS to server:443
2. Server sends: CONNECTED|<sessionId>
3. Client authenticates with NAME command + password
4. Server responds with NAME_OK + UDP encryption keys
5. Client can now use UDP for real-time updates
6. All TCP commands and UDP packets are encrypted
```

### 1.2 Key Features for Clients

- **Zero-configuration TLS**: Server auto-generates certificates
- **Session-based encryption**: Unique UDP keys per player
- **Password protection**: Secure username claiming
- **Real-time updates**: Low-latency UDP for racing data
- **Admin resistance**: Strong encryption prevents packet inspection

## 2. Security Architecture

### 2.1 TLS Configuration

The server uses TLS 1.2/1.3 with:
- **Auto-generated certificates** including public IP (89.114.116.19)
- **Subject Alternative Names** for all network interfaces
- **Modern cipher suites** with forward secrecy
- **Self-signed root** for LAN/development use

### 2.2 Authentication System

- **SHA-256 password hashing** (should upgrade to bcrypt for production)
- **First-come, first-served** username registration
- **Session-based access control** with command filtering
- **Automatic session cleanup** after 60 seconds of inactivity

### 2.3 UDP Encryption

- **AES-256-CBC encryption** for authenticated players
- **Session-specific keys** derived from sessionId + shared secret
- **Packet format**: `[4-byte length][encrypted JSON]`
- **Backward compatibility** with plain-text UDP for unauthenticated clients

## 3. Client Requirements

### 3.1 Essential Libraries

**C#/.NET:**
```csharp
System.Net.Sockets      // TCP/UDP networking
System.Net.Security     // TLS/SSL support
System.Security.Cryptography // Certificate validation, AES encryption
System.Text.Json        // JSON parsing
```

**Unity Additional:**
```csharp
UnityEngine            // Game engine integration
System.Threading.Tasks // Async/await support
System.Collections     // Coroutines
```

**Other Platforms:**
- **C++**: OpenSSL for TLS, nlohmann/json for JSON, platform networking APIs
- **Python**: `ssl`, `socket`, `json`, `cryptography` libraries
- **Node.js**: `tls`, `dgram`, `crypto`, native JSON support
- **Rust**: `tokio-rustls`, `serde_json`, `aes` crates

### 3.2 Network Configuration

**Firewall Requirements:**
- Outbound TCP port 443 (TLS)
- Outbound UDP port 443 (encrypted game data)
- Inbound UDP on random port (for receiving updates)

**NAT Considerations:**
- Server certificate includes public IP for NAT traversal
- UDP hole punching happens automatically on first packet send
- No special NAT configuration required for clients

## 4. TLS Connection Setup

### 4.1 Certificate Validation Strategies

#### Option 1: Development Mode (Accept Self-Signed)
```csharp
private bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                      X509Chain chain, SslPolicyErrors sslPolicyErrors)
{
    // Accept self-signed certificates for development
    if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors ||
        sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
    {
        Debug.Log($"Accepting self-signed certificate: {sslPolicyErrors}");
        return true;
    }
    
    return sslPolicyErrors == SslPolicyErrors.None;
}
```

#### Option 2: Certificate Pinning (Production)
```csharp
public class CertificatePinner
{
    private readonly string _expectedThumbprint;
    
    public CertificatePinner(string certificateThumbprint)
    {
        _expectedThumbprint = certificateThumbprint.Replace(":", "").Replace(" ", "").ToUpperInvariant();
    }
    
    public bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                          X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // Validate against pinned certificate
        var cert = new X509Certificate2(certificate);
        var actualThumbprint = cert.Thumbprint;
        
        if (actualThumbprint.Equals(_expectedThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log("Certificate thumbprint matches pinned certificate");
            return true;
        }
        
        Debug.LogError($"Certificate thumbprint mismatch! Expected: {_expectedThumbprint}, Got: {actualThumbprint}");
        return false;
    }
}
```

#### Option 3: Bundle Server Certificate
```csharp
public class BundledCertificateValidator
{
    private readonly X509Certificate2 _serverCert;
    
    public BundledCertificateValidator(byte[] serverCertificateData)
    {
        _serverCert = new X509Certificate2(serverCertificateData);
    }
    
    public bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                          X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // Compare with bundled certificate
        return certificate.GetCertHashString().Equals(_serverCert.GetCertHashString(), 
                                                     StringComparison.OrdinalIgnoreCase);
    }
}
```

### 4.2 Connection Establishment

```csharp
public async Task<bool> ConnectToServer(string host, int port = 443)
{
    try
    {
        // Create TCP connection
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port);
        
        // Setup TLS with certificate validation
        var sslStream = new SslStream(_tcpClient.GetStream(), false, ValidateServerCertificate);
        
        // Authenticate as client (this triggers TLS handshake)
        await sslStream.AuthenticateAsClientAsync(host);
        
        // Setup reader/writer for JSON communication
        _reader = new StreamReader(sslStream, Encoding.UTF8);
        _writer = new StreamWriter(sslStream, Encoding.UTF8) { AutoFlush = true };
        
        // Read welcome message
        string welcome = await _reader.ReadLineAsync();
        if (welcome?.StartsWith("CONNECTED|") == true)
        {
            _sessionId = welcome.Split('|')[1];
            return true;
        }
        
        throw new Exception($"Invalid welcome message: {welcome}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Connection failed: {ex.Message}");
        return false;
    }
}
```

### 4.3 Handling TLS Errors

**Common TLS Issues:**

1. **RemoteCertificateNameMismatch**: Certificate CN doesn't match hostname
   - Solution: Connect using IP address in certificate SAN list
   - Or: Use certificate validation callback to accept specific certificate

2. **RemoteCertificateChainErrors**: Self-signed certificate not trusted
   - Solution: Implement certificate pinning or accept self-signed in callback

3. **TLS Handshake Timeout**: Network or certificate issues
   - Solution: Check network connectivity, verify server is running
   - Try different IP addresses (public vs local)

## 5. Authentication Flow

### 5.1 Registration (First Time)

```csharp
public async Task<AuthResult> RegisterPlayer(string username, string password)
{
    try
    {
        var command = new
        {
            command = "NAME",
            name = username,
            password = password
        };
        
        await SendCommand(command);
        string response = await _reader.ReadLineAsync();
        
        var result = JsonSerializer.Deserialize<JsonElement>(response);
        string cmd = result.GetProperty("command").GetString();
        
        if (cmd == "NAME_OK")
        {
            bool authenticated = result.GetProperty("authenticated").GetBoolean();
            bool udpEncryption = result.TryGetProperty("udpEncryption", out var udpEl) && udpEl.GetBoolean();
            
            if (authenticated)
            {
                _isAuthenticated = true;
                if (udpEncryption)
                {
                    SetupUdpEncryption();
                }
                return AuthResult.Success;
            }
        }
        else if (cmd == "AUTH_FAILED")
        {
            string message = result.GetProperty("message").GetString();
            return AuthResult.Failed(message);
        }
        
        return AuthResult.Failed("Unknown response");
    }
    catch (Exception ex)
    {
        return AuthResult.Failed($"Registration error: {ex.Message}");
    }
}
```

### 5.2 Login (Returning Player)

```csharp
public async Task<AuthResult> LoginPlayer(string username, string password)
{
    // Same as registration - server automatically detects if username exists
    return await RegisterPlayer(username, password);
}
```

### 5.3 Separate Authentication

```csharp
public async Task<AuthResult> AuthenticateWithPassword(string password)
{
    try
    {
        var command = new { command = "AUTHENTICATE", password = password };
        await SendCommand(command);
        
        string response = await _reader.ReadLineAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(response);
        
        if (result.GetProperty("command").GetString() == "AUTH_OK")
        {
            _isAuthenticated = true;
            SetupUdpEncryption();
            return AuthResult.Success;
        }
        else
        {
            string message = result.GetProperty("message").GetString();
            return AuthResult.Failed(message);
        }
    }
    catch (Exception ex)
    {
        return AuthResult.Failed($"Authentication error: {ex.Message}");
    }
}
```

### 5.4 Authentication State Management

```csharp
public enum AuthState
{
    Disconnected,
    Connected,
    Authenticated
}

public class AuthResult
{
    public bool Success { get; private set; }
    public string ErrorMessage { get; private set; }
    
    public static AuthResult Success => new AuthResult { Success = true };
    public static AuthResult Failed(string error) => new AuthResult { Success = false, ErrorMessage = error };
}

// Track authentication state
private AuthState _authState = AuthState.Disconnected;
public event Action<AuthState> OnAuthStateChanged;

private void SetAuthState(AuthState newState)
{
    if (_authState != newState)
    {
        _authState = newState;
        OnAuthStateChanged?.Invoke(newState);
    }
}
```

## 6. UDP Encryption

### 6.1 Key Derivation

```csharp
public class UdpCrypto
{
    private readonly Aes _aes;
    private readonly byte[] _key;
    private readonly byte[] _iv;
    
    public UdpCrypto(string sessionId, string sharedSecret = "RacingServerUDP2024!")
    {
        // Derive encryption key from session ID and shared secret
        using var sha256 = SHA256.Create();
        var keyMaterial = Encoding.UTF8.GetBytes(sessionId + sharedSecret);
        var hash = sha256.ComputeHash(keyMaterial);
        
        _key = new byte[32]; // AES-256
        _iv = new byte[16];   // AES block size
        
        Array.Copy(hash, 0, _key, 0, 32);
        Array.Copy(hash, 16, _iv, 0, 16);
        
        _aes = Aes.Create();
        _aes.Key = _key;
        _aes.IV = _iv;
        _aes.Mode = CipherMode.CBC;
        _aes.Padding = PaddingMode.PKCS7;
    }
    
    public byte[] Encrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        using var encryptor = _aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
    }
    
    public string Decrypt(byte[] ciphertext)
    {
        try
        {
            using var decryptor = _aes.CreateDecryptor();
            var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch
        {
            return null; // Decryption failed
        }
    }
    
    public void Dispose()
    {
        _aes?.Dispose();
    }
}
```

### 6.2 Packet Format

```csharp
public class EncryptedUdpClient
{
    private readonly UdpClient _udpClient;
    private readonly UdpCrypto _crypto;
    private readonly IPEndPoint _serverEndpoint;
    
    public EncryptedUdpClient(UdpCrypto crypto, string serverHost, int serverPort)
    {
        _crypto = crypto;
        _udpClient = new UdpClient();
        _serverEndpoint = new IPEndPoint(IPAddress.Parse(serverHost), serverPort);
    }
    
    public async Task SendEncryptedPacket<T>(T data)
    {
        // Serialize to JSON
        string json = JsonSerializer.Serialize(data);
        
        // Encrypt the JSON
        byte[] encryptedData = _crypto.Encrypt(json);
        
        // Create packet: [4-byte length][encrypted data]
        byte[] packet = new byte[4 + encryptedData.Length];
        BitConverter.GetBytes(encryptedData.Length).CopyTo(packet, 0);
        encryptedData.CopyTo(packet, 4);
        
        // Send to server
        await _udpClient.SendAsync(packet, packet.Length, _serverEndpoint);
    }
    
    public async Task<T> ReceiveEncryptedPacket<T>()
    {
        var result = await _udpClient.ReceiveAsync();
        byte[] packet = result.Buffer;
        
        // Parse packet format
        if (packet.Length < 4) return default(T);
        
        int encryptedLength = BitConverter.ToInt32(packet, 0);
        if (encryptedLength != packet.Length - 4) return default(T);
        
        // Extract encrypted data
        byte[] encryptedData = new byte[encryptedLength];
        Array.Copy(packet, 4, encryptedData, 0, encryptedLength);
        
        // Decrypt and deserialize
        string json = _crypto.Decrypt(encryptedData);
        if (string.IsNullOrEmpty(json)) return default(T);
        
        return JsonSerializer.Deserialize<T>(json);
    }
}
```

### 6.3 Fallback for Unauthenticated Clients

```csharp
public async Task SendUpdate(object update)
{
    if (_isAuthenticated && _encryptedUdp != null)
    {
        // Send encrypted packet
        await _encryptedUdp.SendEncryptedPacket(update);
    }
    else
    {
        // Fallback to plain-text UDP
        string json = JsonSerializer.Serialize(update);
        byte[] data = Encoding.UTF8.GetBytes(json);
        await _plainUdp.SendAsync(data, data.Length, _serverEndpoint);
    }
}
```

## 7. Complete Implementation Examples

### 7.1 C# Console Client

```csharp
using System;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class RacingClient
{
    private TcpClient _tcpClient;
    private SslStream _sslStream;
    private StreamReader _reader;
    private StreamWriter _writer;
    private UdpClient _udpClient;
    private UdpCrypto _udpCrypto;
    private string _sessionId;
    private bool _isAuthenticated = false;
    
    public async Task<bool> Connect(string host, int port = 443)
    {
        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port);
            
            _sslStream = new SslStream(_tcpClient.GetStream(), false, ValidateServerCertificate);
            await _sslStream.AuthenticateAsClientAsync(host);
            
            _reader = new StreamReader(_sslStream, Encoding.UTF8);
            _writer = new StreamWriter(_sslStream, Encoding.UTF8) { AutoFlush = true };
            
            string welcome = await _reader.ReadLineAsync();
            if (welcome?.StartsWith("CONNECTED|") == true)
            {
                _sessionId = welcome.Split('|')[1];
                Console.WriteLine($"Connected with session ID: {_sessionId}");
                
                // Start listening for messages
                _ = Task.Run(ListenForMessages);
                
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
        }
        return false;
    }
    
    private bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                          X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        // Accept self-signed certificates for development
        return true;
    }
    
    public async Task<bool> Authenticate(string username, string password)
    {
        try
        {
            var command = new { command = "NAME", name = username, password = password };
            await SendCommand(command);
            
            // Wait for response (will be handled in ListenForMessages)
            await Task.Delay(1000);
            return _isAuthenticated;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authentication failed: {ex.Message}");
            return false;
        }
    }
    
    public async Task CreateRoom(string roomName)
    {
        if (!_isAuthenticated) return;
        
        var command = new { command = "CREATE_ROOM", name = roomName };
        await SendCommand(command);
    }
    
    public async Task StartGame()
    {
        if (!_isAuthenticated) return;
        
        var command = new { command = "START_GAME" };
        await SendCommand(command);
    }
    
    public async Task SendPositionUpdate(float x, float y, float z, float rx, float ry, float rz, float rw)
    {
        if (!_isAuthenticated || _udpClient == null) return;
        
        var update = new
        {
            command = "UPDATE",
            sessionId = _sessionId,
            position = new { x, y, z },
            rotation = new { x = rx, y = ry, z = rz, w = rw }
        };
        
        if (_udpCrypto != null)
        {
            // Send encrypted
            string json = JsonSerializer.Serialize(update);
            byte[] encrypted = _udpCrypto.Encrypt(json);
            byte[] packet = new byte[4 + encrypted.Length];
            BitConverter.GetBytes(encrypted.Length).CopyTo(packet, 0);
            encrypted.CopyTo(packet, 4);
            await _udpClient.SendAsync(packet, packet.Length, "localhost", 443);
        }
        else
        {
            // Send plain text
            string json = JsonSerializer.Serialize(update);
            byte[] data = Encoding.UTF8.GetBytes(json);
            await _udpClient.SendAsync(data, data.Length, "localhost", 443);
        }
    }
    
    private async Task SendCommand(object command)
    {
        string json = JsonSerializer.Serialize(command);
        await _writer.WriteLineAsync(json);
        Console.WriteLine($"Sent: {json}");
    }
    
    private async Task ListenForMessages()
    {
        try
        {
            while (_tcpClient.Connected)
            {
                string message = await _reader.ReadLineAsync();
                if (message == null) break;
                
                Console.WriteLine($"Received: {message}");
                await ProcessMessage(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Listen error: {ex.Message}");
        }
    }
    
    private async Task ProcessMessage(string message)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(message);
            string command = json.GetProperty("command").GetString();
            
            switch (command)
            {
                case "NAME_OK":
                    _isAuthenticated = json.GetProperty("authenticated").GetBoolean();
                    if (_isAuthenticated && json.TryGetProperty("udpEncryption", out var udpEl) && udpEl.GetBoolean())
                    {
                        SetupUdpEncryption();
                    }
                    Console.WriteLine($"Authentication successful: {_isAuthenticated}");
                    break;
                    
                case "AUTH_FAILED":
                    Console.WriteLine($"Authentication failed: {json.GetProperty("message").GetString()}");
                    break;
                    
                case "ROOM_CREATED":
                    string roomId = json.GetProperty("roomId").GetString();
                    Console.WriteLine($"Room created: {roomId}");
                    break;
                    
                case "GAME_STARTED":
                    Console.WriteLine("Game started!");
                    if (json.TryGetProperty("spawnPositions", out var spawnEl))
                    {
                        Console.WriteLine($"Spawn positions: {spawnEl}");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Message processing error: {ex.Message}");
        }
    }
    
    private void SetupUdpEncryption()
    {
        _udpClient = new UdpClient();
        _udpCrypto = new UdpCrypto(_sessionId);
        Console.WriteLine("UDP encryption enabled");
    }
    
    public void Disconnect()
    {
        _udpClient?.Close();
        _sslStream?.Close();
        _tcpClient?.Close();
        _udpCrypto?.Dispose();
    }
}

// Usage example
class Program
{
    static async Task Main(string[] args)
    {
        var client = new RacingClient();
        
        if (await client.Connect("localhost"))
        {
            if (await client.Authenticate("TestPlayer", "password123"))
            {
                await client.CreateRoom("Test Room");
                await client.StartGame();
                
                // Send some position updates
                for (int i = 0; i < 10; i++)
                {
                    await client.SendPositionUpdate(i, 0, 0, 0, 0, 0, 1);
                    await Task.Delay(100);
                }
            }
        }
        
        Console.WriteLine("Press any key to disconnect...");
        Console.ReadKey();
        client.Disconnect();
    }
}
```

### 7.2 Python Client Implementation

```python
import asyncio
import ssl
import socket
import json
import hashlib
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import padding
import struct

class RacingClient:
    def __init__(self, host="localhost", port=443):
        self.host = host
        self.port = port
        self.reader = None
        self.writer = None
        self.udp_socket = None
        self.session_id = None
        self.is_authenticated = False
        self.udp_crypto = None
        
    async def connect(self):
        """Connect to the racing server with TLS encryption"""
        try:
            # Create TLS context that accepts self-signed certificates
            context = ssl.create_default_context()
            context.check_hostname = False
            context.verify_mode = ssl.CERT_NONE
            
            # Connect with TLS
            self.reader, self.writer = await asyncio.open_connection(
                self.host, self.port, ssl=context
            )
            
            # Read welcome message
            welcome = await self.reader.readline()
            welcome_str = welcome.decode().strip()
            
            if welcome_str.startswith("CONNECTED|"):
                self.session_id = welcome_str.split("|")[1]
                print(f"Connected with session ID: {self.session_id}")
                
                # Start listening for messages
                asyncio.create_task(self._listen_for_messages())
                return True
            
        except Exception as e:
            print(f"Connection failed: {e}")
            return False
    
    async def authenticate(self, username, password):
        """Authenticate with username and password"""
        command = {
            "command": "NAME",
            "name": username,
            "password": password
        }
        await self._send_command(command)
        
        # Wait a bit for response
        await asyncio.sleep(0.5)
        return self.is_authenticated
    
    async def create_room(self, room_name):
        """Create a new racing room"""
        if not self.is_authenticated:
            return
        
        command = {"command": "CREATE_ROOM", "name": room_name}
        await self._send_command(command)
    
    async def start_game(self):
        """Start the racing game"""
        if not self.is_authenticated:
            return
        
        command = {"command": "START_GAME"}
        await self._send_command(command)
    
    async def send_position_update(self, x, y, z, rx, ry, rz, rw):
        """Send position update via UDP"""
        if not self.is_authenticated or not self.udp_socket:
            return
        
        update = {
            "command": "UPDATE",
            "sessionId": self.session_id,
            "position": {"x": x, "y": y, "z": z},
            "rotation": {"x": rx, "y": ry, "z": rz, "w": rw}
        }
        
        json_data = json.dumps(update)
        
        if self.udp_crypto:
            # Send encrypted
            encrypted_data = self.udp_crypto.encrypt(json_data)
            packet = struct.pack('<I', len(encrypted_data)) + encrypted_data
            self.udp_socket.sendto(packet, (self.host, self.port))
        else:
            # Send plain text
            self.udp_socket.sendto(json_data.encode(), (self.host, self.port))
    
    async def _send_command(self, command):
        """Send JSON command over TCP"""
        json_data = json.dumps(command) + "\n"
        self.writer.write(json_data.encode())
        await self.writer.drain()
        print(f"Sent: {json_data.strip()}")
    
    async def _listen_for_messages(self):
        """Listen for TCP messages from server"""
        try:
            while True:
                line = await self.reader.readline()
                if not line:
                    break
                
                message = line.decode().strip()
                print(f"Received: {message}")
                await self._process_message(message)
                
        except Exception as e:
            print(f"Listen error: {e}")
    
    async def _process_message(self, message):
        """Process incoming messages"""
        try:
            data = json.loads(message)
            command = data.get("command")
            
            if command == "NAME_OK":
                self.is_authenticated = data.get("authenticated", False)
                if self.is_authenticated and data.get("udpEncryption", False):
                    self._setup_udp_encryption()
                print(f"Authentication successful: {self.is_authenticated}")
                
            elif command == "AUTH_FAILED":
                print(f"Authentication failed: {data.get('message')}")
                
            elif command == "ROOM_CREATED":
                room_id = data.get("roomId")
                print(f"Room created: {room_id}")
                
            elif command == "GAME_STARTED":
                print("Game started!")
                spawn_positions = data.get("spawnPositions")
                if spawn_positions:
                    print(f"Spawn positions: {spawn_positions}")
                    
        except Exception as e:
            print(f"Message processing error: {e}")
    
    def _setup_udp_encryption(self):
        """Setup UDP encryption"""
        self.udp_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.udp_crypto = UdpCrypto(self.session_id)
        print("UDP encryption enabled")
    
    def disconnect(self):
        """Disconnect from server"""
        if self.udp_socket:
            self.udp_socket.close()
        if self.writer:
            self.writer.close()

class UdpCrypto:
    """UDP encryption/decryption using AES-256-CBC"""
    
    def __init__(self, session_id, shared_secret="RacingServerUDP2024!"):
        # Derive key from session ID and shared secret
        key_material = (session_id + shared_secret).encode()
        hash_bytes = hashlib.sha256(key_material).digest()
        
        self.key = hash_bytes[:32]  # AES-256 key
        self.iv = hash_bytes[16:32]  # 16-byte IV
        
    def encrypt(self, plaintext):
        """Encrypt plaintext string"""
        cipher = Cipher(algorithms.AES(self.key), modes.CBC(self.iv))
        encryptor = cipher.encryptor()
        
        # Add PKCS7 padding
        padder = padding.PKCS7(128).padder()
        padded_data = padder.update(plaintext.encode()) + padder.finalize()
        
        # Encrypt
        ciphertext = encryptor.update(padded_data) + encryptor.finalize()
        return ciphertext
    
    def decrypt(self, ciphertext):
        """Decrypt ciphertext to string"""
        try:
            cipher = Cipher(algorithms.AES(self.key), modes.CBC(self.iv))
            decryptor = cipher.decryptor()
            
            # Decrypt
            padded_data = decryptor.update(ciphertext) + decryptor.finalize()
            
            # Remove PKCS7 padding
            unpadder = padding.PKCS7(128).unpadder()
            plaintext = unpadder.update(padded_data) + unpadder.finalize()
            
            return plaintext.decode()
        except:
            return None

# Usage example
async def main():
    client = RacingClient("localhost")
    
    if await client.connect():
        if await client.authenticate("PythonPlayer", "password123"):
            await client.create_room("Python Test Room")
            await client.start_game()
            
            # Send some position updates
            for i in range(10):
                await client.send_position_update(i, 0, 0, 0, 0, 0, 1)
                await asyncio.sleep(0.1)
    
    input("Press Enter to disconnect...")
    client.disconnect()

if __name__ == "__main__":
    asyncio.run(main())
```

## 8. Platform-Specific Guides

### 8.1 Unity Integration

See the complete Unity implementation in the main documentation. Key points:

- Use `SslStream` for TLS connections
- Implement certificate validation callbacks
- Use coroutines for async operations
- Handle Unity's main thread requirements for UI updates
- Store credentials securely using PlayerPrefs

### 8.2 Unreal Engine Integration

```cpp
// Unreal Engine C++ implementation outline
#include "Runtime/Online/SSL/Public/Interfaces/ISslManager.h"
#include "Runtime/Sockets/Public/Sockets.h"
#include "Runtime/Core/Public/HAL/PlatformFilemanager.h"

class YOURGAME_API URacingNetworkComponent : public UActorComponent
{
    GENERATED_BODY()

public:
    URacingNetworkComponent();

    UFUNCTION(BlueprintCallable, Category = "Racing Network")
    bool ConnectToServer(const FString& Host, int32 Port = 443);

    UFUNCTION(BlueprintCallable, Category = "Racing Network")
    bool AuthenticatePlayer(const FString& Username, const FString& Password);

private:
    FSocket* TcpSocket;
    TSharedPtr<class FInternetAddr> ServerAddr;
    FString SessionId;
    bool bIsAuthenticated;

    void HandleIncomingData();
    void SendCommand(const FString& JsonCommand);
};
```

### 8.3 Godot Integration

```gdscript
# Godot GDScript implementation outline
extends Node

class_name RacingNetworkManager

var tcp_connection: StreamPeerTCP
var tls_connection: StreamPeerTLS
var udp_socket: PacketPeerUDP
var session_id: String
var is_authenticated: bool = false

func connect_to_server(host: String, port: int = 443) -> bool:
    tcp_connection = StreamPeerTCP.new()
    var error = tcp_connection.connect_to_host(host, port)
    
    if error != OK:
        print("TCP connection failed: ", error)
        return false
    
    # Wait for connection
    while tcp_connection.get_status() == StreamPeerTCP.STATUS_CONNECTING:
        await get_tree().process_frame
    
    if tcp_connection.get_status() != StreamPeerTCP.STATUS_CONNECTED:
        print("Failed to establish TCP connection")
        return false
    
    # Setup TLS
    tls_connection = StreamPeerTLS.new()
    tls_connection.connect_to_stream(tcp_connection, host)
    
    # Accept self-signed certificates for development
    tls_connection.set_verify_mode(StreamPeerTLS.TLS_VERIFY_NONE)
    
    # Wait for TLS handshake
    while tls_connection.get_status() == StreamPeerTLS.STATUS_HANDSHAKING:
        await get_tree().process_frame
    
    if tls_connection.get_status() != StreamPeerTLS.STATUS_CONNECTED:
        print("TLS handshake failed")
        return false
    
    # Read welcome message
    var welcome = read_line()
    if welcome.begins_with("CONNECTED|"):
        session_id = welcome.split("|")[1]
        print("Connected with session ID: ", session_id)
        return true
    
    return false

func authenticate_player(username: String, password: String) -> void:
    var command = {
        "command": "NAME",
        "name": username,
        "password": password
    }
    send_command(command)

func send_command(command: Dictionary) -> void:
    var json = JSON.stringify(command) + "\n"
    tls_connection.put_data(json.to_utf8_buffer())

func read_line() -> String:
    var line = ""
    while true:
        if tls_connection.get_available_bytes() > 0:
            var byte = tls_connection.get_u8()
            if byte == 10:  # newline
                break
            line += char(byte)
        await get_tree().process_frame
    return line
```

## 9. Troubleshooting

### 9.1 Common Connection Issues

#### TLS Handshake Failures

**Symptoms:**
```
"The remote certificate is invalid according to the validation procedure"
"RemoteCertificateNameMismatch, RemoteCertificateChainErrors"
"TLS handshake timeout"
```

**Solutions:**
1. **Certificate Validation Bypass** (development only):
   ```csharp
   private bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                          X509Chain chain, SslPolicyErrors sslPolicyErrors)
   {
       // Accept all certificates for development
       return true;
   }
   ```

2. **Check Server IP Configuration**:
   ```bash
   # Verify server certificate includes your IP
   openssl s_client -connect server-ip:443 -servername server-ip
   ```

3. **Use IP Address Instead of Hostname**:
   ```csharp
   // Connect using IP address that's in certificate SAN
   await client.ConnectAsync("89.114.116.19", 443);
   ```

#### Authentication Failures

**Symptoms:**
```json
{"command":"AUTH_FAILED","message":"Invalid password for this player name."}
{"command":"ERROR","message":"Authentication required. Please use NAME command with password."}
```

**Solutions:**
1. **Check Password Storage**: Ensure client stores/sends exact password
2. **Username Case Sensitivity**: Server may be case-sensitive for usernames
3. **Special Characters**: Ensure proper UTF-8 encoding for passwords
4. **First-Time Registration**: Verify first connection creates the account

#### UDP Encryption Issues

**Symptoms:**
- UDP packets not being received
- Server shows "Failed to decrypt UDP packet" errors
- Position updates not working after authentication

**Solutions:**
1. **Verify UDP Setup**:
   ```csharp
   // Ensure UDP client is created after authentication
   if (_isAuthenticated && udpEncryptionEnabled)
   {
       SetupUdpClient();
   }
   ```

2. **Check Packet Format**:
   ```csharp
   // Correct encrypted packet format
   byte[] packet = new byte[4 + encryptedData.Length];
   BitConverter.GetBytes(encryptedData.Length).CopyTo(packet, 0);
   encryptedData.CopyTo(packet, 4);
   ```

3. **Key Derivation Mismatch**:
   ```csharp
   // Ensure exact same key derivation as server
   var keyMaterial = sessionId + "RacingServerUDP2024!";
   var hash = SHA256.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
   ```

### 9.2 Network Connectivity Issues

#### Firewall/NAT Problems

**Symptoms:**
- Connection timeout to server
- TCP connects but UDP doesn't work
- Works on LAN but not over internet

**Solutions:**
1. **Firewall Configuration**:
   ```bash
   # Allow outbound connections on port 443
   # Windows Firewall
   netsh advfirewall firewall add rule name="Racing Client" dir=out action=allow protocol=TCP localport=443
   
   # Linux iptables
   iptables -A OUTPUT -p tcp --dport 443 -j ACCEPT
   iptables -A OUTPUT -p udp --dport 443 -j ACCEPT
   ```

2. **NAT Traversal**:
   ```csharp
   // Send UDP packet to register endpoint with server
   await udpClient.SendAsync(registrationPacket, packet.Length, serverEndpoint);
   ```

3. **Public IP Access**:
   ```csharp
   // Use server's public IP for internet connections
   string serverHost = "89.114.116.19"; // Server's public IP
   ```

#### Performance Issues

**Symptoms:**
- High latency for position updates
- Choppy movement of other players
- UDP packet loss

**Solutions:**
1. **Update Rate Optimization**:
   ```csharp
   // Don't send updates too frequently
   private DateTime _lastPositionUpdate = DateTime.MinValue;
   
   public async Task SendPositionUpdate(Vector3 pos, Quaternion rot)
   {
       if (DateTime.Now - _lastPositionUpdate < TimeSpan.FromMilliseconds(50))
           return; // Limit to 20 Hz
           
       _lastPositionUpdate = DateTime.Now;
       // ... send update
   }
   ```

2. **Client-Side Interpolation**:
   ```csharp
   public class PlayerInterpolator
   {
       private Vector3 _targetPosition;
       private DateTime _lastUpdate;
       
       public void SetTarget(Vector3 newPosition)
       {
           _targetPosition = newPosition;
           _lastUpdate = DateTime.Now;
       }
       
       public Vector3 GetInterpolatedPosition(Vector3 currentPosition)
       {
           float timeSinceUpdate = (float)(DateTime.Now - _lastUpdate).TotalSeconds;
           float lerpSpeed = Mathf.Clamp01(timeSinceUpdate * 10f);
           return Vector3.Lerp(currentPosition, _targetPosition, lerpSpeed);
       }
   }
   ```

### 9.3 Debug Tools and Logging

#### Enable Verbose Logging

```csharp
public class NetworkLogger
{
    public static bool EnableDebugLogging = true;
    
    public static void LogTcp(string direction, string message)
    {
        if (EnableDebugLogging)
        {
            Console.WriteLine($"[TCP {direction}] {DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }
    
    public static void LogUdp(string direction, string message)
    {
        if (EnableDebugLogging)
        {
            Console.WriteLine($"[UDP {direction}] {DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }
    
    public static void LogError(string component, string error)
    {
        Console.WriteLine($"[ERROR {component}] {DateTime.Now:HH:mm:ss.fff} {error}");
    }
}
```

#### Network Analysis Tools

1. **Wireshark**: Capture and analyze network traffic
2. **Netstat**: Check active connections
   ```bash
   netstat -an | grep 443
   ```
3. **OpenSSL Client**: Test TLS connectivity
   ```bash
   openssl s_client -connect server-ip:443 -debug
   ```

#### Server Dashboard

Access the server's web dashboard at `http://server-ip:8080` to monitor:
- Active player sessions
- Room status and player distribution
- Server uptime and statistics
- Admin controls for troubleshooting

## 10. Performance Optimization

### 10.1 Network Optimization

#### TCP Message Batching

```csharp
public class MessageBatcher
{
    private readonly Queue<object> _pendingMessages = new Queue<object>();
    private readonly Timer _batchTimer;
    
    public MessageBatcher(int batchIntervalMs = 50)
    {
        _batchTimer = new Timer(SendBatch, null, batchIntervalMs, batchIntervalMs);
    }
    
    public void QueueMessage(object message)
    {
        lock (_pendingMessages)
        {
            _pendingMessages.Enqueue(message);
        }
    }
    
    private void SendBatch(object state)
    {
        List<object> batch = new List<object>();
        
        lock (_pendingMessages)
        {
            while (_pendingMessages.Count > 0 && batch.Count < 10)
            {
                batch.Add(_pendingMessages.Dequeue());
            }
        }
        
        if (batch.Count > 0)
        {
            var batchMessage = new { command = "BATCH", messages = batch };
            _ = SendCommand(batchMessage);
        }
    }
}
```

#### UDP Compression

```csharp
public class CompressedUdpClient
{
    public byte[] CompressJson(object data)
    {
        string json = JsonSerializer.Serialize(data);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        
        using var compressed = new MemoryStream();
        using var gzip = new GZipStream(compressed, CompressionMode.Compress);
        gzip.Write(jsonBytes, 0, jsonBytes.Length);
        gzip.Close();
        
        return compressed.ToArray();
    }
    
    public T DecompressJson<T>(byte[] compressedData)
    {
        using var compressed = new MemoryStream(compressedData);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        
        gzip.CopyTo(decompressed);
        string json = Encoding.UTF8.GetString(decompressed.ToArray());
        
        return JsonSerializer.Deserialize<T>(json);
    }
}
```

### 10.2 Memory Optimization

#### Object Pooling for Network Messages

```csharp
public class NetworkMessagePool
{
    private readonly ConcurrentQueue<NetworkMessage> _pool = new ConcurrentQueue<NetworkMessage>();
    
    public NetworkMessage Rent()
    {
        if (_pool.TryDequeue(out var message))
        {
            message.Reset();
            return message;
        }
        
        return new NetworkMessage();
    }
    
    public void Return(NetworkMessage message)
    {
        if (message != null)
        {
            _pool.Enqueue(message);
        }
    }
}

public class NetworkMessage
{
    public string Command { get; set; }
    public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    
    public void Reset()
    {
        Command = null;
        Data.Clear();
    }
}
```

#### Efficient JSON Serialization

```csharp
public class OptimizedJsonSerializer
{
    private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
    
    private static readonly ThreadLocal<Utf8JsonWriter> _writerPool = 
        new ThreadLocal<Utf8JsonWriter>(() => new Utf8JsonWriter(new MemoryStream()));
    
    public static string SerializeOptimized<T>(T value)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        JsonSerializer.Serialize(writer, value, _options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
```

### 10.3 Threading and Async Optimization

#### Dedicated Network Thread

```csharp
public class NetworkManager
{
    private readonly Thread _networkThread;
    private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
    private readonly ConcurrentQueue<Action> _networkTasks = new ConcurrentQueue<Action>();
    
    public NetworkManager()
    {
        _networkThread = new Thread(NetworkThreadLoop)
        {
            Name = "NetworkThread",
            IsBackground = true
        };
        _networkThread.Start();
    }
    
    private void NetworkThreadLoop()
    {
        while (!_cancellation.Token.IsCancellationRequested)
        {
            // Process network tasks
            while (_networkTasks.TryDequeue(out var task))
            {
                try
                {
                    task();
                }
                catch (Exception ex)
                {
                    NetworkLogger.LogError("NetworkThread", ex.Message);
                }
            }
            
            // Process incoming network data
            ProcessIncomingData();
            
            Thread.Sleep(1); // Small yield
        }
    }
    
    public void QueueNetworkTask(Action task)
    {
        _networkTasks.Enqueue(task);
    }
}
```

## 11. Testing and Deployment

### 11.1 Unit Testing

```csharp
[TestClass]
public class RacingClientTests
{
    private TestServer _testServer;
    private RacingClient _client;
    
    [TestInitialize]
    public async Task Setup()
    {
        _testServer = new TestServer();
        await _testServer.Start();
        
        _client = new RacingClient();
    }
    
    [TestMethod]
    public async Task ConnectToServer_ShouldReturnTrue_WhenServerIsAvailable()
    {
        // Act
        bool connected = await _client.Connect("localhost", _testServer.Port);
        
        // Assert
        Assert.IsTrue(connected);
        Assert.IsNotNull(_client.SessionId);
    }
    
    [TestMethod]
    public async Task Authenticate_ShouldReturnTrue_WithValidCredentials()
    {
        // Arrange
        await _client.Connect("localhost", _testServer.Port);
        
        // Act
        bool authenticated = await _client.Authenticate("testuser", "testpass");
        
        // Assert
        Assert.IsTrue(authenticated);
    }
    
    [TestMethod]
    public async Task UdpEncryption_ShouldWorkAfterAuthentication()
    {
        // Arrange
        await _client.Connect("localhost", _testServer.Port);
        await _client.Authenticate("testuser", "testpass");
        
        // Act
        await _client.SendPositionUpdate(1, 2, 3, 0, 0, 0, 1);
        
        // Assert
        var receivedUpdate = await _testServer.GetLastUdpMessage();
        Assert.IsNotNull(receivedUpdate);
        Assert.AreEqual("UPDATE", receivedUpdate.Command);
    }
    
    [TestCleanup]
    public async Task Cleanup()
    {
        _client?.Disconnect();
        await _testServer?.Stop();
    }
}
```

### 11.2 Integration Testing

```csharp
[TestClass]
public class IntegrationTests
{
    [TestMethod]
    public async Task MultipleClients_ShouldReceiveEachOthersUpdates()
    {
        // Arrange
        var client1 = new RacingClient();
        var client2 = new RacingClient();
        
        await client1.Connect("localhost");
        await client2.Connect("localhost");
        
        await client1.Authenticate("player1", "pass1");
        await client2.Authenticate("player2", "pass2");
        
        await client1.CreateRoom("TestRoom");
        await client2.JoinRoom(client1.CurrentRoomId);
        
        // Act
        bool client2ReceivedUpdate = false;
        client2.OnPlayerUpdate += (update) => {
            if (update.SessionId == client1.SessionId)
                client2ReceivedUpdate = true;
        };
        
        await client1.SendPositionUpdate(10, 20, 30, 0, 0, 0, 1);
        await Task.Delay(100); // Wait for propagation
        
        // Assert
        Assert.IsTrue(client2ReceivedUpdate);
    }
}
```

### 11.3 Load Testing

```csharp
[TestClass]
public class LoadTests
{
    [TestMethod]
    public async Task Server_ShouldHandle_100ConcurrentClients()
    {
        // Arrange
        var clients = new List<RacingClient>();
        var tasks = new List<Task>();
        
        // Act
        for (int i = 0; i < 100; i++)
        {
            var client = new RacingClient();
            clients.Add(client);
            
            tasks.Add(Task.Run(async () =>
            {
                await client.Connect("localhost");
                await client.Authenticate($"player{i}", "password");
                
                // Send updates for 30 seconds
                var endTime = DateTime.Now.AddSeconds(30);
                while (DateTime.Now < endTime)
                {
                    await client.SendPositionUpdate(i, 0, 0, 0, 0, 0, 1);
                    await Task.Delay(50); // 20 Hz
                }
            }));
        }
        
        // Wait for all clients to complete
        await Task.WhenAll(tasks);
        
        // Assert
        foreach (var client in clients)
        {
            Assert.IsTrue(client.IsAuthenticated);
        }
    }
}
```

### 11.4 Deployment Checklist

#### Server Deployment

1. **Certificate Configuration**:
   ```bash
   # Set production environment variables
   export SERVER_HOSTNAME="your-racing-server.com"
   export SERVER_PUBLIC_IP="your-public-ip"
   
   # Or use custom certificate
   cp your-certificate.pfx server.pfx
   ```

2. **Firewall Configuration**:
   ```bash
   # Open required ports
   ufw allow 443/tcp    # TLS
   ufw allow 443/udp    # Encrypted game data
   ufw allow 8080/tcp   # Dashboard (LAN only)
   ```

3. **Service Configuration**:
   ```bash
   # Create systemd service
   sudo nano /etc/systemd/system/racing-server.service
   
   [Unit]
   Description=MP Racing Server
   After=network.target
   
   [Service]
   Type=simple
   User=racing
   WorkingDirectory=/opt/racing-server
   ExecStart=/usr/bin/dotnet MP-Server.dll
   Restart=always
   RestartSec=10
   
   [Install]
   WantedBy=multi-user.target
   ```

#### Client Deployment

1. **Certificate Handling**:
   ```csharp
   // Production: Use certificate pinning
   public bool ValidateServerCertificate(object sender, X509Certificate certificate, 
                                        X509Chain chain, SslPolicyErrors sslPolicyErrors)
   {
   #if DEBUG
       return true; // Accept all in debug builds
   #else
       // Validate against pinned certificate
       return certificate.GetCertHashString() == EXPECTED_CERT_HASH;
   #endif
   }
   ```

2. **Configuration Management**:
   ```csharp
   public class ClientConfig
   {
       public string ServerHost { get; set; } = "your-server.com";
       public int ServerPort { get; set; } = 443;
       public bool EnableDebugLogging { get; set; } = false;
       public string CertificateHash { get; set; } = "PRODUCTION_CERT_HASH";
   }
   ```

3. **Error Reporting**:
   ```csharp
   public class ErrorReporter
   {
       public static void ReportError(string component, Exception ex, Dictionary<string, string> context = null)
       {
   #if !DEBUG
           // Send to error reporting service
           var errorData = new
           {
               Component = component,
               Message = ex.Message,
               StackTrace = ex.StackTrace,
               Context = context,
               ClientVersion = Application.version,
               Platform = Application.platform.ToString()
           };
           
           // Send to your error reporting service
   #endif
       }
   }
   ```

### 11.5 Monitoring and Analytics

#### Client-Side Metrics

```csharp
public class NetworkMetrics
{
    public float LatencyMs { get; set; }
    public int PacketsSent { get; set; }
    public int PacketsReceived { get; set; }
    public int PacketsLost { get; set; }
    public float PacketLossPercentage => (float)PacketsLost / PacketsSent * 100f;
    
    public void RecordLatency(DateTime sendTime)
    {
        LatencyMs = (float)(DateTime.Now - sendTime).TotalMilliseconds;
    }
    
    public void RecordPacketSent()
    {
        PacketsSent++;
    }
    
    public void RecordPacketReceived()
    {
        PacketsReceived++;
    }
    
    public void RecordPacketLost()
    {
        PacketsLost++;
    }
}
```

#### Telemetry Collection

```csharp
public class TelemetryCollector
{
    private readonly Timer _reportTimer;
    private readonly NetworkMetrics _metrics;
    
    public TelemetryCollector(NetworkMetrics metrics)
    {
        _metrics = metrics;
        _reportTimer = new Timer(SendTelemetry, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }
    
    private void SendTelemetry(object state)
    {
        var telemetry = new
        {
            Timestamp = DateTime.UtcNow,
            Metrics = _metrics,
            Platform = Environment.OSVersion.Platform.ToString(),
            NetFramework = Environment.Version.ToString(),
            ServerHost = _serverHost
        };
        
        // Send to analytics service (async, non-blocking)
        _ = Task.Run(() => SendTelemetryAsync(telemetry));
    }
    
    private async Task SendTelemetryAsync(object telemetry)
    {
        try
        {
            // Send to your analytics endpoint
            var json = JsonSerializer.Serialize(telemetry);
            using var client = new HttpClient();
            await client.PostAsync("https://your-analytics.com/api/telemetry", 
                                   new StringContent(json, Encoding.UTF8, "application/json"));
        }
        catch
        {
            // Silently fail - don't impact gameplay
        }
    }
}
```

---

This comprehensive guide provides everything needed to implement a secure, production-ready client for the MP-Server racing platform. The implementation covers all security aspects, includes complete working examples for multiple platforms, and provides thorough testing and deployment guidance.

For additional support or questions about specific implementation details, refer to the main documentation or the server's dashboard interface for real-time monitoring and troubleshooting.
