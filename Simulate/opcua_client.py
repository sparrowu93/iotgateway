#!/usr/bin/env python3
"""
OPC UA Client to test connectivity to the local simulated OPC UA server
Server URL: opc.tcp://localhost:4840/freeopcua/server/
"""

import sys
import asyncio
from asyncua import Client
import logging

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


async def main():
    # OPC UA Server URL - connecting to our simulated server
    server_url = "opc.tcp://localhost:4840/freeopcua/server/"

    logger.info(f"Connecting to OPC UA server at: {server_url}")

    # Create client instance
    async with Client(url=server_url) as client:
        try:
            # Get server namespace
            namespace = await client.get_namespace_array()
            logger.info(f"Server namespaces: {namespace}")

            # Get server objects
            objects = client.get_objects_node()
            logger.info(f"Objects node: {objects}")

            # Browse some nodes
            logger.info("Browsing nodes...")
            children = await objects.get_children()
            for node in children:
                try:
                    browse_name = await node.read_browse_name()
                    logger.info(f"Node: {node}, Name: {browse_name}")
                    
                    # If this is our MyDevice node, browse its variables
                    if "MyDevice" in str(browse_name):
                        logger.info(f"Found MyDevice, exploring variables...")
                        device_vars = await node.get_children()
                        for var in device_vars:
                            var_name = await var.read_browse_name()
                            var_value = await var.read_value()
                            logger.info(f"Variable: {var_name}, Value: {var_value}")
                except Exception as e:
                    logger.error(f"Error browsing node {node}: {e}")

            logger.info("Test completed successfully")
            return 0

        except Exception as e:
            logger.error(f"Connection failed: {e}")
            return 1


if __name__ == "__main__":
    # Run the async main function
    sys.exit(asyncio.run(main()))
