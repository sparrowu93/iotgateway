import asyncio
import logging
from asyncua import Client, ua
import time

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
        
        # 获取节点
        objects = client.nodes.objects
        mydevice = await objects.get_child([f"{idx}:MyDevice"])
        temperature = await mydevice.get_child([f"{idx}:Temperature"])
        humidity = await mydevice.get_child([f"{idx}:Humidity"])
        pressure = await mydevice.get_child([f"{idx}:Pressure"])
        current_time = await mydevice.get_child([f"{idx}:CurrentTime"])
        
        # 打印节点ID
        print(f"\n节点ID:")
        print(f"  MyDevice: {mydevice.nodeid}")
        print(f"  Temperature: {temperature.nodeid}")
        print(f"  Humidity: {humidity.nodeid}")
        print(f"  Pressure: {pressure.nodeid}")
        print(f"  CurrentTime: {current_time.nodeid}")
        
        # 连续读取并打印数据
        print("\n开始连续读取数据:")
        for i in range(10):
            temp_value = await temperature.read_value()
            humidity_value = await humidity.read_value()
            pressure_value = await pressure.read_value()
            time_value = await current_time.read_value()
            
            print(f"\n读取次数 #{i+1} - {time.strftime('%Y-%m-%d %H:%M:%S')}:")
            print(f"  Temperature: {temp_value:.2f}")
            print(f"  Humidity: {humidity_value:.2f}")
            print(f"  Pressure: {pressure_value:.2f}")
            print(f"  CurrentTime: {time_value}")
            
            # 等待1秒
            await asyncio.sleep(1)

if __name__ == "__main__":
    asyncio.run(main())
