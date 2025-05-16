import socket
import threading
import select
from collections import defaultdict
import time
import json
import cmd
import datetime
import os
import sys
import tabulate

class RelayServer:
    def __init__(self, tcp_port=7777, udp_port=7778):
        self.tcp_port = tcp_port
        self.udp_port = udp_port
        self.clients = {}  # client_id -> {tcp_socket, public_ip, public_port, last_heartbeat}
        self.game_rooms = {}  # room_id -> {host_id, name, players: [player_ids], max_players}
        
        # Initialize TCP socket
        self.tcp_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.tcp_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.tcp_socket.bind(('0.0.0.0', tcp_port))
        self.tcp_socket.listen(10)
        
        # Initialize UDP socket
        self.udp_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.udp_socket.bind(('0.0.0.0', udp_port))
        
        # Client tracking
        self.clients_lock = threading.Lock()
        self.rooms_lock = threading.Lock()
        self.next_client_id = 1
        
        # Player data tracking
        self.player_positions = {}  # client_id -> {x, y, z, timestamp}
        self.player_latencies = {}  # client_id -> latency_ms
        self.player_names = {}      # client_id -> player_name

        # Add spawn position tracking
        self.room_spawn_positions = {}  # room_id -> {position_index: client_id}
        
    def start(self):
        print(f"Relay server started - TCP: 0.0.0.0:{self.tcp_port}, UDP: 0.0.0.0:{self.udp_port}")
        
        # Start TCP listener thread
        tcp_thread = threading.Thread(target=self.tcp_listen, daemon=True)
        tcp_thread.start()
        
        # Start UDP listener thread
        udp_thread = threading.Thread(target=self.udp_listen, daemon=True)
        udp_thread.start()
        
        # Start cleanup thread
        cleanup_thread = threading.Thread(target=self.cleanup_stale_clients, daemon=True)
        cleanup_thread.start()
        
    def cleanup_stale_clients(self):
        """Remove clients that haven't sent a heartbeat in a while"""
        TIMEOUT = 60  # seconds
        
        while True:
            time.sleep(10)  # Check every 10 seconds
            current_time = time.time()
            
            with self.clients_lock:
                stale_clients = []
                for client_id, client_data in self.clients.items():
                    if current_time - client_data['last_heartbeat'] > TIMEOUT:
                        stale_clients.append(client_id)
                
                for client_id in stale_clients:
                    print(f"Removing stale client: {client_id}")
                    self.remove_client(client_id)
    
    def remove_client(self, client_id):
        """Remove a client from all data structures"""
        rooms_affected = []
        
        with self.clients_lock:
            if client_id in self.clients:
                # Close TCP socket and remove from clients dictionary
                try:
                    self.clients[client_id]['tcp_socket'].close()
                except:
                    pass
                del self.clients[client_id]
        
        # Remove from any game rooms
        with self.rooms_lock:
            rooms_to_remove = []
            
            for room_id, room_data in self.game_rooms.items():
                # If this client is the host, mark room for removal
                if room_data['host_id'] == client_id:
                    rooms_to_remove.append(room_id)
                    rooms_affected.append(room_id)
                # Otherwise, just remove from player list
                elif client_id in room_data['players']:
                    room_data['players'].remove(client_id)
                    rooms_affected.append(room_id)
            
            # Remove any rooms where this client was the host
            for room_id in rooms_to_remove:
                del self.game_rooms[room_id]
        
        # Release any spawn positions this client was using
        for room_id in rooms_affected:
            self.release_spawn_position(room_id, client_id)
    
    def tcp_listen(self):
        """Handle incoming TCP connections"""
        while True:
            try:
                client_socket, addr = self.tcp_socket.accept()
                client_id = self.register_client(client_socket, addr)
                
                # Start a thread to handle this client's TCP messages
                client_thread = threading.Thread(
                    target=self.handle_tcp_client,
                    args=(client_socket, client_id),
                    daemon=True
                )
                client_thread.start()
                
            except Exception as e:
                print(f"TCP accept error: {e}")
    
    def register_client(self, tcp_socket, addr):
        """Register a new client with the server"""
        with self.clients_lock:
            client_id = f"client_{self.next_client_id}"
            self.next_client_id += 1
            
            self.clients[client_id] = {
                'tcp_socket': tcp_socket,
                'public_ip': addr[0],
                'public_port': addr[1],
                'last_heartbeat': time.time()
            }
            
            # Send client their ID
            self.send_tcp_message(tcp_socket, {
                'type': 'REGISTERED',
                'client_id': client_id,
                'public_ip': addr[0],
                'public_port': addr[1]
            })
            
            print(f"New client registered: {client_id} from {addr[0]}:{addr[1]}")
            return client_id
    
    def handle_tcp_client(self, client_socket, client_id):
        """Handle TCP messages from a connected client"""
        print(f"Starting to handle messages for client {client_id}")
        try:
            # Set socket to non-blocking
            client_socket.setblocking(False)
            
            buffer = b""
            
            while True:
                # Use select to wait for data
                ready = select.select([client_socket], [], [], 0.1)
                
                if ready[0]:
                    chunk = client_socket.recv(4096)
                    if not chunk:
                        # Connection closed
                        break
                    
                    buffer += chunk
                    
                    # Process any complete messages in the buffer
                    while b"\n" in buffer:
                        message, buffer = buffer.split(b"\n", 1)
                        if message:
                            try:
                                data = json.loads(message.decode('utf-8'))
                                self.handle_tcp_message(data, client_id)
                            except json.JSONDecodeError:
                                print(f"Invalid JSON from {client_id}: {message}")
        
        except Exception as e:
            print(f"TCP client error ({client_id}): {e}")
        finally:
            # Clean up when client disconnects
            self.remove_client(client_id)
            print(f"Client disconnected: {client_id} - Connection handling terminated")
    
    def handle_tcp_message(self, data, client_id):
        """Process a message received over TCP"""
        msg_type = data.get('type')
        
        # Update last seen time
        with self.clients_lock:
            if client_id in self.clients:
                self.clients[client_id]['last_heartbeat'] = time.time()
        
        if msg_type == 'HEARTBEAT':
            # Just acknowledge
            with self.clients_lock:
                if client_id in self.clients:
                    self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                        'type': 'HEARTBEAT_ACK'
                    })
        
        elif msg_type == 'HOST_GAME':
            # Client wants to host a game
            room_name = data.get('room_name', 'Game Room')
            max_players = data.get('max_players', 4)
            
            with self.rooms_lock:
                # Check if client already hosts a room
                existing_room_id = None
                for room_id, room_data in self.game_rooms.items():
                    if room_data['host_id'] == client_id:
                        existing_room_id = room_id
                        break
                        
                if existing_room_id:
                    print(f"Client {client_id} already hosts room {existing_room_id}, using that instead of creating new room")
                    room_id = existing_room_id
                else:
                    room_id = f"room_{len(self.game_rooms) + 1}"
                    self.game_rooms[room_id] = {
                        'host_id': client_id,
                        'name': room_name,
                        'players': [client_id],
                        'max_players': max_players
                    }
                    print(f"New game room created: {room_id} by {client_id}")
                        
                # Tell client they're now hosting
                with self.clients_lock:
                    if client_id in self.clients:
                        self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                            'type': 'GAME_HOSTED',
                            'room_id': room_id
                        })
        
        elif msg_type == 'LIST_GAMES':
            # Client wants list of available games
            with self.rooms_lock:
                room_list = []
                for room_id, room_data in self.game_rooms.items():
                    room_list.append({
                        'room_id': room_id,
                        'name': room_data['name'],
                        'host_id': room_data['host_id'],
                        'player_count': len(room_data['players']),
                        'max_players': room_data['max_players']
                    })
                
                with self.clients_lock:
                    if client_id in self.clients:
                        self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                            'type': 'GAME_LIST',
                            'rooms': room_list
                        })
        
        elif msg_type == 'JOIN_GAME':
            # Client wants to join a game
            room_id = data.get('room_id')
            
            with self.rooms_lock:
                if room_id in self.game_rooms:
                    room = self.game_rooms[room_id]
                    
                    # Check if room is full
                    if len(room['players']) >= room['max_players']:
                        # Room is full
                        with self.clients_lock:
                            if client_id in self.clients:
                                self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                                    'type': 'JOIN_FAILED',
                                    'reason': 'Room is full'
                                })
                        return
                    
                    # Add client to room
                    if client_id not in room['players']:
                        room['players'].append(client_id)
                    
                    # Notify the room host
                    host_id = room['host_id']
                    with self.clients_lock:
                        if host_id in self.clients and client_id in self.clients:
                            # Tell host about the new player
                            self.send_tcp_message(self.clients[host_id]['tcp_socket'], {
                                'type': 'PLAYER_JOINED',
                                'client_id': client_id
                            })
                            
                            # Tell the joining player about the host and other details
                            self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                                'type': 'JOINED_GAME',
                                'room_id': room_id,
                                'host_id': host_id
                            })
                            
                            print(f"Client {client_id} joined room {room_id}")
                else:
                    # Room doesn't exist
                    with self.clients_lock:
                        if client_id in self.clients:
                            self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                                'type': 'JOIN_FAILED',
                                'reason': 'Room not found'
                            })
        
        elif msg_type == 'RELAY_MESSAGE':
            # Client wants to relay a message to a specific client or all in room
            room_id = data.get('room_id')
            target_id = data.get('target_id')  # Optional - if None, send to all in room
            message = data.get('message')
            
            if not message:
                return
                
            # If target_id is specified, send just to that client
            if target_id:
                with self.clients_lock:
                    if target_id in self.clients:
                        self.send_tcp_message(self.clients[target_id]['tcp_socket'], {
                            'type': 'RELAY',
                            'from': client_id,
                            'message': message
                        })
            
            # Otherwise, send to all players in the room
            elif room_id:
                with self.rooms_lock:
                    if room_id in self.game_rooms:
                        room = self.game_rooms[room_id]
                        with self.clients_lock:
                            # Send to all players in the room except the sender
                            for player_id in room['players']:
                                if player_id != client_id and player_id in self.clients:
                                    try:
                                        # FIX: ADD THIS LINE - SEND THE MESSAGE!
                                        self.send_tcp_message(self.clients[player_id]['tcp_socket'], {
                                            'type': 'RELAY',
                                            'from': client_id,
                                            'message': message
                                        })
                                    except Exception as e:
                                        print(f"Error relaying message to {player_id}: {e}")
        
        elif msg_type == 'PING':
            # Send ping response
            timestamp = data.get('timestamp', 0)
            with self.clients_lock:
                if client_id in self.clients:
                    self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                        'type': 'PING_RESPONSE',
                        'timestamp': timestamp
                    })
                    
                    # Calculate and store latency
                    current_time = time.time() * 1000  # Convert to ms
                    if timestamp > 0:
                        latency = current_time - timestamp
                        self.player_latencies[client_id] = latency
        
        elif msg_type == 'PLAYER_INFO':
            # Store player name and any other info
            player_name = data.get('name')
            if player_name:
                self.player_names[client_id] = player_name
        
        elif msg_type == 'POSITION_UPDATE':
            # Store player position
            position = data.get('position')
            if position and isinstance(position, dict):
                self.player_positions[client_id] = {
                    'x': position.get('x', 0),
                    'y': position.get('y', 0),
                    'z': position.get('z', 0),
                    'timestamp': time.time()
                }

        elif msg_type == 'LEAVE_ROOM':
            # Client wants to leave a room
            room_id = data.get('room_id')
            
            with self.rooms_lock:
                if room_id in self.game_rooms:
                    room = self.game_rooms[room_id]
                    
                    # Remove client from player list
                    if client_id in room['players']:
                        room['players'].remove(client_id)
                        print(f"Client {client_id} left room {room_id}")
                        
                        # If room is now empty or host left, remove the room
                        if len(room['players']) == 0 or room['host_id'] == client_id:
                            del self.game_rooms[room_id]
                            print(f"Room {room_id} deleted - empty or host left")

        elif msg_type == 'START_GAME':
            room_id = data.get('room_id')
            
            with self.rooms_lock:
                if room_id in self.game_rooms:
                    room = self.game_rooms[room_id]
                    
                    # Only the host can start the game
                    if room['host_id'] != client_id:
                        return
                    
                    # Notify all players about the game start with their positions
                    with self.clients_lock:
                        # First make a complete player list for everyone
                        player_ids = room['players']
                        
                        for player_id in player_ids:
                            if player_id in self.clients:
                                # Assign a spawn position for this player
                                spawn_position = self.assign_spawn_position(room_id, player_id)
                                
                                # Send the game started message with position
                                player_data = {
                                    'type': 'GAME_STARTED',
                                    'player_ids': player_ids,
                                    'spawn_position': spawn_position
                                }
                                self.send_tcp_message(self.clients[player_id]['tcp_socket'], player_data)
                    
                    print(f"Game started in room {room_id}")

        elif msg_type == 'JOIN_ROOM':
            room_id = data.get('room_id')
            # ... existing code ...
            
            # If game is already running, assign a spawn position immediately
            if room.get('game_started', False):
                spawn_position = self.assign_spawn_position(room_id, client_id)
                response = {
                    'type': 'ROOM_JOINED',
                    'room_id': room_id,
                    'players': room['players'],
                    'game_started': True,
                    'spawn_position': spawn_position
                }
            else:
                response = {
                    'type': 'ROOM_JOINED',
                    'room_id': room_id,
                    'players': room['players'],
                    'game_started': False
                }
            
            self.send_tcp_message(client_socket, response)

        elif msg_type == 'GET_ROOM_PLAYERS':
            room_id = data.get('room_id')
            if room_id in self.game_rooms:
                room = self.game_rooms[room_id]
                players_list = room['players']
                
                # Send the player list to the requesting client
                with self.clients_lock:
                    if client_id in self.clients:
                        self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                            'type': 'ROOM_PLAYERS',
                            'players': players_list
                        })
    
    def udp_listen(self):
        """Handle incoming UDP datagrams"""
        while True:
            try:
                data, addr = self.udp_socket.recvfrom(2048)
                self.handle_udp_message(data, addr)
            except Exception as e:
                print(f"UDP receive error: {e}")
    
    def get_active_players_in_room(self, room_id):
        """Get a list of active players in a room"""
        if room_id not in self.game_rooms:
            return []
            
        room = self.game_rooms[room_id]
        active_players = []
        
        for player_id in room['players']:
            if player_id in self.clients:
                active_players.append(player_id)
        
        return active_players

 
    def handle_udp_message(self, data, addr):
        """Process a message received over UDP"""
        try:
            message = json.loads(data.decode('utf-8'))
            
            msg_type = message.get('type')
            client_id = message.get('client_id')
            
            if not client_id:
                return
            
            # Update UDP endpoint for this client
            with self.clients_lock:
                if client_id in self.clients:
                    # Add/update UDP endpoint info
                    self.clients[client_id]['udp_ip'] = addr[0]
                    self.clients[client_id]['udp_port'] = addr[1]
                    # Update last heartbeat time to keep connection alive
                    self.clients[client_id]['last_heartbeat'] = time.time()
            
            if msg_type == 'POSITION_UPDATE':
                # Store player position from UDP updates too
                position = message.get('position')
                if position and isinstance(position, dict):
                    self.player_positions[client_id] = {
                        'x': position.get('x', 0),
                        'y': position.get('y', 0),
                        'z': position.get('z', 0),
                        'timestamp': time.time()
                    }
            
            # Handle game data relay - find target and forward
            target_id = message.get('target_id')
            room_id = message.get('room_id')
            game_data = message.get('data')
            
            if target_id:
                # Send to specific target
                with self.clients_lock:
                    if target_id in self.clients and 'udp_ip' in self.clients[target_id]:
                        target_addr = (self.clients[target_id]['udp_ip'], self.clients[target_id]['udp_port'])
                        forwarded_data = {
                            'type': 'GAME_DATA',
                            'from': client_id,
                            'data': game_data
                        }
                        self.udp_socket.sendto(json.dumps(forwarded_data).encode('utf-8'), target_addr)
            
            elif room_id:
                # Send to all in room with optimized locking
                active_players = []
                
                with self.rooms_lock:
                    if room_id in self.game_rooms:
                        active_players = self.get_active_players_in_room(room_id)
                
                # Send the data outside the lock to prevent bottlenecks
                if active_players:
                    forwarded_data = {
                        'type': 'GAME_DATA',
                        'from': client_id,
                        'data': game_data
                    }
                    serialized_data = json.dumps(forwarded_data).encode('utf-8')
                    
                    with self.clients_lock:
                        for player_id in active_players:
                            # Skip sending back to the original sender
                            if player_id != client_id and player_id in self.clients:
                                # Make sure we have UDP info for this client
                                if 'udp_ip' in self.clients[player_id] and 'udp_port' in self.clients[player_id]:
                                    target_addr = (self.clients[player_id]['udp_ip'], self.clients[player_id]['udp_port'])
                                    try:
                                        self.udp_socket.sendto(serialized_data, target_addr)
                                    except Exception as e:
                                        print(f"Error forwarding UDP to {player_id}: {e}")
                
        except json.JSONDecodeError:
            print(f"Invalid UDP JSON from {addr}")
        except Exception as e:
            print(f"UDP message handling error: {e}")

    
    def send_tcp_message(self, socket, data):
        """Send a JSON message over TCP"""
        try:
            message = json.dumps(data).encode('utf-8') + b"\n"
            socket.sendall(message)
        except Exception as e:
            print(f"TCP send error: {e}")

    def kick_player(self, client_id):
        """Kick a player from the server"""
        with self.clients_lock:
            if client_id in self.clients:
                try:
                    # Send kick message
                    self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                        'type': 'KICKED',
                        'message': 'You have been kicked from the server'
                    })
                except:
                    pass
                
                # Remove the client
                self.remove_client(client_id)
                return True
            return False
            
    def broadcast_message(self, message):
        """Send a message to all connected clients"""
        with self.clients_lock:
            for client_id, client_data in self.clients.items():
                try:
                    self.send_tcp_message(client_data['tcp_socket'], {
                        'type': 'SERVER_MESSAGE',
                        'message': message
                    })
                except:
                    pass
                    
    def get_player_stats(self):
        """Get comprehensive stats about all players"""
        stats = []
        
        with self.clients_lock:
            for client_id, client_data in self.clients.items():
                # Basic info
                player_info = {
                    'id': client_id,
                    'name': self.player_names.get(client_id, 'Unknown'),
                    'ip': client_data.get('public_ip', 'Unknown'),
                    'connected_since': datetime.datetime.fromtimestamp(
                        client_data.get('last_heartbeat', 0) - 60).strftime('%H:%M:%S')
                }
                
                # Add latency if we have it
                if client_id in self.player_latencies:
                    player_info['latency'] = f"{int(self.player_latencies[client_id])}ms"
                else:
                    player_info['latency'] = "Unknown"
                
                # Add position if we have it
                if client_id in self.player_positions:
                    pos = self.player_positions[client_id]
                    player_info['position'] = f"({pos['x']:.1f}, {pos['y']:.1f}, {pos['z']:.1f})"
                else:
                    player_info['position'] = "Unknown"
                
                # Find which room they're in
                player_info['room'] = "None"
                with self.rooms_lock:
                    for room_id, room_data in self.game_rooms.items():
                        if client_id in room_data['players']:
                            player_info['room'] = room_id
                            if room_data['host_id'] == client_id:
                                player_info['role'] = "Host"
                            else:
                                player_info['role'] = "Player"
                            break
                    else:
                        player_info['role'] = "Lobby"
                
                stats.append(player_info)
        
        return stats

    def assign_spawn_position(self, room_id, client_id):
        """Find the first unoccupied spawn position in a room"""
        if room_id not in self.room_spawn_positions:
            self.room_spawn_positions[room_id] = {}
        
        # Track-aligned garage positions
        track_positions = [
            (66, -2, 0.8),   # Position 0
            (60, -2, 0.8),   # Position 1
            (54, -2, 0.8),   # Position 2
            (47, -2, 0.8),   # Position 3
            (41, -2, 0.8),   # Position 4
            (35, -2, 0.8),   # Position 5
            (28, -2, 0.8),   # Position 6
            (22, -2, 0.8),   # Position 7
            (16, -2, 0.8),   # Position 8
            (9, -2, 0.8),    # Position 9
            (3, -2, 0.8),    # Position 10
            (-3, -2, 0.8),   # Position 11
            (-9, -2, 0.8),   # Position 12
            (-15, -2, 0.8),  # Position 13
            (-22, -2, 0.8),  # Position 14
            (-28, -2, 0.8),  # Position 15
            (-34, -2, 0.8),  # Position 16
            (-41, -2, 0.8),  # Position 17
            (-47, -2, 0.8),  # Position 18
            (-54, -2, 0.8)   # Position 19
        ]
        
        # Find first unoccupied position index
        position_index = 0
        occupied_positions = self.room_spawn_positions[room_id]
        
        # Find the first available spot
        while position_index in occupied_positions and position_index < len(track_positions):
            position_index += 1
        
        # If all positions are taken, cycle back to the first one (but this shouldn't happen with max 20 players)
        if position_index >= len(track_positions):
            position_index = 0
            print(f"Warning: All spawn positions are occupied, reusing position 0 for client {client_id}")
        
        # Mark this position as occupied by this client
        occupied_positions[position_index] = client_id
        
        # Get the actual position coordinates from our track positions
        pos = track_positions[position_index]
        
        # Calculate the actual position coordinates
        spawn_position = {
            'x': pos[0],
            'y': pos[1],
            'z': pos[2],
            'index': position_index  # Include the index for reference
        }
        
        print(f"Assigned spawn position {position_index} (garage {position_index+1}) to client {client_id} in room {room_id}")
        return spawn_position

    def release_spawn_position(self, room_id, client_id):
        """Release a spawn position when a player leaves"""
        if room_id in self.room_spawn_positions:
            positions = self.room_spawn_positions[room_id]
            # Find and remove any positions occupied by this client
            positions_to_remove = [idx for idx, cid in positions.items() if cid == client_id]
            for idx in positions_to_remove:
                del positions[idx]
                print(f"Released spawn position {idx} from client {client_id} in room {room_id}")

    def handle_player_disconnect(self, room_id, client_id):
        # Release the spawn position
        self.release_spawn_position(room_id, client_id)
        
        # Notify other players about the disconnection
        with self.rooms_lock:
            if room_id in self.game_rooms:
                room = self.game_rooms[room_id]
                # Only notify if this player was part of the room
                if client_id in room['players']:
                    room['players'].remove(client_id)
                    
                    # Tell remaining players about the disconnection
                    message = {
                        'type': 'PLAYER_DISCONNECTED',
                        'player_id': client_id
                    }
                    
                    with self.clients_lock:
                        for player_id in room['players']:
                            if player_id in self.clients:
                                try:
                                    # FIX: ADD THIS LINE - SEND THE MESSAGE!
                                    self.send_tcp_message(self.clients[player_id]['tcp_socket'], message)
                                except Exception as e:
                                    print(f"Error sending disconnect notification to {player_id}: {e}")


