# <img width="1998" height="394" alt="IP_Scout" src="https://github.com/user-attachments/assets/2d5d5109-5628-42d0-b6da-aa93d2f8ea2e" />


IP Scout is a high-performance network scanner for Windows that provides comprehensive network discovery and visualization capabilities. Built with .NET Framework and Windows Forms, it offers both traditional list-based results and an innovative radar-style network map for intuitive network topology understanding.

## Features

- **Multi-threaded Network Scanning** - Parallel host discovery with configurable thread pools for optimal performance
- **Port Detection** - Customizable port scanning with range support and automatic service identification
- **Network Visualization** - Interactive radar-style map displaying devices based on network latency
- **Quick Actions** - Direct access to SSH, RDP, HTTP/HTTPS, Windows shares, and network diagnostics
- **Advanced Configuration** - Granular control over scan parameters, timeouts, and display filters
- **Professional Interface** - Clean Windows-style UI with comprehensive settings management

## Installation

1. Download the latest release from the [Releases]([../../releases](https://github.com/BentendoYT/IP-Scout/releases)) page.
2. Extract the archive to your desired location.
3. Run `IPScout.exe`.

No installation required - fully portable.

## Usage

### Basic Scanning

1. Enter the IP range in the provided fields (e.g., `192.168.1.1` to `192.168.1.254`).
2. Click **Scan** to begin network discovery.
3. View results in either the **Results List** or **Network Map** tab.

### Network Map

The Network Map provides a visual representation of discovered devices:
- Your computer appears in the center.
- Other devices are positioned based on ping latency (closer = faster response).
- Color coding indicates device status: Green (open ports), Blue (alive), Red (offline).
- Use mouse wheel to zoom in/out.
- Right-click devices for quick actions.

### Context Menu Actions

Right-click any discovered host to access:
- Open in Browser (HTTP/HTTPS)
- Connect via SSH
- Connect via Remote Desktop (RDP)
- Access Windows File Shares
- Continuous Ping
- Traceroute
- Copy IP Address or Hostname

## Configuration

Access **Settings** to customize:

### Scanning Tab
- Thread count and delay configuration
- Ping probe count and timeout values
- Dead host scanning options
- IP address filtering (.0 and .255 exclusion)

### Ports Tab
- Port connection timeout settings
- Adaptive timeout based on RTT
- Custom port ranges (supports comma-separated lists and ranges)

### Display Tab
- Filter results by status (all hosts, alive only, or hosts with open ports)
- Scan confirmation dialogs
- Completion statistics

## System Requirements

- Windows 10 or higher
- .NET Framework 4.7.2 or higher
- Network access to target subnet

## Technical Details

### Scanning Engine
- Asynchronous ICMP ping with configurable retry logic
- Parallel TCP port scanning with semaphore-based concurrency control
- DNS reverse lookup for hostname resolution
- Configurable timeout adaptation based on network latency

### Supported Protocols
- ICMP (ping)
- TCP port scanning
- DNS resolution

### Default Port Scan List
21 (FTP), 22 (SSH), 23 (Telnet), 80 (HTTP), 443 (HTTPS), 445 (SMB), 3389 (RDP), 5900 (VNC), 8080 (HTTP-Alt)

## Building from Source

1. Clone the repository.
2. Open `IPScout.sln` in Visual Studio 2019 or later.
3. Restore NuGet packages if prompted.
4. Build the solution (Release configuration recommended).
5. The executable will be located in `bin\Release\`.

## Known Limitations

- Windows-only application (requires .NET Framework)
- ICMP requires appropriate network permissions
- Very large subnet scans may be resource-intensive

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Author

Bentendo © 2026
