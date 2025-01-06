using System;
using System.Text;
using TCP.DKScrew.Models;

namespace TCP.DKScrew.Communication
{
    public class PacketBuilder
    {
        private readonly StringBuilder _builder;

        public PacketBuilder()
        {
            _builder = new StringBuilder();
            // Add frame header
            _builder.Append((char)Protocol.FrameHeader);
        }

        public PacketBuilder WithLength(int length)
        {
            _builder.Append(length.ToString("D4"));
            return this;
        }

        public PacketBuilder WithOperation(char operation)
        {
            _builder.Append(operation);
            return this;
        }

        public PacketBuilder WithMID(string mid)
        {
            _builder.Append(mid);
            return this;
        }

        public PacketBuilder WithPID(string pid, string data)
        {
            _builder.Append($"{pid}={data};");
            return this;
        }

        public byte[] Build()
        {
            // Calculate length (excluding header and footer)
            int contentLength = _builder.Length - 1; // -1 for header
            
            // Insert length after header
            _builder.Insert(1, contentLength.ToString("D4"));
            
            // Add frame footer
            _builder.Append((char)Protocol.FrameFooter);
            
            return Encoding.ASCII.GetBytes(_builder.ToString());
        }

        // Helper methods for common packet types
        public static byte[] BuildConnectionRequest()
        {
            return new PacketBuilder()
                .WithOperation(Protocol.ReadOperation)
                .WithMID(Protocol.MID.Communication)
                .Build();
        }

        public static byte[] BuildDisconnectRequest()
        {
            return new PacketBuilder()
                .WithOperation(Protocol.ReadOperation)
                .WithMID(Protocol.MID.Disconnect)
                .Build();
        }

        public static byte[] BuildPsetSelectRequest(int psetNumber)
        {
            return new PacketBuilder()
                .WithOperation(Protocol.WriteOperation)
                .WithMID(Protocol.MID.PsetSelect)
                .WithPID("01", psetNumber.ToString())
                .Build();
        }

        public static byte[] BuildMotorControlRequest(int command)
        {
            return new PacketBuilder()
                .WithOperation(Protocol.WriteOperation)
                .WithMID(Protocol.MID.MotorControl)
                .WithPID("01", command.ToString())
                .Build();
        }

        public static byte[] BuildRunStatusRequest()
        {
            return new PacketBuilder()
                .WithOperation(Protocol.ReadOperation)
                .WithMID(Protocol.MID.RunStatus)
                .Build();
        }

        public static byte[] BuildTighteningResultRequest()
        {
            return new PacketBuilder()
                .WithOperation(Protocol.ReadOperation)
                .WithMID(Protocol.MID.TighteningResult)
                .Build();
        }

        public static byte[] BuildCurveDataRequest()
        {
            return new PacketBuilder()
                .WithOperation(Protocol.ReadOperation)
                .WithMID(Protocol.MID.CurveData)
                .Build();
        }

        public static byte[] BuildPsetDataRequest(int? psetNumber = null)
        {
            var builder = new PacketBuilder()
                .WithOperation(Protocol.ReadOperation)
                .WithMID(Protocol.MID.PsetData);

            if (psetNumber.HasValue)
            {
                builder.WithPID("0101", psetNumber.Value.ToString());
            }

            return builder.Build();
        }
    }
}
