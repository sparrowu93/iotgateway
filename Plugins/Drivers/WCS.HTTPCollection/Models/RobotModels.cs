using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Robot.DataCollector.Models
{
    #region Robot State Models
    
    public class RobotStateResponse
    {
        [JsonProperty("Success")]
        public bool Success { get; set; }
        
        [JsonProperty("Data")]
        public RobotStateData Data { get; set; }
    }
    
    public class RobotStateData
    {
        [JsonProperty("RobotsState")]
        public List<RobotState> RobotsState { get; set; }
    }
    
    public class RobotState
    {
        [JsonProperty("RobotId")]
        public int RobotId { get; set; }
        
        [JsonProperty("Battery")]
        public Battery Battery { get; set; }
        
        [JsonProperty("RobotParam")]
        public RobotParam RobotParam { get; set; }
        
        [JsonProperty("Current")]
        public CurrentState Current { get; set; }
        
        [JsonProperty("Target")]
        public TargetState Target { get; set; }
        
        [JsonProperty("State")]
        public int State { get; set; }
        
        [JsonProperty("IsCharging")]
        public int IsCharging { get; set; }
        
        [JsonProperty("IsOnline")]
        public int IsOnline { get; set; }
        
        [JsonProperty("IsObstacle")]
        public int IsObstacle { get; set; }
        
        [JsonProperty("IsPayload")]
        public int IsPayload { get; set; }
        
        [JsonProperty("IsAvoidance")]
        public int IsAvoidance { get; set; }
        
        [JsonProperty("SoftEmer")]
        public bool SoftEmer { get; set; }
        
        [JsonProperty("WorkMode")]
        public int WorkMode { get; set; }
        
        [JsonProperty("Rack")]
        public Rack Rack { get; set; }
        
        [JsonProperty("RobotInfo")]
        public RobotInfo RobotInfo { get; set; }
        
        [JsonProperty("Path")]
        public List<int> Path { get; set; }
        
        [JsonProperty("ErrorCodes")]
        public List<int> ErrorCodes { get; set; }
        
        [JsonProperty("ErrorInfo")]
        public List<string> ErrorInfo { get; set; }
        
        [JsonProperty("ErrorTime")]
        public long ErrorTime { get; set; }
        
        [JsonProperty("OfflineTime")]
        public long OfflineTime { get; set; }
        
        [JsonProperty("Wcs")]
        public WcsState Wcs { get; set; }
    }
    
    public class Battery
    {
        [JsonProperty("Capacity")]
        public double Capacity { get; set; }
    }
    
    public class RobotParam
    {
        [JsonProperty("Protocol")]
        public string Protocol { get; set; }
        
        [JsonProperty("Length")]
        public double Length { get; set; }
        
        [JsonProperty("Width")]
        public double Width { get; set; }
        
        [JsonProperty("Radius")]
        public double Radius { get; set; }
        
        [JsonProperty("IsOmni")]
        public bool IsOmni { get; set; }
        
        [JsonProperty("Turn")]
        public bool Turn { get; set; }
        
        [JsonProperty("RackTurn")]
        public bool RackTurn { get; set; }
        
        [JsonProperty("HorizontalMove")]
        public bool HorizontalMove { get; set; }
        
        [JsonProperty("PrecisionDis")]
        public double PrecisionDis { get; set; }
        
        [JsonProperty("PrecisionAngle")]
        public double PrecisionAngle { get; set; }
        
        [JsonProperty("Charger")]
        public string Charger { get; set; }
    }
    
    public class CurrentState
    {
        [JsonProperty("Station")]
        public string Station { get; set; }
        
        [JsonProperty("Location")]
        public int Location { get; set; }
        
        [JsonProperty("Floor")]
        public int Floor { get; set; }
        
        [JsonProperty("X")]
        public double X { get; set; }
        
        [JsonProperty("Y")]
        public double Y { get; set; }
        
        [JsonProperty("Yaw")]
        public int Yaw { get; set; }
        
        [JsonProperty("RackYaw")]
        public int RackYaw { get; set; }
        
        [JsonProperty("Linear")]
        public int Linear { get; set; }
    }
    
    public class TargetState
    {
        [JsonProperty("Floor")]
        public int Floor { get; set; }
        
        [JsonProperty("X")]
        public double X { get; set; }
        
        [JsonProperty("Y")]
        public double Y { get; set; }
        
        [JsonProperty("Yaw")]
        public int Yaw { get; set; }
        
        [JsonProperty("RackYaw")]
        public int RackYaw { get; set; }
    }
    
    public class Rack
    {
        [JsonProperty("Length")]
        public double Length { get; set; }
        
        [JsonProperty("Width")]
        public double Width { get; set; }
    }
    
    public class RobotInfo
    {
        [JsonProperty("Ip")]
        public string Ip { get; set; }
        
        [JsonProperty("Model")]
        public string Model { get; set; }
        
        [JsonProperty("Name")]
        public string Name { get; set; }
        
        [JsonProperty("Port")]
        public int Port { get; set; }
    }
    
    public class WcsState
    {
        [JsonProperty("RecvProcessId")]
        public int RecvProcessId { get; set; }
        
        [JsonProperty("SendProcessId")]
        public int SendProcessId { get; set; }
        
        [JsonProperty("Maintenance")]
        public bool Maintenance { get; set; }
        
        [JsonProperty("AllowMove")]
        public int AllowMove { get; set; }
        
        [JsonProperty("HaveTarget")]
        public int HaveTarget { get; set; }
        
        [JsonProperty("NavReached")]
        public bool NavReached { get; set; }
        
        [JsonProperty("Error")]
        public int Error { get; set; }
        
        [JsonProperty("TaskType")]
        public string TaskType { get; set; }
        
        [JsonProperty("TaskId")]
        public string TaskId { get; set; }
        
        [JsonProperty("Cmd")]
        public CmdState Cmd { get; set; }
    }
    
    public class CmdState
    {
        [JsonProperty("X")]
        public double X { get; set; }
        
        [JsonProperty("Y")]
        public double Y { get; set; }
        
        [JsonProperty("Yaw")]
        public int Yaw { get; set; }
        
        [JsonProperty("RackYaw")]
        public int RackYaw { get; set; }
    }
    
    #endregion

    #region Task Notification Models
    
    public class TaskNotifyConfig
    {
        [JsonProperty("Enable")]
        public bool Enable { get; set; }
        
        [JsonProperty("BufferSize")]
        public int BufferSize { get; set; }
        
        [JsonProperty("Sinks")]
        public Dictionary<string, NotifySink> Sinks { get; set; }
    }
    
    public class NotifySink
    {
        [JsonProperty("Type")]
        public string Type { get; set; }
        
        [JsonProperty("Timeout")]
        public int Timeout { get; set; }
        
        [JsonProperty("Url")]
        public string Url { get; set; }
    }
    
    public class TaskNotifyResponse
    {
        [JsonProperty("Success")]
        public bool Success { get; set; }
        
        [JsonProperty("Data")]
        public TaskNotifyResponseData Data { get; set; }
    }
    
    public class TaskNotifyResponseData
    {
        [JsonProperty("ErrorCode")]
        public int ErrorCode { get; set; }
        
        [JsonProperty("ErrorInfo")]
        public string ErrorInfo { get; set; }
    }
    
    #endregion

    #region Task Status Models
    
    public class TaskStatus
    {
        [JsonProperty("Id")]
        public string Id { get; set; }
        
        [JsonProperty("State")]
        public string State { get; set; }
        
        [JsonProperty("RobotId")]
        public string RobotId { get; set; }
        
        [JsonProperty("ErrorInfo")]
        public string ErrorInfo { get; set; }
    }
    
    #endregion
}
