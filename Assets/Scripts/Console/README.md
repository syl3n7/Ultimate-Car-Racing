# Console Command System

This directory contains the in-game console command system for development and server configuration.

## Files

### ConsoleCommandSystem.cs
- **Purpose**: Core console command system implementation
- **Features**:
  - Command registration and execution
  - Server connection commands (`setserver`, `connect`, `disconnect`)
  - Help system (`help`, `clear`)
  - Real-time server configuration changes
- **Usage**: Attach to any GameObject in your scene

### ConsoleSetup.cs
- **Purpose**: UI setup and integration for the console system
- **Features**:
  - Input field and display area setup
  - Key binding (tilde key `~` to toggle console)
  - Auto-completion support
  - Command history
- **Usage**: Attach to a GameObject with the console UI components

## Usage

1. Add both scripts to GameObjects in your scene
2. Set up UI components (Input Field, Text Area, Panel)
3. Press `~` (tilde) in-game to open/close console
4. Type `help` for available commands

## Commands

- `setserver <hostname> <port>` - Configure server connection
- `connect` - Connect to configured server
- `disconnect` - Disconnect from current server
- `help` - Show available commands
- `clear` - Clear console output

## Notes

This system is designed for development use. Consider removing or securing it for production builds.
