using System.Collections.Generic;

namespace Nine.Design.Login.Models
{
    /// <summary>
    /// 阵距点信息
    /// </summary>
    public class PointInfo
    {
        /// <summary>
        /// X坐标
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y坐标
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// X轴速度 wpf距离单位/二十四分之一秒
        /// </summary>
        public double SpeedX { get; set; }

        /// <summary>
        /// Y轴速度 wpf距离单位/二十四分之一秒
        /// </summary>
        public double SpeedY { get; set; }

        /// <summary>
        /// X轴需要移动的距离
        /// </summary>
        public double DistanceX { get; set; }

        /// <summary>
        /// Y轴需要移动的距离
        /// </summary>
        public double DistanceY { get; set; }

        /// <summary>
        /// X轴已经移动的距离
        /// </summary>
        public double MovedX { get; set; }

        /// <summary>
        /// Y轴已经移动的距离
        /// </summary>
        public double MovedY { get; set; }

        /// <summary>
        /// 多边形信息列表
        /// </summary>
        public List<PolygonInfo> PolygonInfoList { get; set; }
    }
}
