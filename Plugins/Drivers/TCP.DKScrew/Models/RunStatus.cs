using System;

namespace TCP.DKScrew.Models
{
    public class RunStatus
    {
        // PID 001 - Running Status
        public bool IsReady { get; set; }
        public bool IsRunning { get; set; }
        public bool IsOK { get; set; }
        public bool IsNG { get; set; }

        // PID 002 - System Error Status
        public bool HasSystemError { get; set; }
        public int SystemErrorId { get; set; }  // 1-16 different error types

        // PID 003 - Running Error Status
        public bool HasParameterSetupError { get; set; }
        public bool HasControlTimeoutError { get; set; }
    }
}
