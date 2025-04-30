import socket
import threading
from collections import defaultdict

class RelayServer:
    def __init__(self, host='0.0.0.0', port=7778):
        self.host = host
        self.port = port
        self.clients = {}  # peer_id -> (public_ip, public_port, local_ip, local_port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind((host, port))
        
    def start(self):
        print(f"Relay server started on {self.host}:{self.port}")
        threading.Thread(target=self.listen, daemon=True).start()
        
    def listen(self):
        while True:
            data, addr = self.sock.recvfrom(1024)
            message = data.decode('utf-8')
            self.handle_message(message, addr)
            
    def handle_message(self, message, addr):
        parts = message.split('|')
        
        if parts[0] == "REGISTER":
            # Format: REGISTER|local_ip|local_port
            peer_id = f"{addr[0]}:{addr[1]}"
            self.clients[peer_id] = (addr[0], addr[1], parts[1], int(parts[2]))
            response = f"REGISTERED|{addr[0]}|{addr[1]}"
            self.sock.sendto(response.encode('utf-8'), addr)
            
        elif parts[0] == "HOST":
            # Format: HOST|public_endpoint (IP:PORT)
            peer_id = f"{addr[0]}:{addr[1]}"
            if peer_id in self.clients and len(parts) > 1:
                # Parse the endpoint which comes as a single string "IP:PORT"
                try:
                    endpoint_parts = parts[1].split(':')
                    if len(endpoint_parts) == 2:
                        public_ip = endpoint_parts[0]
                        public_port = int(endpoint_parts[1])
                        self.clients[peer_id] = (public_ip, public_port, 
                                             self.clients[peer_id][2], self.clients[peer_id][3])
                        print(f"Host registered: {peer_id} -> {public_ip}:{public_port}")
                except Exception as e:
                    print(f"Error parsing HOST message: {e}")
                
        elif parts[0] == "REQUEST_PEER":
            # Format: REQUEST_PEER|peer_id
            requester_id = f"{addr[0]}:{addr[1]}"
            target_id = parts[1]
            
            if target_id in self.clients:
                target = self.clients[target_id]
                # Send peer info to requester
                info = f"PEER_INFO|{target_id}|{target[0]}|{target[1]}|{target[2]}|{target[3]}"
                self.sock.sendto(info.encode('utf-8'), addr)
                
                # Also notify the target about the requester
                requester = self.clients[requester_id]
                info = f"PEER_INFO|{requester_id}|{requester[0]}|{requester[1]}|{requester[2]}|{requester[3]}"
                self.sock.sendto(info.encode('utf-8'), (target[0], target[1]))
                
        elif parts[0] == "JOIN":
            # Format: JOIN|host_id|joiner_endpoint
            if len(parts) >= 2:
                host_id = parts[1]
                if host_id in self.clients:
                    # Send the host information to the joiner
                    host_info = self.clients[host_id]
                    info = f"PEER_INFO|{host_id}|{host_info[0]}|{host_info[1]}|{host_info[2]}|{host_info[3]}"
                    self.sock.sendto(info.encode('utf-8'), addr)
                    
                    # Also notify the host about the joiner
                    joiner_id = f"{addr[0]}:{addr[1]}"
                    if joiner_id in self.clients:
                        joiner = self.clients[joiner_id]
                        info = f"PEER_INFO|{joiner_id}|{joiner[0]}|{joiner[1]}|{joiner[2]}|{joiner[3]}"
                        self.sock.sendto(info.encode('utf-8'), (host_info[0], host_info[1]))
                        print(f"Join request: {joiner_id} -> {host_id}")
                
        elif parts[0] == "RELAY":
            # Format: RELAY|dest_peer_id|message
            if len(parts) >= 3:
                dest_peer_id = parts[1]
                if dest_peer_id in self.clients:
                    dest = self.clients[dest_peer_id]
                    relay_message = '|'.join(parts[2:])
                    self.sock.sendto(relay_message.encode('utf-8'), (dest[0], dest[1]))
                    
        elif parts[0] == "LIST_SERVERS":
            # Return a list of servers that have called HOST
            server_list = []
            for server_id, server_data in self.clients.items():
                # Just add basic information for now
                # In a real implementation, you'd store and return more data
                server_list.append(f"{server_id}|Game Room|Host|1/4")
                
            response = "SERVER_LIST|" + "|".join(server_list)
            self.sock.sendto(response.encode('utf-8'), addr)
            print(f"Server list requested by {addr[0]}:{addr[1]}")
            
        elif parts[0] == "HEARTBEAT":
            peer_id = f"{addr[0]}:{addr[1]}"
            if peer_id in self.clients:
                # Update the last seen time (not implemented here but could be added)
                print(f"Heartbeat from {peer_id}")

if __name__ == "__main__":
    server = RelayServer()
    server.start()
    input("Press Enter to stop the server...\n")