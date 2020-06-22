using System;

namespace ArcFaceSDK.SDKModels
{
    /// <summary>
    /// 图像质量检测结构体
    /// </summary>
    public struct ASF_ImageQualityInfo
    {
        ///<summary>
        ///人脸质量结果集
        /// </summary>
        public IntPtr faceQualityValues;

        ///<summary>
        ///人脸数量
        /// </summary>
        public int num;
    }
}
