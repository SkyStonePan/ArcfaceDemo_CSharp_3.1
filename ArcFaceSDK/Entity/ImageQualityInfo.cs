namespace ArcFaceSDK.Entity
{
    /// <summary>
    /// 图像质量检测结构体
    /// </summary>
    public class ImageQualityInfo
    {
        ///<summary>
        ///人脸质量结果集
        /// </summary>
        public float[] faceQualityValues { get; set; }

        ///<summary>
        ///人脸数量
        /// </summary>
        public int num { get; set; }
    }
}
