# DosBox Hard Disk Activity

A Raspberry Pi service that monitors file system activity (such as DOSBox accessing a mounted drive) and provides visual and audio feedback through GPIO pins and sound effects. This creates an authentic retro computer experience by simulating hard disk activity indicators and sounds.

## Features

- **File System Monitoring**: Watches specified directories for file changes, creations, deletions, and modifications
- **GPIO Control**: Drives an LED or other indicator through a GPIO pin to simulate hard disk activity
- **Audio Feedback**: Plays different sound effects for short bursts vs. sustained activity
- **Intelligent Activity Detection**: Distinguishes between brief file operations and sustained disk activity
- **Configurable**: Easy YAML-based configuration for directories, GPIO pins, and sound files
- **Cross-platform Audio**: Supports both Linux (aplay/paplay) and Windows (PowerShell) audio playback

## Use Cases

- Add visual/audio hard disk activity indicators to a DOSBox gaming setup
- Monitor file system activity on a Raspberry Pi retro gaming system
- Create an authentic vintage computer experience with LED indicators and sound effects
- Monitor any directory for file changes with customizable feedback

## Requirements

- **.NET 10.0 Runtime** (linux-arm64)
- **Raspberry Pi** (or compatible Linux ARM device with GPIO support)
- **Audio player**: `aplay`, `paplay`, or `ffplay` (Linux) / PowerShell (Windows)
- **Optional**: `soxi` or `ffprobe` for audio duration detection

## Installation

1. Clone this repository:
   ```bash
   git clone https://github.com/yourusername/DosBoxHardDiskActivity.git
   cd DosBoxHardDiskActivity
   ```

2. Install .NET 10.0 Runtime on your Raspberry Pi:
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --runtime dotnet
   ```

3. Build the project (or use the pre-built binaries):
   ```bash
   dotnet build -c Release
   ```

4. Copy the executable and configuration to your Raspberry Pi:
   ```bash
   scp -r bin/Release/net10.0/linux-arm64/publish/* pi@raspberrypi:~/DosBoxHardDiskActivity/
   ```

## Configuration

Edit the `config.yaml` file to customize the behavior:

```yaml
config:
  directories:
    - "~/drives"        # Directories to monitor (supports ~ and environment variables)
    - "/path/to/dosbox/c"
  gpio_pin: 4          # GPIO pin number for LED indicator

sounds:
  long_activity: "sounds/long.wav"    # Sound for sustained activity
  short_activity: "sounds/short.wav"   # Sound for brief activity
```

### Configuration Options

- **directories**: List of directories to monitor for file system activity. Supports:
  - Home directory expansion (`~`)
  - Environment variables
  - Subdirectories are monitored recursively

- **gpio_pin**: BCM GPIO pin number to control (default: 4)
  - Connect an LED with appropriate resistor to this pin
  - Pin will pulse during disk activity

- **sounds**: Audio files to play for different activity types
  - **long_activity**: Played when sustained disk activity is detected (3+ events in 3 seconds)
  - **short_activity**: Played for brief disk operations

## Sound Files

Place your sound files in the `sounds/` directory. The project looks for:
- `sounds/long.wav` - Sustained activity sound (e.g., continuous hard disk grinding)
- `sounds/short.wav` - Brief activity sound (e.g., single disk seek/click)

You can use any WAV, MP3, or other audio format supported by your system's audio player.

## Usage

### Running the Service

```bash
cd ~/DosBoxHardDiskActivity
./DosBoxHardDiskActivity
```

## License

See [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit pull requests or open issues for bugs and feature requests.