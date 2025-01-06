using System;
using System.Collections.Generic;

namespace TCP.DKScrew.Models
{
    public class CurveData
    {
        public int SamplingFrequency { get; set; }
        public int PsetNumber { get; set; }  // 0=Undefined, 1-8=Pset number
        public bool IsCurveFinished { get; set; }
        public bool IsCurveStart { get; set; }
        public List<double> TorqueValues { get; set; } = new List<double>();
        public List<double> AngleValues { get; set; } = new List<double>();
        public double CurrentTorque { get; set; }
        public double CurrentAngle { get; set; }
    }
}
