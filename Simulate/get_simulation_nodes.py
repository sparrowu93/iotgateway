#!/usr/bin/env python3
"""
OPC UA Client to get node information from the simulation server at:
opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer
"""

import sys
import asyncio
import logging
from asyncua import Client, ua

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

async def browse_node(node, indent=0):
    """Browse node recursively and print all nodes with their IDs and values."""
    try:
        name = await node.read_browse_name()
        node_id = node.nodeid
        node_class = await node.read_node_class()
        
        prefix = "  " * indent
        logger.info(f"{prefix}Node: {name}, ID: {node_id}, Class: {node_class}")
        
        # If it's a variable, try to read its value
        if node_class == ua.NodeClass.Variable:
            try:
                value = await node.read_value()
                data_type = await node.read_data_type_as_variant_type()
                logger.info(f"{prefix}  Value: {value}, Type: {data_type}")
            except Exception as e:
                logger.error(f"{prefix}  Error reading value: {e}")
        
        # Get children of the node
        children = await node.get_children()
        for child in children:
            await browse_node(child, indent + 1)
    except Exception as e:
        logger.error(f"Error browsing node: {e}")

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

            # Get root node
            root = client.nodes.root
            logger.info(f"Root node: {root}, ID: {root.nodeid}")
            
            # Get objects node
            objects = client.nodes.objects
            logger.info(f"Objects node: {objects}, ID: {objects.nodeid}")
            
            # Browse objects node
            logger.info("\nBrowsing objects node:")
            await browse_node(objects)
            
            # Check for specific variables that might be Temperature, Humidity, etc.
            logger.info("\nSearching for variables with specific names:")
            
            # Function to search for variables with specific names
            async def find_variables(node, target_names, level=0, max_level=3):
                if level > max_level:
                    return
                
                try:
                    children = await node.get_children()
                    for child in children:
                        try:
                            name = await child.read_browse_name()
                            name_str = str(name)
                            
                            # Check if this node name contains any of our target names
                            for target in target_names:
                                if target.lower() in name_str.lower():
                                    node_id = child.nodeid
                                    node_class = await child.read_node_class()
                                    logger.info(f"Found potential match: {name}, ID: {node_id}, Class: {node_class}")
                                    
                                    # If it's a variable, try to read its value
                                    if node_class == ua.NodeClass.Variable:
                                        try:
                                            value = await child.read_value()
                                            data_type = await child.read_data_type_as_variant_type()
                                            logger.info(f"  Value: {value}, Type: {data_type}")
                                        except Exception as e:
                                            logger.error(f"  Error reading value: {e}")
                            
                            # Continue searching recursively
                            await find_variables(child, target_names, level + 1, max_level)
                        except Exception as e:
                            logger.error(f"Error processing node: {e}")
                except Exception as e:
                    logger.error(f"Error getting children: {e}")
            
            # Target variable names to search for
            target_names = ["temperature", "humidity", "pressure", "time", "current"]
            
            # Start search from objects node
            await find_variables(objects, target_names)

            logger.info("\nTest completed successfully")
            return 0

        except Exception as e:
            logger.error(f"Connection failed: {e}")
            return 1


if __name__ == "__main__":
    # Run the async main function
    sys.exit(asyncio.run(main()))
