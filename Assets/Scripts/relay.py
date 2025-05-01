import socket
import threading
import select
from collections import defaultdict
import time
import json

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
        with self.clients_lock:
            if client_id in self.clients:
                try:
                    # Close TCP socket
                    self.clients[client_id]['tcp_socket'].close()
                except:
                    pass
                
                # Remove from clients dictionary
                del self.clients[client_id]
        
        # Remove from any game rooms
        with self.rooms_lock:
            rooms_to_remove = []
            
            for room_id, room_data in self.game_rooms.items():
                # If this client is the host, mark room for removal
                if room_data['host_id'] == client_id:
                    rooms_to_remove.append(room_id)
                # Otherwise, just remove from player list
                elif client_id in room_data['players']:
                    room_data['players'].remove(client_id)
            
            # Remove any rooms where this client was the host
            for room_id in rooms_to_remove:
                del self.game_rooms[room_id]
                
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
                room_id = f"room_{len(self.game_rooms) + 1}"
                self.game_rooms[room_id] = {
                    'host_id': client_id,
                    'name': room_name,
                    'players': [client_id],
                    'max_players': max_players
                }
                
                # Tell client they're now hosting
                with self.clients_lock:
                    if client_id in self.clients:
                        self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                            'type': 'GAME_HOSTED',
                            'room_id': room_id
                        })
                
                print(f"New game room created: {room_id} by {client_id}")
        
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
                            for player_id in room['players']:
                                if player_id != client_id and player_id in self.clients:
                                    self.send_tcp_message(self.clients[player_id]['tcp_socket'], {
                                        'type': 'RELAY',
                                        'from': client_id,
                                        'message': message
                                    })
        
        elif msg_type == 'PING':
            # Send ping response
            timestamp = data.get('timestamp', 0)
            with self.clients_lock:
                if client_id in self.clients:
                    self.send_tcp_message(self.clients[client_id]['tcp_socket'], {
                        'type': 'PING_RESPONSE',
                        'timestamp': timestamp
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
            
            if not client_id or msg_type != 'GAME_DATA':
                return
            
            # Update UDP endpoint for this client
            with self.clients_lock:
                if client_id in self.clients:
                    # Add/update UDP endpoint info
                    self.clients[client_id]['udp_ip'] = addr[0]
                    self.clients[client_id]['udp_port'] = addr[1]
                    # Update last heartbeat time to keep connection alive
                    self.clients[client_id]['last_heartbeat'] = time.time()
            
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
                            if player_id != client_id and player_id in self.clients and 'udp_ip' in self.clients[player_id]:
                                target_addr = (self.clients[player_id]['udp_ip'], self.clients[player_id]['udp_port'])
                                try:
                                    self.udp_socket.sendto(serialized_data, target_addr)
                                except Exception as e:
                                    print(f"Error sending UDP data to {player_id}: {e}")
                
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

if __name__ == "__main__":
    server = RelayServer()
    server.start()
    
    try:
        # Keep the main thread alive
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("Server shutting down...")