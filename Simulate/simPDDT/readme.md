# 获取设备数据

Base URL:

http://192.168.201.12:8054

## GET

GET /production_manage/get_equipment_data

### 请求参数

|名称|位置|类型|必选|说明|
|---|---|---|---|---|
|deviceType|query|integer| 否 |设备类型  1：机械臂；2：协作臂；3：视觉采集|
|robCode|query|string| 否 |设备编号|
|startTime|query|string| 否 |开始时间：2025-01-01 00:00:00|
|endTime|query|string| 否 |结束时间：2025-04-01 00:00:00|

> 返回示例

```json
{
  "success": true,
  "data": [
    {
      "create_time": "2025-03-03 09:51:12",
      "deviceType": 1,
      "robCode": "FDJ_1",
      "content": {
        "robCode": "FDJ_1",
        "position": [
          23,
          45,
          23,
          76,
          89,
          -82
        ],
        "deviceType": 1,
        "endEffectorCode": "SBC_CabinFixture",
        "endEffectorStatus": 1
      }
    },
    {
      "create_time": "2025-03-03 09:51:16",
      "deviceType": 2,
      "robCode": "FDJ_1",
      "content": {
        "robCode": "FDJ_1",
        "deviceType": 2,
        "screwIndex": 2,
        "torque": 1.987,
        "result": true
      }
    },
    {
      "create_time": "2025-03-03 10:02:11",
      "deviceType": 3,
      "robCode": "code001",
      "content": {
        "deviceType": 3,
        "robCode": "code001",
        "fileUrl": "/uploads/checkPic/e3ef06cc3002df478f3db75b0253565d.png",
        "result": false,
        "memo": "失败原因"
      }
    },
    {
      "create_time": "2025-03-03 10:02:11",
      "deviceType": 3,
      "robCode": "code001",
      "content": {
        "deviceType": 3,
        "robCode": "code001",
        "fileUrl": "/uploads/checkPic/e3ef06cc3002df478f3db75b0253565d.png",
        "result": true,
        "memo": ""
      }
    }
  ]
}
```

### 返回结果

|状态码|状态码含义|说明|数据模型|
|---|---|---|---|
|200|[OK](https://tools.ietf.org/html/rfc7231#section-6.3.1)|none|Inline|

### 返回数据结构

#### 机械臂
```json
{
    "robCode": "FDJ_1",  // 设备编号
    "position": [
        23,
        45,
        23,
        76,
        89,
        -82
    ], // 机械臂各关节角度信息（单位：度），数组内的关节角的排列顺序为[第一个关节，...，第六个关节]。第一个关节到第六个关节对应实体机械臂从基座开始到机械臂末端的关节。
    "deviceType": 1, // 设备类型：机械臂
    "endEffectorCode": "SBC_CabinFixture", // 当前使用的末端执行器代号（唯一标识）。SBC工位的共两种，舱段夹具（SBC_CabinFixture）和惯导夹具（SBC_InertialNavigationFixture）。FDJ工位的只有一种（FDJ_Fixture）。
    "endEffectorStatus": 1 // 当前末端执行器的状态：1：夹紧 0：松开。
}
```

#### 协作臂
```json
{
    "robCode": "FDJ_1",  // 设备编号
    "deviceType": 2, // 设备类型：协作臂
    "screwIndex": 2, // 第几颗钉（1~4）。
    "torque": 1.987, // 拧钉力矩值
    "result": true // 当前拧钉的结果：true：成功 false：失败。
}
```

#### 视觉采集
```json
{
    "robCode": "code001",  // 设备编号
    "deviceType": 3, // 设备类型：视觉采集
    "fileUrl": "/uploads/checkPic/e3ef06cc3002df478f3db75b0253565d.png", // 图片链接，拼接Base URL
    "result": true, // 检测结果：true：成功 false：失败。
    "memo": "失败原因", // 失败原因
}
```