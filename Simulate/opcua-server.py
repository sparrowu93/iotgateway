import asyncio
import logging
from datetime import datetime
import random

from asyncua import Server


async def main():
    # 设置日志级别
    logging.basicConfig(level=logging.INFO)
    logging.getLogger("asyncua").setLevel(logging.INFO)

    # 初始化服务器
    server = Server()

    # 设置服务器信息
    await server.init()
    server.set_endpoint("opc.tcp://0.0.0.0:4840/freeopcua/server/")
    server.set_server_name("简单 OPC UA 服务器")

    # 设置命名空间
    uri = "http://examples.freeopcua.github.io"
    idx = await server.register_namespace(uri)

    # 获取对象节点
    objects = server.nodes.objects

    # 创建一个对象
    myobj = await objects.add_object(idx, "MyDevice")

    # 添加变量
    temperature = await myobj.add_variable(idx, "Temperature", 25.0)
    humidity = await myobj.add_variable(idx, "Humidity", 60.0)
    pressure = await myobj.add_variable(idx, "Pressure", 101.3)
    current_time = await myobj.add_variable(idx, "CurrentTime", datetime.now())

    # 设置变量为可写
    await temperature.set_writable()
    await humidity.set_writable()
    await pressure.set_writable()

    # 启动服务器
    print("服务器启动中...")
    async with server:
        print(f"服务器已启动在 {server.endpoint}")
        print("按 Ctrl+C 停止服务器")

        # 周期性更新变量值
        while True:
            # 更新温度，增加一些随机波动
            temp_value = 20.0 + random.random() * 10
            await temperature.write_value(round(temp_value, 2))

            # 更新湿度，增加一些随机波动
            hum_value = 50.0 + random.random() * 30
            await humidity.write_value(round(hum_value, 2))

            # 更新压力，增加一些随机波动
            press_value = 101.0 + random.random() * 1.0
            await pressure.write_value(round(press_value, 2))

            # 更新时间
            await current_time.write_value(datetime.now())

            print(
                f"更新变量值 - 温度: {temp_value:.2f}°C, 湿度: {hum_value:.2f}%, 压力: {press_value:.2f}kPa"
            )
            await asyncio.sleep(2)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("服务器已停止")
