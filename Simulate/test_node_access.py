import asyncio
import logging
from asyncua import Client, ua

async def main():
    # 设置日志级别
    logging.basicConfig(level=logging.INFO)
    
    # 连接到服务器
    url = "opc.tcp://localhost:4840/freeopcua/server/"
    print(f"连接到服务器: {url}")
    
    async with Client(url=url) as client:
        # 获取命名空间
        namespaces = await client.get_namespace_array()
        print("命名空间列表:")
        for i, ns in enumerate(namespaces):
            print(f"  [{i}] {ns}")
        
        # 获取命名空间索引
        idx = await client.get_namespace_index("http://examples.freeopcua.github.io")
        print(f"\n命名空间索引: {idx}")
        
        # 尝试不同的节点ID格式
        print("\n测试不同的节点ID格式:")
        
        # 方式1：使用字符串格式的节点ID
        try:
            node_id_str = f"ns={idx};s=MyDevice.Temperature"
            node = client.get_node(node_id_str)
            value = await node.read_value()
            print(f"  使用字符串格式 '{node_id_str}': {value}")
        except Exception as e:
            print(f"  使用字符串格式 '{node_id_str}' 失败: {e}")
        
        # 方式2：使用字符串格式的节点ID（不带对象名）
        try:
            node_id_str = f"ns={idx};s=Temperature"
            node = client.get_node(node_id_str)
            value = await node.read_value()
            print(f"  使用字符串格式 '{node_id_str}': {value}")
        except Exception as e:
            print(f"  使用字符串格式 '{node_id_str}' 失败: {e}")
        
        # 方式3：使用NodeId对象
        try:
            node_id = ua.NodeId("MyDevice.Temperature", idx)
            node = client.get_node(node_id)
            value = await node.read_value()
            print(f"  使用NodeId对象 'MyDevice.Temperature': {value}")
        except Exception as e:
            print(f"  使用NodeId对象 'MyDevice.Temperature' 失败: {e}")
        
        # 方式4：使用NodeId对象（不带对象名）
        try:
            node_id = ua.NodeId("Temperature", idx)
            node = client.get_node(node_id)
            value = await node.read_value()
            print(f"  使用NodeId对象 'Temperature': {value}")
        except Exception as e:
            print(f"  使用NodeId对象 'Temperature' 失败: {e}")
        
        # 方式5：使用浏览路径
        try:
            objects = client.nodes.objects
            mydevice = await objects.get_child([f"{idx}:MyDevice"])
            temperature = await mydevice.get_child([f"{idx}:Temperature"])
            value = await temperature.read_value()
            print(f"  使用浏览路径 'Objects/MyDevice/Temperature': {value}")
        except Exception as e:
            print(f"  使用浏览路径 'Objects/MyDevice/Temperature' 失败: {e}")
        
        # 尝试获取节点的NodeId
        try:
            objects = client.nodes.objects
            mydevice = await objects.get_child([f"{idx}:MyDevice"])
            temperature = await mydevice.get_child([f"{idx}:Temperature"])
            node_id = await temperature.read_node_id()
            print(f"\n温度节点的NodeId: {node_id}")
        except Exception as e:
            print(f"\n获取温度节点的NodeId失败: {e}")

if __name__ == "__main__":
    asyncio.run(main())
