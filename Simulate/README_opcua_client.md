# OPC UA Python Client

This is a simple Python client for testing connectivity to an OPC UA server using the asyncua library.

## Prerequisites

- Python 3.7 or higher
- asyncua library

## Installation

Install the required dependencies:

```bash
pip install -r requirements.txt
```

## Usage

Run the client script:

```bash
python opcua_client.py
```

The script will attempt to connect to the OPC UA server at:
`opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer`

If the connection is successful, it will browse and display some nodes from the server.

## Troubleshooting

If you encounter connection issues:

1. Verify that the OPC UA server is running
2. Check if the hostname is correct (you might need to use IP address instead)
3. Ensure that the port 53530 is open and accessible
4. Check if any firewall is blocking the connection
