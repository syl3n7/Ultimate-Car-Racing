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
        self.game_rooms = {}  # room_id -> {host_id, name, players: [player_ids], max_players, game_started}
        
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

        # Spawn position tracking
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
                try:
                    self.clients[client_id]['tcp_socket'].close()
                except:
                    pass
                del self.clients[client_id]
        
        # Remove from any game rooms and notify other players
        with self.rooms_lock:
            rooms_to_remove = []
            
            for room_id, room_data in self.game_rooms.items():
                if client_id in room_data['players']:
                    rooms_affected.append(room_id)
                    room_data['players'].remove(client_id)
                    
                    # Notify other players about the disconnection
                    for player_id in room_data['players']:
                        with self.clients_lock:
                            if player_id in self.clients:
                                try:
                                    self.send_tcp_message(self.clients[player_id]['tcp_socket'], {
                                        'type': 'PLAYER_DISCONNECTED',
                                        'player_id': client_id
                                    })
                                except Exception as e:
                                    print(f"Error sending disconnect notification: {e}")
                    
                    # If host left or room is empty, mark for removal
                    if room_data['host_id'] == client_id or not room_data['players']:
                        rooms_to_remove.append(room_id)
            
            # Remove empty rooms
            for room_id in rooms_to_remove:
                del self.game_rooms[room_id]
                if room_id in self.room_spawn_positions:
                    del self.room_spawn_positions[room_id]
        
        # Release any spawn positions
        for room_id in rooms_affected:
            self.release_spawn_position(room_id, client_id)
    
    def tcp_listen(self):
        """Handle incoming TCP connections"""
        while True:
            try:
                client_socket, addr = self.tcp_socket.accept()
                client_id = self.register_client(client_socket, addr)
                
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
            
            self.send_tcp_message(tcp_socket, {
                'type': 'REGISTERED',
                'client_id': client_id
            })
            
            print(f"New client registered: {client_id} from {addr[0]}:{addr[1]}")
            return client_id
    
    def handle_tcp_client(self, client_socket, client_id):
        """Handle TCP messages from a connected client"""
        try:
            client_socket.setblocking(False)
            buffer = b""
            
            while True:
                ready = select.select([client_socket], [], [], 0.1)
                
                if ready[0]:
                    chunk = client_socket.recv(4096)
                    if not chunk:
                        break
                    
                    buffer += chunk
                    
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
            self.remove_client(client_id)
            print(f"Client disconnected: {client_id}")
    
    def handle_tcp_message(self, data, client_id):
        """Process a message received over TCP"""
        msg_type = data.get('type')
        
        # Update last seen time
        with self.clients_lock:
            if client_id in self.clients:
                self.clients[client_id]['last_heartbeat'] = time.time()
        
        if msg_type == 'HEARTBEAT':
            with self.clients_lock:
                if client_id in self.clients:
                    self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                        'type': 'HEARTBEAT_ACK'
                    })
        
        elif msg_type == 'HOST_GAME':
            room_name = data.get('room_name', 'Game Room')
            max_players = data.get('max_players', 4)
            
            with self.rooms_lock:
                # Create or find existing room for this host
                room_id = None
                for rid, room in self.game_rooms.items():
                    if room['host_id'] == client_id:
                        room_id = rid
                        break
                
                if not room_id:
                    room_id = f"room_{len(self.game_rooms) + 1}"
                    self.game_rooms[room_id] = {
                        'host_id': client_id,
                        'name': room_name,
                        'players': [client_id],
                        'max_players': max_players,
                        'game_started': False
                    }
                    print(f"New room created: {room_id}")
                
                # Send response
                with self.clients_lock:
                    if client_id in self.clients:
                        self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                            'type': 'GAME_HOSTED',
                            'room_id': room_id
                        })
        
        elif msg_type == 'LIST_GAMES':
            with self.rooms_lock:
                room_list = []
                for room_id, room_data in self.game_rooms.items():
                    room_list.append({
                        'room_id': room_id,
                        'name': room_data['name'],
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
            room_id = data.get('room_id')
            
            with self.rooms_lock:
                if room_id in self.game_rooms:
                    room = self.game_rooms[room_id]
                    
                    if len(room['players']) >= room['max_players']:
                        with self.clients_lock:
                            if client_id in self.clients:
                                self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                                    'type': 'JOIN_FAILED',
                                    'reason': 'Room is full'
                                })
                        return
                    
                    if client_id not in room['players']:
                        room['players'].append(client_id)
                    
                    # Notify all players in the room about the new player
                    player_list = room['players'].copy()
                    host_id = room['host_id']
                    
                    with self.clients_lock:
                        # Tell the new player about all existing players
                        if client_id in self.clients:
                            self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                                'type': 'JOINED_GAME',
                                'room_id': room_id,
                                'host_id': host_id,
                                'players': player_list,
                                'game_started': room['game_started']
                            })
                        
                        # Tell existing players about the new player
                        for player_id in room['players']:
                            if player_id != client_id and player_id in self.clients:
                                try:
                                    self.send_tcp_message(self.clients[player_id]['tcp_socket'], {
                                        'type': 'PLAYER_JOINED',
                                        'client_id': client_id
                                    })
                                except Exception as e:
                                    print(f"Error notifying player {player_id}: {e}")
                    
                    print(f"Client {client_id} joined room {room_id}")
                else:
                    with self.clients_lock:
                        if client_id in self.clients:
                            self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                                'type': 'JOIN_FAILED',
                                'reason': 'Room not found'
                            })
        
        elif msg_type == 'RELAY_MESSAGE':
            room_id = data.get('room_id')
            target_id = data.get('target_id')
            message = data.get('message')
            
            if not message:
                return
                
            if target_id:
                with self.clients_lock:
                    if target_id in self.clients:
                        self.send_tcp_message(self.clients[target_id]['tcp_socket'], {
                            'type': 'RELAY',
                            'from': client_id,
                            'message': message
                        })
            elif room_id:
                with self.rooms_lock:
                    if room_id in self.game_rooms:
                        room = self.game_rooms[room_id]
                        with self.clients_lock:
                            for player_id in room['players']:
                                if player_id != client_id and player_id in self.clients:
                                    try:
                                        self.send_tcp_message(self.clients[player_id]['tcp_socket'], {
                                            'type': 'RELAY',
                                            'from': client_id,
                                            'message': message
                                        })
                                    except Exception as e:
                                        print(f"Error relaying message: {e}")
        
        elif msg_type == 'PING':
            timestamp = data.get('timestamp', 0)
            with self.clients_lock:
                if client_id in self.clients:
                    self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                        'type': 'PING_RESPONSE',
                        'timestamp': timestamp
                    })
                    
                    current_time = time.time() * 1000
                    if timestamp > 0:
                        self.player_latencies[client_id] = current_time - timestamp
        
        elif msg_type == 'PLAYER_INFO':
            player_name = data.get('name')
            if player_name:
                self.player_names[client_id] = player_name
        
        elif msg_type == 'LEAVE_ROOM':
            room_id = data.get('room_id')
            
            with self.rooms_lock:
                if room_id in self.game_rooms:
                    room = self.game_rooms[room_id]
                    
                    if client_id in room['players']:
                        room['players'].remove(client_id)
                        print(f"Client {client_id} left room {room_id}")
                        
                        # Notify other players
                        with self.clients_lock:
                            for player_id in room['players']:
                                if player_id in self.clients:
                                    try:
                                        self.send_tcp_message(self.clients[player_id]['tcp_socket'], {
                                            'type': 'PLAYER_DISCONNECTED',
                                            'player_id': client_id
                                        })
                                    except Exception as e:
                                        print(f"Error notifying player {player_id}: {e}")
                        
                        if len(room['players']) == 0 or room['host_id'] == client_id:
                            del self.game_rooms[room_id]
                            if room_id in self.room_spawn_positions:
                                del self.room_spawn_positions[room_id]
                            print(f"Room {room_id} deleted")

        elif msg_type == 'START_GAME':
            room_id = data.get('room_id')
            
            with self.rooms_lock:
                if room_id in self.game_rooms:
                    room = self.game_rooms[room_id]
                    
                    if room['host_id'] != client_id:
                        return
                    
                    # Mark game as started
                    room['game_started'] = True
                    
                    # Assign spawn positions and notify all players
                    player_ids = room['players'].copy()
                    
                    with self.clients_lock:
                        for player_id in player_ids:
                            if player_id in self.clients:
                                spawn_position = self.assign_spawn_position(room_id, player_id)
                                
                                self.send_tcp_message(self.clients[player_id]['tcp_socket'], {
                                    'type': 'GAME_STARTED',
                                    'player_ids': player_ids,
                                    'spawn_position': spawn_position
                                })
                    
                    print(f"Game started in room {room_id}")

        elif msg_type == 'GET_ROOM_PLAYERS':
            room_id = data.get('room_id')
            if room_id in self.game_rooms:
                room = self.game_rooms[room_id]
                
                with self.clients_lock:
                    if client_id in self.clients:
                        self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                            'type': 'ROOM_PLAYERS',
                            'players': room['players']
                        })
    
    def udp_listen(self):
        """Handle incoming UDP datagrams"""
        while True:
            try:
                data, addr = self.udp_socket.recvfrom(2048)
                self.handle_udp_message(data, addr)
            except Exception as e:
                print(f"UDP receive error: {e}")
    
    def handle_udp_message(self, data, addr):
        """Process a message received over UDP"""
        try:
            message = json.loads(data.decode('utf-8'))
            msg_type = message.get('type')
            client_id = message.get('client_id')
            room_id = message.get('room_id')
            
            if not client_id:
                return
            
            # Update UDP endpoint
            with self.clients_lock:
                if client_id in self.clients:
                    self.clients[client_id]['udp_ip'] = addr[0]
                    self.clients[client_id]['udp_port'] = addr[1]
                    self.clients[client_id]['last_heartbeat'] = time.time()
            
            # Handle game data forwarding
            if msg_type == 'GAME_DATA':
                game_data = message.get('data')
                target_id = message.get('target_id')
                
                if target_id:
                    # Send to specific target
                    with self.clients_lock:
                        if target_id in self.clients and 'udp_ip' in self.clients[target_id]:
                            target_addr = (self.clients[target_id]['udp_ip'], self.clients[target_id]['udp_port'])
                            self.udp_socket.sendto(json.dumps({
                                'type': 'GAME_DATA',
                                'from': client_id,
                                'data': game_data
                            }).encode('utf-8'), target_addr)
                
                elif room_id:
                    # Send to all in room except sender
                    with self.rooms_lock:
                        if room_id in self.game_rooms:
                            player_ids = [pid for pid in self.game_rooms[room_id]['players'] if pid != client_id]
                            
                            # Prepare message once
                            forwarded_data = json.dumps({
                                'type': 'GAME_DATA',
                                'from': client_id,
                                'data': game_data
                            }).encode('utf-8')
                            
                            # Send to all relevant players
                            with self.clients_lock:
                                for player_id in player_ids:
                                    if player_id in self.clients and 'udp_ip' in self.clients[player_id]:
                                        try:
                                            target_addr = (self.clients[player_id]['udp_ip'], self.clients[player_id]['udp_port'])
                                            self.udp_socket.sendto(forwarded_data, target_addr)
                                        except Exception as e:
                                            print(f"Error forwarding UDP: {e}")
        
        except json.JSONDecodeError:
            print(f"Invalid UDP JSON from {addr}")
        except Exception as e:
            print(f"UDP message handling error: {e}")

    def assign_spawn_position(self, room_id, client_id):
        """Assign a spawn position to a player"""
        if room_id not in self.room_spawn_positions:
            self.room_spawn_positions[room_id] = {}
        
        # Track-aligned garage positions
        track_positions = [
            (66, -2, 0.8), (60, -2, 0.8), (54, -2, 0.8), (47, -2, 0.8), (41, -2, 0.8),
            (35, -2, 0.8), (28, -2, 0.8), (22, -2, 0.8), (16, -2, 0.8), (9, -2, 0.8),
            (3, -2, 0.8), (-3, -2, 0.8), (-9, -2, 0.8), (-15, -2, 0.8), (-22, -2, 0.8),
            (-28, -2, 0.8), (-34, -2, 0.8), (-41, -2, 0.8), (-47, -2, 0.8), (-54, -2, 0.8)
        ]
        
        # Find first available position
        position_index = 0
        occupied = self.room_spawn_positions[room_id]
        
        while position_index in occupied and position_index < len(track_positions):
            position_index += 1
        
        if position_index >= len(track_positions):
            position_index = 0
            print(f"Warning: All spawn positions occupied, reusing position 0")
        
        # Assign position
        occupied[position_index] = client_id
        pos = track_positions[position_index]
        
        return {
            'x': pos[0],
            'y': pos[1],
            'z': pos[2],
            'index': position_index
        }

    def release_spawn_position(self, room_id, client_id):
        """Release a spawn position when a player leaves"""
        if room_id in self.room_spawn_positions:
            positions = self.room_spawn_positions[room_id]
            to_remove = [idx for idx, cid in positions.items() if cid == client_id]
            for idx in to_remove:
                del positions[idx]
                print(f"Released spawn position {idx} from {client_id}")

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
                    self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                        'type': 'KICKED',
                        'message': 'You have been kicked'
                    })
                except:
                    pass
                
                self.remove_client(client_id)
                return True
        return False

    def get_player_stats(self):
        """Get comprehensive stats about all players"""
        stats = []
        
        with self.clients_lock:
            for client_id, client_data in self.clients.items():
                player_info = {
                    'id': client_id,
                    'name': self.player_names.get(client_id, 'Unknown'),
                    'ip': client_data.get('public_ip', 'Unknown'),
                    'connected_since': datetime.datetime.fromtimestamp(
                        client_data.get('last_heartbeat', 0) - 60).strftime('%H:%M:%S')
                }
                
                if client_id in self.player_latencies:
                    player_info['latency'] = f"{int(self.player_latencies[client_id])}ms"
                else:
                    player_info['latency'] = "Unknown"
                
                if client_id in self.player_positions:
                    pos = self.player_positions[client_id]
                    player_info['position'] = f"({pos['x']:.1f}, {pos['y']:.1f}, {pos['z']:.1f})"
                else:
                    player_info['position'] = "Unknown"
                
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
            (66, -2, 0.8), (60, -2, 0.8), (54, -2, 0.8), (47, -2, 0.8), (41, -2, 0.8),
            (35, -2, 0.8), (28, -2, 0.8), (22, -2, 0.8), (16, -2, 0.8), (9, -2, 0.8),
            (3, -2, 0.8), (-3, -2, 0.8), (-9, -2, 0.8), (-15, -2, 0.8), (-22, -2, 0.8),
            (-28, -2, 0.8), (-34, -2, 0.8), (-41, -2, 0.8), (-47, -2, 0.8), (-54, -2, 0.8)
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
    try:
        import tabulate
    except ImportError:
        print("Installing tabulate...")
        import subprocess
        subprocess.check_call([sys.executable, "-m", "pip", "install", "tabulate"])
        import tabulate
    
    server = RelayServer()
    server.start_time = datetime.datetime.now()
    server.start()
    
    admin = AdminConsole(server)
    
    try:
        admin.cmdloop()
    except KeyboardInterrupt:
        print("\nServer shutting down...")
    
    while admin.server_running:
        time.sleep(1)