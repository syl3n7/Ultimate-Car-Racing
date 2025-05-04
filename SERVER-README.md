# Ultimate Car Racing - Relay Server

![Game Server](https://img.shields.io/badge/Game%20Server-Racing-brightgreen)
![Python](https://img.shields.io/badge/Python-3.7%2B-blue)
![License](https://img.shields.io/badge/License-MIT-yellow)

A high-performance, multi-threaded network relay server for the Ultimate Car Racing game.

## Features

- **Dual Protocol Design**: TCP for reliable commands, UDP for fast position updates
- **Room Management**: Create, join, and manage game rooms with customizable player limits
- **Advanced Player Tracking**: Monitor positions, latency, and connection status
- **Admin Console**: Full-featured command line interface for server administration
- **Stress Testing**: Built-in tools for performance evaluation (development only)

## Getting Started

### Requirements
- Python 3.7 or higher

### Installation
1. Clone this repository
2. Run the server:

```bash
python relay.py
