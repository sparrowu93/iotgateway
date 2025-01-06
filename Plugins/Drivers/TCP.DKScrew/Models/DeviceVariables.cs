using System.Collections.Generic;

namespace TCP.DKScrew.Models
{
    public static class DeviceVariables
    {
        // 系统状态变量
        public const string IsConnected = "IsConnected";
        public const string IsReady = "IsReady";
        public const string IsRunning = "IsRunning";
        public const string IsOK = "IsOK";
        public const string IsNG = "IsNG";
        public const string HasSystemError = "HasSystemError";
        public const string SystemErrorId = "SystemErrorId";
        public const string HasParameterSetupError = "HasParameterSetupError";
        public const string HasControlTimeoutError = "HasControlTimeoutError";
        public const string RunStatus = "RunStatus";

        // 拧紧结果变量
        public const string FinalTorque = "FinalTorque";
        public const string MonitoringAngle = "MonitoringAngle";
        public const string FinalTime = "FinalTime";
        public const string FinalAngle = "FinalAngle";
        public const string ResultStatus = "ResultStatus";
        public const string NGCode = "NGCode";
        public const string LastTighteningResult = "LastTighteningResult";

        // 曲线数据变量
        public const string CurrentTorque = "CurrentTorque";
        public const string CurrentAngle = "CurrentAngle";
        public const string IsCurveFinished = "IsCurveFinished";
        public const string IsCurveStart = "IsCurveStart";
        public const string LastCurveData = "LastCurveData";

        // 控制命令变量
        public const string StartMotor = "StartMotor";
        public const string StopMotor = "StopMotor";
        public const string LoosenMotor = "LoosenMotor";
        public const string SelectPset = "SelectPset";
        public const string CurrentPset = "CurrentPset";

        // 获取所有变量定义
        public static Dictionary<string, (string Description, string Unit)> GetVariableDefinitions()
        {
            return new Dictionary<string, (string Description, string Unit)>
            {
                // 系统状态变量
                { IsConnected, ("通讯连接状态", "Bool") },
                { IsReady, ("设备就绪状态", "Bool") },
                { IsRunning, ("设备运行状态", "Bool") },
                { IsOK, ("拧紧合格状态", "Bool") },
                { IsNG, ("拧紧不合格状态", "Bool") },
                { HasSystemError, ("系统故障状态", "Bool") },
                { SystemErrorId, ("系统故障代码", "Int32") },
                { HasParameterSetupError, ("参数设置错误", "Bool") },
                { HasControlTimeoutError, ("控制超时错误", "Bool") },
                { RunStatus, ("运行状态", "RunStatus") },

                // 拧紧结果变量
                { FinalTorque, ("最终扭矩", "N·m") },
                { MonitoringAngle, ("监测角度", "度") },
                { FinalTime, ("拧紧时间", "秒") },
                { FinalAngle, ("最终角度", "度") },
                { ResultStatus, ("拧紧结果状态", "Int32") },
                { NGCode, ("不合格代码", "Int32") },
                { LastTighteningResult, ("最近一次拧紧结果", "TighteningResult") },

                // 曲线数据变量
                { CurrentTorque, ("当前扭矩", "N·m") },
                { CurrentAngle, ("当前角度", "度") },
                { IsCurveFinished, ("曲线完成状态", "Bool") },
                { IsCurveStart, ("曲线开始状态", "Bool") },
                { LastCurveData, ("最近一次曲线数据", "CurveData") },

                // 控制命令变量
                { StartMotor, ("启动电机", "Bool") },
                { StopMotor, ("停止电机", "Bool") },
                { LoosenMotor, ("松开电机", "Bool") },
                { SelectPset, ("选择Pset组别", "Int32") },
                { CurrentPset, ("当前Pset组别", "Int32") }
            };
        }
    }
}
