using System.Windows.Shapes;

namespace Nine.Design.Login.Models
{
    /// <summary>
    /// 多边形信息
    /// </summary>
    public class PolygonInfo
    {
        /// <summary>
        /// 对多边形的引用
        /// </summary>
        public Polygon PolygonRef { get; set; }

        /// <summary>
        /// 需要改变的点的索引
        /// </summary>
        public int PointIndex { get; set; }
    }
}
