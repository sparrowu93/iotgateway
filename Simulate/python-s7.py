# 基本S7服务器测试 - 基于官方示例
# 尽可能简化以测试服务器功能

import time
import snap7
from snap7.server import Server

print(f"使用snap7版本: {snap7.__version__}")

server = Server()
size = 100
DBdata = (snap7.type.wordlen_to_ctypes[snap7.type.S7WLByte] * size)()

# 尝试最简单的注册方式
server.register_area(snap7.type.srvAreaDB, 1, DBdata)

# 启动服务
server.start()
print("服务器已启动，按Ctrl+C停止...")

try:
    while True:
        time.sleep(1)
        print("服务器运行中...")
except KeyboardInterrupt:
    print("停止服务器...")
finally:
    server.stop()
    server.destroy()
    print("服务器已停止")
