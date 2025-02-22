namespace TCP.Parser.WeightBalance
{
    /// <summary>
    /// 称重定重心台测试数据模型
    /// </summary>
    public class WeightBalanceData
    {
        /// <summary>
        /// 产品类型
        /// </summary>
        public string ProductType { get; set; }

        /// <summary>
        /// 产品编号
        /// </summary>
        public string ProductNumber { get; set; }

        /// <summary>
        /// 产品状态
        /// </summary>
        public string ProductState { get; set; }

        /// <summary>
        /// 产品测试质量单位 X
        /// </summary>
        public double TestMassX { get; set; }

        /// <summary>
        /// 产品测试质量单位 Y
        /// </summary>
        public double TestMassY { get; set; }

        /// <summary>
        /// 产品测试质量单位 Z
        /// </summary>
        public double TestMassZ { get; set; }

        /// <summary>
        /// 产品测试倾角单位
        /// </summary>
        public double TestSita { get; set; }

        /// <summary>
        /// 产品标转换后质量单位
        /// </summary>
        public double ConversionMass { get; set; }

        /// <summary>
        /// 产品坐标转换 X 原点单位
        /// </summary>
        public double ConversionLengX { get; set; }

        /// <summary>
        /// 产品坐标转换 Y 原点单位
        /// </summary>
        public double ConversionLengY { get; set; }

        /// <summary>
        /// 产品坐标转换 Z 原点单位
        /// </summary>
        public double ConversionLengZ { get; set; }

        /// <summary>
        /// 产品坐标转换倾角单位
        /// </summary>
        public double ConversionRo { get; set; }

        /// <summary>
        /// 产品坐标转换和配平后 X 原点单位
        /// </summary>
        public double BalanceLengX { get; set; }

        /// <summary>
        /// 产品坐标转换和配平后 Y 原点单位
        /// </summary>
        public double BalanceLengY { get; set; }

        /// <summary>
        /// 产品坐标转换和配平后 Z 原点单位
        /// </summary>
        public double BalanceLengZ { get; set; }

        /// <summary>
        /// 测试结果
        /// </summary>
        public string TestResult { get; set; }
    }

    /// <summary>
    /// 服务测试结果
    /// </summary>
    public class WeightBalanceTestResult
    {
        /// <summary>
        /// 服务是否可用
        /// </summary>
        public bool IsServiceAvailable { get; set; }
    }
}
