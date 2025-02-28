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
        
        # 获取Objects节点
        objects = client.nodes.objects
        
        # 获取MyDevice节点
        mydevice = await objects.get_child([f"{idx}:MyDevice"])
        mydevice_id = mydevice.nodeid
        print(f"\nMyDevice节点ID: {mydevice_id}")
        
        # 获取Temperature节点
        temperature = await mydevice.get_child([f"{idx}:Temperature"])
        temperature_id = temperature.nodeid
        print(f"Temperature节点ID: {temperature_id}")
        
        # 获取Humidity节点
        humidity = await mydevice.get_child([f"{idx}:Humidity"])
        humidity_id = humidity.nodeid
        print(f"Humidity节点ID: {humidity_id}")
        
        # 获取Pressure节点
        pressure = await mydevice.get_child([f"{idx}:Pressure"])
        pressure_id = pressure.nodeid
        print(f"Pressure节点ID: {pressure_id}")
        
        # 获取CurrentTime节点
        current_time = await mydevice.get_child([f"{idx}:CurrentTime"])
        current_time_id = current_time.nodeid
        print(f"CurrentTime节点ID: {current_time_id}")
        
        # 读取节点值
        print("\n读取节点值:")
        print(f"Temperature: {await temperature.read_value()}")
        print(f"Humidity: {await humidity.read_value()}")
        print(f"Pressure: {await pressure.read_value()}")
        print(f"CurrentTime: {await current_time.read_value()}")

if __name__ == "__main__":
    asyncio.run(main())
