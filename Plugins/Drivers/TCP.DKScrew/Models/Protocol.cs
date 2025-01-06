using System;

namespace TCP.DKScrew.Models
{
    public static class Protocol
    {
        // Frame Constants
        public const byte FrameHeader = 0x02;
        public const byte FrameFooter = 0x03;
        
        // Operation Modes
        public const char ReadOperation = 'R';
        public const char WriteOperation = 'W';
        
        // Response Modes
        public const char AckResponse = 'A';
        public const char TransferResponse = 'T';
        
        // MID Codes
        public static class MID
        {
            public const string Communication = "0001";
            public const string Disconnect = "0002";
            public const string DownloadPset = "0102";
            public const string PsetSelect = "0103";
            public const string RunStatus = "0201";
            public const string TighteningResult = "0202";
            public const string CurveData = "0203";
            public const string PsetData = "0204";
            public const string MotorControl = "0301";
        }

        // Error Codes
        public static class ErrorCode
        {
            public const string FrameHeaderError = "000100";
            public const string FrameFooterError = "000200";
            public const string LengthError = "000300";
            public const string DataParseError = "000400";
            public const string InvalidMID = "000500";
            public const string InvalidPID = "000600";
            public const string InvalidDataType = "000700";
        }

        // Motor Control Commands
        public static class MotorCommand
        {
            public const int Ignore = 0;
            public const int Start = 1;
            public const int Loosen = 2;
            public const int EmergencyStop = 3;
            public const int CancelEmergencyStop = 4;
        }
    }
}
