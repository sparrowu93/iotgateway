import asyncio
import logging
from asyncua import Client

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
        
        # 尝试浏览根节点
        root = client.nodes.root
        print("\n浏览根节点:")
        children = await root.get_children()
        for child in children:
            name = await child.read_browse_name()
            print(f"  {name}")
        
        # 尝试浏览Objects节点
        objects = client.nodes.objects
        print("\n浏览Objects节点:")
        children = await objects.get_children()
        for child in children:
            name = await child.read_browse_name()
            print(f"  {name}")
            
            # 如果是MyDevice节点，则浏览其子节点
            if "MyDevice" in str(name):
                print(f"\n浏览 {name} 节点:")
                device_children = await child.get_children()
                for device_child in device_children:
                    child_name = await device_child.read_browse_name()
                    child_value = await device_child.read_value()
                    print(f"  {child_name} = {child_value}")

if __name__ == "__main__":
    asyncio.run(main())
