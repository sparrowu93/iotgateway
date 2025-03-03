import asyncio
import websockets
import json
from datetime import datetime
import random


async def generate_mock_data():
    return {
        "status": "ok",
        "time": datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3],
        "tag": "updateMessage",
        "workshopNo": "No90",
        "data": {
            "deskInfo": [
                {
                    "deskNo": f"W{str(i + 1).zfill(3)}",
                    "bootStatus": random.choice(
                        [True, True, True, False]
                    ),  # 75% chance of being True
                    "personsStr": f"张{i + 1}",
                    "carsStr": "null",
                    "devicesStr": "null",
                    "toolsStr": "工具101",
                }
                for i in range(20)
            ],
            "deskSumInfo": {
                "deskOccupy": str(random.randint(8, 15)),
                "deskEmpty": str(random.randint(5, 12)),
            },
            "deviceStatus": {
                "deviceOnline": str(random.randint(8, 15)),
                "deviceOffline": str(random.randint(5, 12)),
            },
            "enviInfo": [
                {
                    "enviNo": f"A{str(i + 1).zfill(3)}",
                    "thermoStatus": random.choice([True, False]),
                    "tempVal": f"{random.uniform(10, 30):.1f}℃",
                    "humiVal": f"{random.randint(30, 90)}%",
                    "dustVal": f"{random.uniform(50, 150):.1f}",
                    "flueGasStatus": random.choice([True, False]),
                    "flueGasVal": f"{random.uniform(2000, 4000):.1f}",
                }
                for i in range(6)
            ],
            "equipInfo": [
                {
                    "equipNo": f"E{str(i + 1).zfill(3)}",
                    "bootStatus": random.choice([True, False]),
                }
                for i in range(10)
            ],
            "personInfo": [
                {
                    "doorNo": f"N{str(i + 1).zfill(3)}",
                    "dayIn": str(random.randint(5, 20)),
                    "dayOut": str(random.randint(5, 20)),
                }
                for i in range(6)
            ],
            "personSumInfo": {
                "onAreaNum": str(random.randint(15, 25)),
                "dayInNum": str(random.randint(100, 200)),
                "dayOutNum": str(random.randint(100, 200)),
            },
            "alarminfo": {
                "msg1": random.choice(["温度超高！", "温度正常", ""]),
                "msg2": random.choice(["湿度超高！", "湿度正常", ""]),
            },
        },
    }


async def websocket_handler(websocket, path):
    try:
        while True:
            mock_data = await generate_mock_data()
            await websocket.send(json.dumps(mock_data, ensure_ascii=False))
            await asyncio.sleep(1)  # Update every second
    except websockets.exceptions.ConnectionClosed:
        pass


async def main():
    server = await websockets.serve(
        websocket_handler,
        "0.0.0.0",  # Listen on all network interfaces
        1234,
    )
    print("WebSocket server started at ws://0.0.0.0:1234")
    await server.wait_closed()


if __name__ == "__main__":
    asyncio.run(main())
