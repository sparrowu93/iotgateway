using System;

namespace TCP.DKScrew.Models
{
    public class TighteningResult
    {
        // PID 00010 - Final Tightening Result Values
        public double FinalTorque { get; set; }
        public double MonitoringAngle { get; set; }
        public double FinalTime { get; set; }
        public double FinalAngle { get; set; }

        // PID 00011 - Final Tightening Result Status
        public int ResultStatus { get; set; }  // 0=Undefined, 1=OK, 2=NG

        // PID 00012 - NG Code
        public int NGCode { get; set; }  // Various error codes

        // Stage Results (1-5)
        public StageResult[] StageResults { get; set; } = new StageResult[5];
    }

    public class StageResult
    {
        public double Torque { get; set; }
        public double Angle { get; set; }
        public double Time { get; set; }
        public int Status { get; set; }  // 0=Undefined, 1=OK, 2=TorqueHigh, 3=TorqueLow, etc.
    }
}