class AdminConsole(cmd.Cmd):
    prompt = 'race-admin> '
    intro = 'Welcome to the Race Server Admin Console. Type help or ? to list commands.'

    def __init__(self, server):
        super().__init__()
        self.server = server
        self.server_running = True
    
    def do_players(self, arg):
        """List all connected players and their details"""
        player_stats = self.server.get_player_stats()
        
        if not player_stats:
            print("No players connected")
            return
            
        headers = ["ID", "Name", "IP", "Latency", "Position", "Room", "Role", "Connected"]
        table_data = []
        
        for player in player_stats:
            table_data.append([
                player['id'], 
                player['name'], 
                player['ip'], 
                player['latency'],
                player['position'],
                player['room'],
                player['role'],
                player['connected_since']
            ])
            
        print(tabulate.tabulate(table_data, headers=headers, tablefmt="pretty"))
    
    def do_rooms(self, arg):
        """List all game rooms"""
        with self.server.rooms_lock:
            if not self.server.game_rooms:
                print("No active game rooms")
                return
                
            headers = ["Room ID", "Name", "Host", "Players", "Max Players"]
            table_data = []
            
            for room_id, room_data in self.server.game_rooms.items():
                host_name = self.server.player_names.get(room_data['host_id'], room_data['host_id'])
                
                table_data.append([
                    room_id,
                    room_data['name'],
                    host_name,
                    f"{len(room_data['players'])}/{room_data['max_players']}",
                    room_data['max_players']
                ])
                
            print(tabulate.tabulate(table_data, headers=headers, tablefmt="pretty"))
    
    def do_kick(self, arg):
        """Kick a player: kick <player_id>"""
        if not arg:
            print("Please specify a player ID to kick")
            return
            
        client_id = arg.strip()
        if self.server.kick_player(client_id):
            print(f"Player {client_id} has been kicked")
        else:
            print(f"Player {client_id} not found")
    
    def do_broadcast(self, arg):
        """Send a message to all players: broadcast <message>"""
        if not arg:
            print("Please specify a message to broadcast")
            return
            
        self.server.broadcast_message(arg)
        print(f"Message broadcast: '{arg}'")
    
    def do_stats(self, arg):
        """Show server statistics"""
        with self.server.clients_lock:
            num_clients = len(self.server.clients)
            
        with self.server.rooms_lock:
            num_rooms = len(self.server.game_rooms)
            
        print(f"Server Statistics:")
        print(f"- TCP Port: {self.server.tcp_port}")
        print(f"- UDP Port: {self.server.udp_port}")
        print(f"- Connected Clients: {num_clients}")
        print(f"- Active Game Rooms: {num_rooms}")
        print(f"- Server Uptime: {datetime.datetime.now() - self.server.start_time}")
    
    def do_clear(self, arg):
        """Clear the console screen"""
        os.system('cls' if os.name == 'nt' else 'clear')
    
    def do_exit(self, arg):
        """Exit the admin console and shut down the server"""
        print("Shutting down server...")
        self.server_running = False
        return True

    def do_EOF(self, arg):
        """Exit on Ctrl-D"""
        print("\nShutting down server...")
        self.server_running = False
        return True

    def do_resetpos(self, arg):
        """Reset a player's position: resetpos <player_id>"""
        if not arg:
            print("Please specify a player ID to reset")
            return
            
        client_id = arg.strip()
        
        # Check if player exists
        with self.server.clients_lock:
            if client_id not in self.server.clients:
                print(f"Player {client_id} not found")
                return
        
        # Track-aligned garage positions
        track_positions = [
            (66, -2, 0.8),   # Position 0
            (60, -2, 0.8),   # Position 1
            (54, -2, 0.8),   # Position 2
            (47, -2, 0.8),   # Position 3
            (41, -2, 0.8),   # Position 4
            (35, -2, 0.8),   # Position 5
            (28, -2, 0.8),   # Position 6
            (22, -2, 0.8),   # Position 7
            (16, -2, 0.8),   # Position 8
            (9, -2, 0.8),    # Position 9
            (3, -2, 0.8),    # Position 10
            (-3, -2, 0.8),   # Position 11
            (-9, -2, 0.8),   # Position 12
            (-15, -2, 0.8),  # Position 13
            (-22, -2, 0.8),  # Position 14
            (-28, -2, 0.8),  # Position 15
            (-34, -2, 0.8),  # Position 16
            (-41, -2, 0.8),  # Position 17
            (-47, -2, 0.8),  # Position 18
            (-54, -2, 0.8)   # Position 19
        ]
        
        # Find which room the player is in
        player_room = None
        position_index = 0
        
        with self.server.rooms_lock:
            for room_id, room_data in self.server.game_rooms.items():
                if client_id in room_data['players']:
                    player_room = room_id
                    break
        
        # Default is first position if we can't find the player's assigned position
        position = {'x': track_positions[0][0], 'y': track_positions[0][1], 'z': track_positions[0][2]}
        
        # If in a room, look for their assigned spawn position
        if player_room and player_room in self.server.room_spawn_positions:
            spawn_positions = self.server.room_spawn_positions[player_room]
            for pos_idx, pos_client_id in spawn_positions.items():
                if pos_client_id == client_id:
                    if pos_idx < len(track_positions):
                        # Get the corresponding track position
                        pos = track_positions[pos_idx]
                        position = {
                            'x': pos[0],
                            'y': pos[1],
                            'z': pos[2]
                        }
                        position_index = pos_idx
                        print(f"Using assigned spawn position {pos_idx} (garage {pos_idx+1}) for player {client_id}")
                    break
        
        # Send position reset message to the client
        try:
            with self.server.clients_lock:
                if client_id in self.server.clients:
                    self.server.send_tcp_message(self.server.clients[client_id]['tcp_socket'], {
                        'type': 'RESET_POSITION',
                        'position': position
                    })
            
            # Update position in server's tracking
            self.server.player_positions[client_id] = {
                'x': position['x'],
                'y': position['y'],
                'z': position['z'],
                'timestamp': time.time()
            }
            
            print(f"Reset position for player {client_id} to garage {position_index+1} ({position['x']}, {position['y']}, {position['z']})")
        except Exception as e:
            print(f"Error resetting position: {e}")

if __name__ == "__main__":
    # Add tabulate dependency if not present
    try:
        import tabulate
    except ImportError:
        print("Installing required dependency: tabulate")
        import subprocess
        subprocess.check_call([sys.executable, "-m", "pip", "install", "tabulate"])
        import tabulate
    
    server = RelayServer()
    server.start_time = datetime.datetime.now()
    server.start()
    
    # Start admin console
    admin = AdminConsole(server)
    
    try:
        admin.cmdloop()
    except KeyboardInterrupt:
        print("\nServer shutting down...")
    
    # Keep server running until admin console exits
    while admin.server_running:
        time.sleep(1)