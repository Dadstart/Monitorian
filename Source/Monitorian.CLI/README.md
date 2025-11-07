# Monitorian CLI

A command-line interface for controlling monitor brightness and contrast using DDC/CI protocol.

## Features

- Get brightness and contrast values from monitors
- Set brightness and contrast values for individual or all monitors
- List all available monitors with their capabilities
- Simple command-line interface
- Direct DDC/CI communication with monitors

## Usage

### Basic Commands

```bash
# List all monitors
monitorian-cli list

# Get brightness from all monitors
monitorian-cli get --brightness

# Get brightness and contrast from the same command
monitorian-cli get --brightness --contrast

# Get brightness from a specific monitor
monitorian-cli get --brightness --monitor "MONITOR\\DISPLAY1\\4&12345678&0&UID1"

# Set brightness for all monitors to 50%
monitorian-cli set --brightness 50

# Set brightness for a specific monitor
monitorian-cli set --brightness 75 --monitor "MONITOR\\DISPLAY1\\4&12345678&0&UID1"

# Set brightness and contrast together
monitorian-cli set --brightness 50 --contrast 70
```

### Examples

```bash
# List all monitors
monitorian-cli list

# Get brightness from all monitors
monitorian-cli get -b

# Get contrast from all monitors
monitorian-cli get -c

# Set brightness for all monitors to 50%
monitorian-cli set -b 50

# Set contrast for all monitors to 75%
monitorian-cli set -c 75
```

## Output Format

### List Command
Shows detailed information about all monitors including:
- Device Instance ID
- Monitor name and type (Internal/External)
- Current brightness and contrast values
- Support status for brightness and contrast control
- Summary statistics

### Get Command
Output format: `[Device Instance ID] [Monitor name] [Value] [B/C]`

- `B` indicates brightness
- `C` indicates contrast
- `(Internal)` indicates internal monitor (laptop display)
- `(Unreachable)` indicates monitor cannot be controlled

### Set Command
Shows success/failure status for each operation:
- `✓` indicates successful operation
- `✗` indicates failed operation with error message

## Requirements

- Windows 7 or newer
- .NET Framework 4.8
- External monitors must support DDC/CI for brightness/contrast control

## Building

The CLI application is part of the Monitorian solution. Build it using:

```bash
dotnet build Source/Monitorian.CLI/Monitorian.CLI.csproj
```

Or build the entire solution:

```bash
dotnet build Source/Monitorian.sln
```

## Error Handling

The CLI provides comprehensive error handling:

- Invalid device instance IDs are reported with available monitor list
- Unsupported operations (e.g., contrast on unsupported monitors) are clearly indicated
- Network/communication errors are reported with details
- Verbose mode provides stack traces for debugging

## Implementation

This CLI tool provides a standalone implementation that:

- Uses direct Windows API calls for DDC/CI communication
- Supports physical monitor enumeration and control
- Handles brightness and contrast adjustment via DDC/CI protocol
- Provides simple command-line interface for automation
- Works independently of the main Monitorian application
