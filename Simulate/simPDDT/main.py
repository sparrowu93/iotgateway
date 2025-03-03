from flask import Flask, request, jsonify
from datetime import datetime
import random

app = Flask(__name__)


@app.route("/production_manage/get_equipment_data", methods=["GET"])
def get_equipment_data():
    # Get query parameters
    device_type = request.args.get("deviceType", type=int)
    rob_code = request.args.get("robCode")
    start_time = request.args.get("startTime")
    end_time = request.args.get("endTime")

    # Mock data examples
    mechanical_arm_data = {
        "create_time": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "deviceType": 1,
        "robCode": "FDJ_1",
        "content": {
            "robCode": "FDJ_1",
            "position": [
                random.randint(-90, 90),
                random.randint(-90, 90),
                random.randint(-90, 90),
                random.randint(-90, 90),
                random.randint(-90, 90),
                random.randint(-90, 90),
            ],
            "deviceType": 1,
            "endEffectorCode": "SBC_CabinFixture",
            "endEffectorStatus": random.choice([0, 1]),
        },
    }

    collaborative_arm_data = {
        "create_time": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "deviceType": 2,
        "robCode": "FDJ_1",
        "content": {
            "robCode": "FDJ_1",
            "deviceType": 2,
            "screwIndex": random.randint(1, 4),
            "torque": round(random.uniform(1.5, 2.5), 3),
            "result": random.choice([True, False]),
        },
    }

    vision_data = {
        "create_time": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "deviceType": 3,
        "robCode": "code001",
        "content": {
            "deviceType": 3,
            "robCode": "code001",
            "fileUrl": f"/uploads/checkPic/{random.getrandbits(128):x}.png",
            "result": random.choice([True, False]),
            "memo": "失败原因" if random.choice([True, False]) else "",
        },
    }

    # Create response data based on device type
    response_data = []
    if device_type:
        if device_type == 1:
            response_data.append(mechanical_arm_data)
        elif device_type == 2:
            response_data.append(collaborative_arm_data)
        elif device_type == 3:
            response_data.append(vision_data)
    else:
        response_data.extend([mechanical_arm_data, collaborative_arm_data, vision_data])

    return jsonify({"success": True, "data": response_data})


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8054)
