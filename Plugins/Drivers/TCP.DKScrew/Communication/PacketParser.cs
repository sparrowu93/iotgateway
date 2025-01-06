using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TCP.DKScrew.Models;

namespace TCP.DKScrew.Communication
{
    public class PacketParser
    {
        public class ParsedPacket
        {
            public string? MID { get; set; }
            public char ResponseType { get; set; }
            public Dictionary<string, string> PIDData { get; set; } = new Dictionary<string, string>();
            public bool IsError { get; set; }
            public string? ErrorCode { get; set; }
        }

        public static ParsedPacket Parse(byte[] data)
        {
            try
            {
                var packet = Encoding.ASCII.GetString(data);

                // Validate frame header and footer
                if (packet[0] != Protocol.FrameHeader)
                    throw new Exception($"Invalid frame header: {packet[0]}");
                if (packet[packet.Length - 1] != Protocol.FrameFooter)
                    throw new Exception($"Invalid frame footer: {packet[packet.Length - 1]}");

                // Parse length (4 bytes after header)
                var length = int.Parse(packet.Substring(1, 4));

                // Parse response type (A or T)
                var responseType = packet[5];
                if (responseType != Protocol.AckResponse && responseType != Protocol.TransferResponse)
                    throw new Exception($"Invalid response type: {responseType}");

                var result = new ParsedPacket
                {
                    ResponseType = responseType
                };

                // Parse MID (4 characters)
                result.MID = packet.Substring(6, 4);

                // Check for error response
                if (packet.Contains("ERROR="))
                {
                    result.IsError = true;
                    result.ErrorCode = packet.Substring(packet.IndexOf("ERROR=") + 6, 6);
                    return result;
                }

                // Parse PID data if present
                if (packet.Length > 10)
                {
                    var dataSection = packet.Substring(10, packet.Length - 11); // Exclude header, length, response type, MID, and footer
                    var pidSections = dataSection.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var pidSection in pidSections)
                    {
                        var pidParts = pidSection.Split('=');
                        if (pidParts.Length == 2)
                        {
                            result.PIDData[pidParts[0].Trim()] = pidParts[1].Trim();
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing packet: {ex.Message}");
            }
        }

        public static RunStatus ParseRunStatus(ParsedPacket packet)
        {
            var status = new RunStatus();

            if (packet.PIDData.TryGetValue("001", out var runningStatus))
            {
                var statusParts = runningStatus.Split(',');
                if (statusParts.Length >= 4)
                {
                    status.IsReady = statusParts[0] == "1";
                    status.IsRunning = statusParts[1] == "1";
                    status.IsOK = statusParts[2] == "1";
                    status.IsNG = statusParts[3] == "1";
                }
            }

            if (packet.PIDData.TryGetValue("002", out var systemError))
            {
                var errorParts = systemError.Split(',');
                if (errorParts.Length >= 2)
                {
                    status.HasSystemError = errorParts[0] == "1";
                    if (int.TryParse(errorParts[1], out var errorId))
                    {
                        status.SystemErrorId = errorId;
                    }
                }
            }

            if (packet.PIDData.TryGetValue("003", out var runError))
            {
                var errorParts = runError.Split(',');
                if (errorParts.Length >= 2)
                {
                    status.HasParameterSetupError = errorParts[0] == "1";
                    status.HasControlTimeoutError = errorParts[1] == "1";
                }
            }

            return status;
        }

        public static TighteningResult ParseTighteningResult(ParsedPacket packet)
        {
            var result = new TighteningResult();

            if (packet.PIDData.TryGetValue("00010", out var finalValues))
            {
                var parts = finalValues.Split(',');
                if (parts.Length >= 4)
                {
                    if (double.TryParse(parts[0], out var finalTorque))
                    {
                        result.FinalTorque = finalTorque;
                    }
                    if (double.TryParse(parts[1], out var monitoringAngle))
                    {
                        result.MonitoringAngle = monitoringAngle;
                    }
                    if (double.TryParse(parts[2], out var finalTime))
                    {
                        result.FinalTime = finalTime;
                    }
                    if (double.TryParse(parts[3], out var finalAngle))
                    {
                        result.FinalAngle = finalAngle;
                    }
                }
            }

            if (packet.PIDData.TryGetValue("00011", out var status))
            {
                if (int.TryParse(status, out var resultStatus))
                {
                    result.ResultStatus = resultStatus;
                }
            }

            if (packet.PIDData.TryGetValue("00012", out var ngCode))
            {
                if (int.TryParse(ngCode, out var resultNGCode))
                {
                    result.NGCode = resultNGCode;
                }
            }

            // Parse stage results (1-5)
            for (int i = 1; i <= 5; i++)
            {
                var stageResult = new StageResult();
                var stageDataKey = $"0{i}010";
                var stageStatusKey = $"0{i}011";

                if (packet.PIDData.TryGetValue(stageDataKey, out var stageData))
                {
                    var parts = stageData.Split(',');
                    if (parts.Length >= 3)
                    {
                        if (double.TryParse(parts[0], out var torque))
                        {
                            stageResult.Torque = torque;
                        }
                        if (double.TryParse(parts[1], out var angle))
                        {
                            stageResult.Angle = angle;
                        }
                        if (double.TryParse(parts[2], out var time))
                        {
                            stageResult.Time = time;
                        }
                    }
                }

                if (packet.PIDData.TryGetValue(stageStatusKey, out var stageStatus))
                {
                    if (int.TryParse(stageStatus, out var resultStatus))
                    {
                        stageResult.Status = resultStatus;
                    }
                }

                result.StageResults[i - 1] = stageResult;
            }

            return result;
        }

        public static CurveData ParseCurveData(ParsedPacket packet)
        {
            var curveData = new CurveData();

            if (packet.PIDData.TryGetValue("00020", out var curveValues))
            {
                var parts = curveValues.Split(',');
                if (parts.Length >= 2)
                {
                    if (double.TryParse(parts[0], out var torque))
                    {
                        curveData.CurrentTorque = torque;
                    }
                    if (double.TryParse(parts[1], out var angle))
                    {
                        curveData.CurrentAngle = angle;
                    }
                }
            }

            if (packet.PIDData.TryGetValue("00021", out var curveStatus))
            {
                var parts = curveStatus.Split(',');
                if (parts.Length >= 2)
                {
                    curveData.IsCurveStart = parts[0] == "1";
                    curveData.IsCurveFinished = parts[1] == "1";
                }
            }

            return curveData;
        }
    }
}
