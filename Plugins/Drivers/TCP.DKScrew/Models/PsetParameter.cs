using System;

namespace TCP.DKScrew.Models
{
    public class PsetParameter
    {
        public int PsetNumber { get; set; }  // 1-8

        // Basic Parameters
        public int TighteningDirection { get; set; }  // 0=CW, 1=CCW
        public double TargetTorque { get; set; }
        public double MaxTorque { get; set; }
        public double MinTorque { get; set; }
        public double AngleMonitorStartTorque { get; set; }
        public double AngleMonitorEndTorque { get; set; }
        public double MaxAcceptableAngle { get; set; }
        public double MinAcceptableAngle { get; set; }
        public double CycleTime { get; set; }
        public double LooseningSpeed { get; set; }
        public double LooseningAngle { get; set; }
        public int MotorMode { get; set; }
        public int StepCheckbox { get; set; }

        // Single Step Tightening Parameters
        public SingleStepParameters? SingleStep { get; set; }

        // Two Step Tightening Parameters
        public TwoStepParameters? TwoStep { get; set; }

        // Self-Tapping Parameters
        public SelfTappingParameters? SelfTapping { get; set; }

        public PsetParameter()
        {
            SingleStep = new SingleStepParameters();
            TwoStep = new TwoStepParameters();
            SelfTapping = new SelfTappingParameters();
        }
    }

    public class SingleStepParameters
    {
        public double ReverseCapSpeed { get; set; }
        public double ReverseCapAngle { get; set; }
        public double ReverseCapMaxTorque { get; set; }
        public double ReverseCapMaxTime { get; set; }
        public double ForwardInitialSpeed { get; set; }
        public double ForwardInitialAngle { get; set; }
        public double ForwardInitialMaxTorque { get; set; }
        public double ForwardInitialMaxTime { get; set; }
        public double ForwardRunningSpeed { get; set; }
        public double ForwardRunningAngle { get; set; }
        public double ForwardRunningMaxTorque { get; set; }
        public double ForwardRunningMaxTime { get; set; }
        public double SeatingSpeed { get; set; }
        public double SeatingTorque { get; set; }
        public double SeatingMaxTorque { get; set; }
        public double SeatingMaxTime { get; set; }
        public double TighteningSpeed { get; set; }
        public double TighteningTargetTorque { get; set; }
        public double TighteningMaxTorque { get; set; }
        public double TighteningMaxTime { get; set; }
    }

    public class TwoStepParameters
    {
        public double ReverseCapSpeed { get; set; }
        public double ReverseCapAngle { get; set; }
        public double ReverseCapMaxTorque { get; set; }
        public double ReverseCapMaxTime { get; set; }
        public double ForwardInitialSpeed { get; set; }
        public double ForwardInitialAngle { get; set; }
        public double ForwardInitialMaxTorque { get; set; }
        public double ForwardInitialMaxTime { get; set; }
        public double ForwardRunningSpeed { get; set; }
        public double ForwardRunningAngle { get; set; }
        public double ForwardRunningMaxTorque { get; set; }
        public double ForwardRunningMaxTime { get; set; }
        public double SeatingSpeed { get; set; }
        public double SeatingTorque { get; set; }
        public double FirstStageTorque { get; set; }
        public double SeatingMaxTime { get; set; }
        public double TighteningSpeed { get; set; }
        public double TighteningTargetTorque { get; set; }
        public double TighteningMaxTorque { get; set; }
        public double TighteningMaxTime { get; set; }
    }

    public class SelfTappingParameters
    {
        public double SoftStartSpeed { get; set; }
        public double SoftStartAngle { get; set; }
        public double SoftStartMaxTorque { get; set; }
        public double SoftStartMaxTime { get; set; }
        public double FastTappingSpeed { get; set; }
        public double FastTappingAngle { get; set; }
        public double FastTappingMaxTorque { get; set; }
        public double FastTappingMaxTime { get; set; }
        public double ContinuousRunningSpeed { get; set; }
        public double ContinuousRunningAngle { get; set; }
        public double ContinuousRunningMaxTorque { get; set; }
        public double ContinuousRunningMaxTime { get; set; }
        public double SeatingSpeed { get; set; }
        public double SeatingTorque { get; set; }
        public double SeatingMaxTorque { get; set; }
        public double SeatingMaxTime { get; set; }
        public double TighteningSpeed { get; set; }
        public double TighteningTargetTorque { get; set; }
        public double TighteningMaxTorque { get; set; }
        public double TighteningMaxTime { get; set; }
    }
}
