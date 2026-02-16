# <img width="2471" height="394" alt="IP_Scout_github" src="https://github.com/user-attachments/assets/5ee653eb-3359-405e-bf5e-223881b559ad" />



IP Scout is a high-performance network scanner for Windows built with .NET Framework. It provides comprehensive network discovery with both traditional list-based results and an innovative radar-style visualization that displays devices based on network latency, making network topology analysis intuitive and efficient.

## Features

- Multi-threaded parallel scanning with configurable thread pools.
- Customizable port scanning with range support.
- Interactive network radar map with distance-based visualization.
- Context menu quick actions for SSH, RDP, HTTP, and network diagnostics.
- Advanced settings for scan parameters, timeouts, and display filters.
- Live scan updates with real-time device discovery.

## Installation

1. Download the latest release from the [Releases](../../releases) page.
2. Extract the archive to your desired location.
3. Run `IPScout.exe`.

## Usage

1. Open **IP Scout**.
2. Enter the IP range:
   - **Start IP:** The first IP address to scan (e.g., 192.168.1.1).
   - **End IP:** The last IP address to scan (e.g., 192.168.1.254).
3. Click **Scan**.
4. View results in either:
   - **Results List:** Traditional table view with all device information.
   - **Network Map:** Radar-style visualization with your computer in the center.
5. Right-click any device for quick actions:
   - Open in browser (HTTP/HTTPS).
   - Connect via SSH or Remote Desktop.
   - Access Windows file shares.
   - Run ping or traceroute.

## System Requirements

- Windows 10 or higher
- .NET Framework 4.7.2 or higher
- Network access to target subnet

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Author

Bentendo © 2026
