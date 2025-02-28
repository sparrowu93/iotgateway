#!/usr/bin/env python3
"""
OPC UA Client to test connection to the simulation server at:
opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer
"""

import sys
import asyncio
import logging
from asyncua import Client, ua

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

async def main():
    # OPC UA Server URL - connecting to the simulation server
    server_url = "opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer"

    logger.info(f"Connecting to OPC UA server at: {server_url}")

    # Create client instance with a longer timeout
    async with Client(url=server_url, timeout=30) as client:
        try:
            # Get server namespace
            namespaces = await client.get_namespace_array()
            logger.info(f"Server namespaces:")
            for i, ns in enumerate(namespaces):
                logger.info(f"  [{i}] {ns}")

            # Get server objects
            objects = client.get_objects_node()
            logger.info(f"Objects node: {objects}")
            
            # Browse root level nodes
            logger.info("\nBrowsing root level nodes...")
            children = await objects.get_children()
            for node in children:
                try:
                    browse_name = await node.read_browse_name()
                    node_id = node.nodeid
                    logger.info(f"Node: {browse_name}, ID: {node_id}")
                    
                    # Browse children of this node
                    logger.info(f"Children of {browse_name}:")
                    sub_children = await node.get_children()
                    for sub_node in sub_children:
                        try:
                            sub_name = await sub_node.read_browse_name()
                            sub_id = sub_node.nodeid
                            try:
                                # Try to read the value if it's a variable
                                value = await sub_node.read_value()
                                data_type = await sub_node.read_data_type_as_variant_type()
                                logger.info(f"  Variable: {sub_name}, ID: {sub_id}, Type: {data_type}, Value: {value}")
                            except:
                                logger.info(f"  Node: {sub_name}, ID: {sub_id}")
                                
                                # Try to browse one level deeper
                                try:
                                    sub_sub_children = await sub_node.get_children()
                                    for sub_sub_node in sub_sub_children:
                                        try:
                                            sub_sub_name = await sub_sub_node.read_browse_name()
                                            sub_sub_id = sub_sub_node.nodeid
                                            try:
                                                # Try to read the value if it's a variable
                                                value = await sub_sub_node.read_value()
                                                data_type = await sub_sub_node.read_data_type_as_variant_type()
                                                logger.info(f"    Variable: {sub_sub_name}, ID: {sub_sub_id}, Type: {data_type}, Value: {value}")
                                            except:
                                                logger.info(f"    Node: {sub_sub_name}, ID: {sub_sub_id}")
                                        except Exception as e:
                                            logger.error(f"    Error browsing sub-sub-node: {e}")
                                except Exception as e:
                                    pass  # Ignore errors at this level
                        except Exception as e:
                            logger.error(f"  Error browsing sub-node: {e}")
                except Exception as e:
                    logger.error(f"Error browsing node {node}: {e}")

            logger.info("\nTest completed successfully")
            return 0

        except Exception as e:
            logger.error(f"Connection failed: {e}")
            return 1


if __name__ == "__main__":
    # Run the async main function
    sys.exit(asyncio.run(main()))
