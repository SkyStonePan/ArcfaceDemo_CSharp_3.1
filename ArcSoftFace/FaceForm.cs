﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ArcSoftFace.Utils;
using ArcSoftFace.Entity;
using System.IO;
using System.Configuration;
using System.Threading;
using AForge.Video.DirectShow;
using ArcFaceSDK;
using ArcFaceSDK.SDKModels;
using ArcFaceSDK.Entity;
using ArcFaceSDK.Utils;

namespace ArcSoftFace
{
    public partial class FaceForm : Form
    {
        #region 参数定义
        /// <summary>
        /// 图像处理引擎对象
        /// </summary>
        private FaceEngine imageEngine = new FaceEngine();

        /// <summary>
        /// 保存右侧图片路径
        /// </summary>
        private string image1Path;

        /// <summary>
        /// 图片最大大小限制
        /// </summary>
        private long maxSize = 1024 * 1024 * 2;

        /// <summary>
        /// 最大宽度
        /// </summary>
        private int maxWidth = 1536;

        /// <summary>
        /// 最大高度
        /// </summary>
        private int maxHeight = 1536;

        /// <summary>
        /// 右侧图片人脸特征
        /// </summary>
        private FaceFeature image1Feature;

        /// <summary>
        /// 保存对比图片的列表
        /// </summary>
        private List<string> imagePathList = new List<string>();

        /// <summary>
        /// 左侧图库人脸特征列表
        /// </summary>
        private List<FaceFeature> imagesFeatureList = new List<FaceFeature>();

        /// <summary>
        /// 相似度
        /// </summary>
        private float threshold = 0.8f;

        /// <summary>
        /// 用于标记是否需要清除比对结果
        /// </summary>
        private bool isCompare = false;

        #region 视频模式下相关
        /// <summary>
        /// 视频引擎对象
        /// </summary>
        private FaceEngine videoEngine = new FaceEngine();

        /// <summary>
        /// RGB视频引擎对象
        /// </summary>
        private FaceEngine videoRGBImageEngine = new FaceEngine();

        /// <summary>
        /// IR视频引擎对象
        /// </summary>
        private FaceEngine videoIRImageEngine = new FaceEngine();

        /// <summary>
        /// 视频输入设备信息
        /// </summary>
        private FilterInfoCollection filterInfoCollection;

        /// <summary>
        /// RGB摄像头设备
        /// </summary>
        private VideoCaptureDevice rgbDeviceVideo;

        /// <summary>
        /// IR摄像头设备
        /// </summary>
        private VideoCaptureDevice irDeviceVideo;

        /// <summary>
        /// 缓存的RGB视频帧检测FaceId
        /// </summary>
        private int rgbTempFaceId;

        /// <summary>
        /// 缓存的IR视频帧检测FaceId
        /// </summary>
        private int irTempFaceId;

        /// <summary>
        /// 是否是双目摄像
        /// </summary>
        private bool isDoubleShot = false;

        /// <summary>
        /// RGB 摄像头索引
        /// </summary>
        private int rgbCameraIndex = 0;

        /// <summary>
        /// IR 摄像头索引
        /// </summary>
        private int irCameraIndex = 0;

        /// <summary>
        /// 人员库图片选择 锁对象
        /// </summary>
        private object chooseImgLocker = new object();

        /// <summary>
        /// RGB 摄像头视频人脸追踪检测结果
        /// </summary>
        private FaceTrackUnit trackRGBUnit = new FaceTrackUnit();

        /// <summary>
        /// IR 视频人脸追踪检测结果
        /// </summary>
        private FaceTrackUnit trackIRUnit = new FaceTrackUnit();

        /// <summary>
        /// VideoPlayer 框的字体
        /// </summary>
        private Font font = new Font(FontFamily.GenericSerif, 10f, FontStyle.Bold);

        /// <summary>
        /// 黄色画笔
        /// </summary>
        private SolidBrush yellowBrush = new SolidBrush(Color.Yellow);

        /// <summary>
        /// 蓝色画笔
        /// </summary>
        private SolidBrush blueBrush = new SolidBrush(Color.Blue);

        /// <summary>
        /// RGB视频帧-活体检测/特征比对锁
        /// </summary>
        private bool isRGBLock = false;

        /// <summary>
        /// RGB视频帧-活体检测/特征比对锁
        /// </summary>
        private bool isIRLock = false;

        /// <summary>
        /// IR视频帧检测-引擎锁对象
        /// </summary>
        private object irLocker = new object();
        #endregion
        #endregion

        #region 初始化
        public FaceForm()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
            //初始化引擎
            InitEngines();
            //隐藏摄像头图像窗口
            rgbVideoSource.Hide();
            irVideoSource.Hide();
            //阈值控件不可用
            txtThreshold.Enabled = false;
        }

        /// <summary>
        /// 初始化引擎
        /// </summary>
        private void InitEngines()
        {
            try
            {
                //读取配置文件
                AppSettingsReader reader = new AppSettingsReader();
                string appId = (string)reader.GetValue("APPID", typeof(string));
                string sdkKey64 = (string)reader.GetValue("SDKKEY64", typeof(string));
                string sdkKey32 = (string)reader.GetValue("SDKKEY32", typeof(string));
                string activeKey64 = (string)reader.GetValue("ACTIVEKEY64", typeof(string));
                string activeKey32 = (string)reader.GetValue("ACTIVEKEY32", typeof(string));
                rgbCameraIndex = (int)reader.GetValue("RGB_CAMERA_INDEX", typeof(int));
                irCameraIndex = (int)reader.GetValue("IR_CAMERA_INDEX", typeof(int));
                //判断CPU位数
                var is64CPU = Environment.Is64BitProcess;
                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(is64CPU ? sdkKey64 : sdkKey32) || string.IsNullOrWhiteSpace(is64CPU ? activeKey64 : activeKey32))
                {
                    //禁用相关功能按钮
                    ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
                    MessageBox.Show(string.Format("请在App.config配置文件中先配置APPID、SDKKEY{0}、ACTIVEKEY{0}!", is64CPU ? "64" : "32"));
                    System.Environment.Exit(0);
                }
                //在线激活引擎    如出现错误，1.请先确认从官网下载的sdk库已放到对应的bin中，2.当前选择的CPU为x86或者x64
                int retCode = 0;
                try
                {
                    retCode = imageEngine.ASFOnlineActivation(appId, is64CPU ? sdkKey64 : sdkKey32, is64CPU ? activeKey64 : activeKey32);
                    if (retCode != 0 && retCode != 90114)
                    {
                        MessageBox.Show("激活SDK失败,错误码:" + retCode);
                        System.Environment.Exit(0);
                    }
                }
                catch (Exception ex)
                {
                    //禁用相关功能按钮
                    ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
                    if (ex.Message.Contains("无法加载 DLL"))
                    {
                        MessageBox.Show("请将SDK相关DLL放入bin对应的x86或x64下的文件夹中!");
                    }
                    else
                    {
                        MessageBox.Show("激活SDK失败,请先检查依赖环境及SDK的平台、版本是否正确!");
                    }
                    System.Environment.Exit(0);
                }

                //初始化引擎
                DetectionMode detectMode = DetectionMode.ASF_DETECT_MODE_IMAGE;
                //Video模式下检测脸部的角度优先值
                ASF_OrientPriority videoDetectFaceOrientPriority = ASF_OrientPriority.ASF_OP_ALL_OUT;
                //Image模式下检测脸部的角度优先值
                ASF_OrientPriority imageDetectFaceOrientPriority = ASF_OrientPriority.ASF_OP_ALL_OUT;
                //人脸在图片中所占比例，如果需要调整检测人脸尺寸请修改此值，有效数值为2-32
                int detectFaceScaleVal = 16;
                //最大需要检测的人脸个数
                int detectFaceMaxNum = 5;
                //引擎初始化时需要初始化的检测功能组合
                int combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_AGE | FaceEngineMask.ASF_GENDER | FaceEngineMask.ASF_FACE3DANGLE | FaceEngineMask.ASF_IMAGEQUALITY;
                //初始化引擎，正常值为0，其他返回值请参考http://ai.arcsoft.com.cn/bbs/forum.php?mod=viewthread&tid=19&_dsign=dbad527e
                retCode = imageEngine.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMask);
                Console.WriteLine("InitEngine Result:" + retCode);
                AppendText((retCode == 0) ? "引擎初始化成功!\r\n" : string.Format("引擎初始化失败!错误码为:{0}\r\n", retCode));
                if (retCode != 0)
                {
                    //禁用相关功能按钮
                    ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
                }

                //初始化视频模式下人脸检测引擎
                DetectionMode detectModeVideo = DetectionMode.ASF_DETECT_MODE_VIDEO;
                int combinedMaskVideo = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_IMAGEQUALITY;
                retCode = videoEngine.ASFInitEngine(detectModeVideo, videoDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMaskVideo);

                //RGB视频专用FR引擎
                combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_LIVENESS | FaceEngineMask.ASF_IMAGEQUALITY;
                retCode = videoRGBImageEngine.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMask);
                //设置活体阈值
                videoRGBImageEngine.ASFSetLivenessParam(0.5f);

                //IR视频专用FR引擎
                combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_IR_LIVENESS| FaceEngineMask.ASF_IMAGEQUALITY;
                retCode = videoIRImageEngine.ASFInitEngine(detectModeVideo, imageDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMask);
                //设置活体阈值
                videoIRImageEngine.ASFSetLivenessParam(0.5f, 0.7f);

                initVideo();
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
                MessageBox.Show("程序初始化异常,请在App.config中修改日志配置,根据日志查找原因!");
                System.Environment.Exit(0);
            }
        }

        /// <summary>
        /// 摄像头初始化
        /// </summary>
        private void initVideo()
        {
            try
            {
                filterInfoCollection = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                //如果没有可用摄像头，“启用摄像头”按钮禁用，否则使可用
                btnStartVideo.Enabled = !(filterInfoCollection.Count == 0);
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }
        #endregion

        #region 注册人脸按钮事件        
        /// <summary>
        /// 人脸库图片选择按钮事件
        /// </summary>
        private void ChooseMultiImg(object sender, EventArgs e)
        {
            try
            {
                lock (chooseImgLocker)
                {
                    OpenFileDialog openFileDialog = new OpenFileDialog();
                    openFileDialog.Title = "选择图片";
                    openFileDialog.Filter = "图片文件|*.bmp;*.jpg;*.jpeg;*.png";
                    openFileDialog.Multiselect = true;
                    openFileDialog.FileName = string.Empty;
                    imageList.Refresh();
                    if (openFileDialog.ShowDialog().Equals(DialogResult.OK))
                    {

                        List<string> imagePathListTemp = new List<string>();
                        var numStart = imagePathList.Count;
                        int isGoodImage = 0;

                        //保存图片路径并显示
                        string[] fileNames = openFileDialog.FileNames;
                        for (int i = 0; i < fileNames.Length; i++)
                        {
                            //图片格式判断
                            if (CheckImage(fileNames[i]))
                            {
                                imagePathListTemp.Add(fileNames[i]);
                            }
                        }

                        //人脸检测以及提取人脸特征
                        ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                        {
                            //禁止点击按钮
                            Invoke(new Action(delegate
                            {
                                ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn, btnStartVideo);
                            }));

                            //人脸检测和剪裁
                            for (int i = 0; i < imagePathListTemp.Count; i++)
                            {
                                Image image = ImageUtil.readFromFile(imagePathListTemp[i]);
                                //校验图片宽高
                                CheckImageWidthAndHeight(ref image);
                                if (image == null)
                                {
                                    continue;
                                }
                                //调整图像宽度，需要宽度为4的倍数
                                if (image.Width % 4 != 0)
                                {
                                    image = ImageUtil.ScaleImage(image, image.Width - (image.Width % 4), image.Height);
                                }
                                //人脸检测
                                MultiFaceInfo multiFaceInfo = new MultiFaceInfo();
                                int retCode = imageEngine.ASFDetectFacesEx(image, out multiFaceInfo);
                                //判断检测结果
                                bool detectAndQualityResult = false;
                                if (retCode == 0 && multiFaceInfo.faceNum > 0)
                                {
                                    ImageQualityInfo imageQualityInfo = new ImageQualityInfo();
                                    int qualityRetCode = imageEngine.ASFImageQualityDetectEx(image, multiFaceInfo, out imageQualityInfo);
                                    if (qualityRetCode == 0 && imageQualityInfo.num >0 && imageQualityInfo.faceQualityValues[0] > 0.35)
                                    {
                                        //多人脸时，默认裁剪第一个人脸
                                        imagePathList.Add(imagePathListTemp[i]);
                                        MRECT rect = multiFaceInfo.faceRects[0];
                                        image = ImageUtil.CutImage(image, rect.left, rect.top, rect.right, rect.bottom);
                                        detectAndQualityResult = true;
                                    }
                                }
                                if(!detectAndQualityResult)
                                {
                                    if (image != null)
                                    {
                                        image.Dispose();
                                    }
                                    continue;
                                }
                                //显示人脸
                                this.Invoke(new Action(delegate
                                {
                                    if (image == null)
                                    {
                                        image = ImageUtil.readFromFile(imagePathListTemp[i]);
                                        //校验图片宽高
                                        CheckImageWidthAndHeight(ref image);
                                    }
                                    imageLists.Images.Add(imagePathListTemp[i], image);
                                    imageList.Items.Add((numStart + isGoodImage) + "号", imagePathListTemp[i]);
                                    imageList.Refresh();
                                    isGoodImage += 1;
                                    if (image != null)
                                    {
                                        image.Dispose();
                                    }
                                }));
                            }

                            //提取人脸特征
                            for (int i = numStart; i < imagePathList.Count; i++)
                            {
                                Image image = ImageUtil.readFromFile(imagePathList[i]);
                                //校验图片宽高
                                CheckImageWidthAndHeight(ref image);
                                if (image == null)
                                {
                                    continue;
                                }
                                if (image.Width % 4 != 0)
                                {
                                    image = ImageUtil.ScaleImage(image, image.Width - (image.Width % 4), image.Height);
                                }
                                int retCode = -1;
                                SingleFaceInfo singleFaceInfo = new SingleFaceInfo();
                                FaceFeature feature = FaceUtil.ExtractFeature(imageEngine, image, out singleFaceInfo, ref retCode);
                                this.Invoke(new Action(delegate
                                {
                                    if (retCode != 0)
                                    {
                                        AppendText(string.Format("{0}号未检测到人脸\r\n", i));
                                    }
                                    else
                                    {
                                        AppendText(string.Format("已提取{0}号人脸特征值，[left:{1},right:{2},top:{3},bottom:{4},orient:{5}]\r\n", i, singleFaceInfo.faceRect.left, singleFaceInfo.faceRect.right, singleFaceInfo.faceRect.top, singleFaceInfo.faceRect.bottom, singleFaceInfo.faceOrient));
                                        imagesFeatureList.Add(feature);
                                    }
                                }));
                                if (image != null)
                                {
                                    image.Dispose();
                                }
                            }
                            //允许点击按钮
                            Invoke(new Action(delegate
                            {
                                ControlsEnable(true, chooseMultiImgBtn, btnClearFaceList, btnStartVideo);
                                ControlsEnable(("启用摄像头".Equals(btnStartVideo.Text)), chooseImgBtn, matchBtn);
                            }));
                        }));

                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }
        #endregion

        #region 清空人脸库按钮事件
        /// <summary>
        /// 清除人脸库事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnClearFaceList_Click(object sender, EventArgs e)
        {
            try
            {
                //清除数据
                imageLists.Images.Clear();
                imageList.Items.Clear();
                imagesFeatureList.Clear();
                imagePathList.Clear();
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }
        #endregion

        #region 选择识别图按钮事件
        /// <summary>
        /// “选择识别图片”按钮事件
        /// </summary>
        private void ChooseImg(object sender, EventArgs e)
        {
            try
            {
                lblCompareInfo.Text = string.Empty;
                //判断引擎是否初始化成功
                if (!imageEngine.GetEngineStatus())
                {
                    //禁用相关功能按钮
                    ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
                    MessageBox.Show("请先初始化引擎!");
                    return;
                }
                //选择图片
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "选择图片";
                openFileDialog.Filter = "图片文件|*.bmp;*.jpg;*.jpeg;*.png";
                openFileDialog.Multiselect = false;
                openFileDialog.FileName = string.Empty;
                if (openFileDialog.ShowDialog().Equals(DialogResult.OK))
                {

                    image1Path = openFileDialog.FileName;
                    //检测图片格式
                    if (!CheckImage(image1Path))
                    {
                        return;
                    }
                    DateTime detectStartTime = DateTime.Now;
                    AppendText(string.Format("------------------------------开始检测，时间:{0}------------------------------\r\n", detectStartTime.ToString("yyyy-MM-dd HH:mm:ss:ms")));

                    //获取文件，拒绝过大的图片
                    FileInfo fileInfo = new FileInfo(image1Path);
                    if (fileInfo.Length > maxSize)
                    {
                        MessageBox.Show("图像文件最大为2MB，请压缩后再导入!");
                        AppendText(string.Format("------------------------------检测结束，时间:{0}------------------------------\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ms")));
                        AppendText("\r\n");
                        return;
                    }

                    Image srcImage = ImageUtil.readFromFile(image1Path);
                    //校验图片宽高
                    CheckImageWidthAndHeight(ref srcImage);
                    if (srcImage == null)
                    {
                        MessageBox.Show("图像数据获取失败，请稍后重试!");
                        AppendText(string.Format("------------------------------检测结束，时间:{0}------------------------------\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ms")));
                        AppendText("\r\n");
                        return;
                    }
                    //调整图像宽度，需要宽度为4的倍数
                    if (srcImage.Width % 4 != 0)
                    {
                        srcImage = ImageUtil.ScaleImage(srcImage, srcImage.Width - (srcImage.Width % 4), srcImage.Height);
                    }
                    //人脸检测
                    MultiFaceInfo multiFaceInfo = new MultiFaceInfo();
                    int retCode = imageEngine.ASFDetectFacesEx(srcImage, out multiFaceInfo);
                    if (retCode != 0)
                    {
                        MessageBox.Show("图像人脸检测失败，请稍后重试!");
                        AppendText(string.Format("------------------------------检测结束，时间:{0}------------------------------\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ms")));
                        AppendText("\r\n");
                        return;
                    }
                    if (multiFaceInfo.faceNum < 1)
                    {
                        srcImage = ImageUtil.ScaleImage(srcImage, picImageCompare.Width, picImageCompare.Height);
                        image1Feature = new FaceFeature();
                        picImageCompare.Image = srcImage;
                        AppendText(string.Format("{0} - 未检测出人脸!\r\n", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
                        AppendText(string.Format("------------------------------检测结束，时间:{0}------------------------------\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ms")));
                        AppendText("\r\n");
                        return;
                    }

                    //年龄检测
                    int retCode_Age = -1;
                    AgeInfo ageInfo = FaceUtil.AgeEstimation(imageEngine, srcImage, multiFaceInfo, out retCode_Age);
                    //性别检测
                    int retCode_Gender = -1;
                    GenderInfo genderInfo = FaceUtil.GenderEstimation(imageEngine, srcImage, multiFaceInfo, out retCode_Gender);
                    //3DAngle检测
                    int retCode_3DAngle = -1;
                    Face3DAngle face3DAngleInfo = FaceUtil.Face3DAngleDetection(imageEngine, srcImage, multiFaceInfo, out retCode_3DAngle);


                    MRECT temp = new MRECT();
                    int ageTemp = 0;
                    int genderTemp = 0;
                    int rectTemp = 0;
                    int maxFaceIndex = 0;

                    //标记出检测到的人脸
                    for (int i = 0; i < multiFaceInfo.faceNum; i++)
                    {
                        MRECT rect = multiFaceInfo.faceRects[i]; ;
                        int orient = multiFaceInfo.faceOrients[i];
                        int age = 0;

                        if (retCode_Age != 0)
                        {
                            AppendText(string.Format("年龄检测失败，返回{0}!\r\n", retCode_Age));
                        }
                        else
                        {
                            age = ageInfo.ageArray[i];
                        }

                        int gender = -1;
                        if (retCode_Gender != 0)
                        {
                            AppendText(string.Format("性别检测失败，返回{0}!\r\n", retCode_Gender));
                        }
                        else
                        {
                            gender = genderInfo.genderArray[i];
                        }

                        int face3DStatus = -1;
                        float roll = 0f;
                        float pitch = 0f;
                        float yaw = 0f;
                        if (retCode_3DAngle != 0)
                        {
                            AppendText(string.Format("3DAngle检测失败，返回{0}!\r\n", retCode_3DAngle));
                        }
                        else
                        {
                            //角度状态 非0表示人脸不可信
                            face3DStatus = face3DAngleInfo.status[i];
                            //roll为侧倾角，pitch为俯仰角，yaw为偏航角
                            roll = face3DAngleInfo.roll[i];
                            pitch = face3DAngleInfo.pitch[i];
                            yaw = face3DAngleInfo.yaw[i];
                        }

                        int rectWidth = rect.right - rect.left;
                        int rectHeight = rect.bottom - rect.top;

                        //查找最大人脸
                        if (rectWidth * rectHeight > rectTemp)
                        {
                            maxFaceIndex = i;
                            rectTemp = rectWidth * rectHeight;
                            temp = rect;
                            ageTemp = age;
                            genderTemp = gender;
                        }
                        AppendText(string.Format("{0} - 人脸坐标:[left:{1},top:{2},right:{3},bottom:{4},orient:{5},roll:{6},pitch:{7},yaw:{8},status:{11}] Age:{9} Gender:{10}\r\n", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), rect.left, rect.top, rect.right, rect.bottom, orient, roll, pitch, yaw, age, (gender >= 0 ? gender.ToString() : ""), face3DStatus));
                    }

                    AppendText(string.Format("{0} - 人脸数量:{1}\r\n", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), multiFaceInfo.faceNum));
                    //图片质量检测
                    bool qualityResult = true;
                    ImageQualityInfo imageQualityInfo = new ImageQualityInfo();
                    int qualityRetCode = imageEngine.ASFImageQualityDetectEx(srcImage, multiFaceInfo, out imageQualityInfo);
                    if (qualityRetCode != 0 || imageQualityInfo.num <= 0 || imageQualityInfo.faceQualityValues == null | imageQualityInfo.faceQualityValues.Length <= maxFaceIndex || imageQualityInfo.faceQualityValues[maxFaceIndex] <= 0.35)
                    {
                        qualityResult = false;
                        AppendText(string.Format("{0} - 图片质量检测失败，无法进行特征提取\r\n", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
                    }
                    DateTime detectEndTime = DateTime.Now;
                    AppendText(string.Format("------------------------------检测结束，时间:{0}------------------------------\r\n", detectEndTime.ToString("yyyy-MM-dd HH:mm:ss:ms")));
                    AppendText("\r\n");
                    if (qualityResult)
                    {
                        SingleFaceInfo singleFaceInfo = new SingleFaceInfo();
                        //提取人脸特征
                        image1Feature = FaceUtil.ExtractFeature(imageEngine, srcImage, out singleFaceInfo, ref retCode, maxFaceIndex);
                    }
                    //清空上次的匹配结果
                    for (int i = 0; i < imagesFeatureList.Count; i++)
                    {
                        imageList.Items[i].Text = string.Format("{0}号", i);
                    }
                    //获取缩放比例
                    float scaleRate = ImageUtil.getWidthAndHeight(srcImage.Width, srcImage.Height, picImageCompare.Width, picImageCompare.Height);
                    //缩放图片
                    srcImage = ImageUtil.ScaleImage(srcImage, picImageCompare.Width, picImageCompare.Height);
                    //添加标记
                    srcImage = ImageUtil.MarkRectAndString(srcImage, (int)(temp.left * scaleRate), (int)(temp.top * scaleRate), (int)(temp.right * scaleRate) - (int)(temp.left * scaleRate), (int)(temp.bottom * scaleRate) - (int)(temp.top * scaleRate), ageTemp, genderTemp, picImageCompare.Width);

                    //显示标记后的图像
                    picImageCompare.Image = srcImage;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }
        #endregion

        #region 开始匹配按钮事件
        /// <summary>
        /// 匹配事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void matchBtn_Click(object sender, EventArgs e)
        {
            try
            {
                if (imagesFeatureList.Count == 0)
                {
                    MessageBox.Show("请注册人脸!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (image1Feature == null || image1Feature.featureSize <= 0)
                {
                    if (picImageCompare.Image == null)
                    {
                        MessageBox.Show("请选择识别图!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        MessageBox.Show("比对失败，识别图未提取到特征值!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return;
                }
                //标记已经做了匹配比对，在开启视频的时候要清除比对结果
                isCompare = true;
                float compareSimilarity = 0f;
                int compareNum = 0;
                AppendText(string.Format("------------------------------开始比对，时间:{0}------------------------------\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ms")));
                for (int i = 0; i < imagesFeatureList.Count; i++)
                {
                    FaceFeature feature = imagesFeatureList[i];
                    float similarity = 0f;
                    int ret = imageEngine.ASFFaceFeatureCompare(image1Feature, feature, out similarity);
                    //增加异常值处理
                    if (similarity.ToString().IndexOf("E") > -1)
                    {
                        similarity = 0f;
                    }
                    AppendText(string.Format("与{0}号比对结果:{1}\r\n", i, similarity));
                    imageList.Items[i].Text = string.Format("{0}号({1})", i, similarity);
                    if (similarity > compareSimilarity)
                    {
                        compareSimilarity = similarity;
                        compareNum = i;
                    }
                }
                if (compareSimilarity > 0)
                {
                    lblCompareInfo.Text = " " + compareNum + "号," + compareSimilarity;
                }
                AppendText(string.Format("------------------------------比对结束，时间:{0}------------------------------\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ms")));
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }
        #endregion

        #region 视频检测相关(<摄像头按钮点击事件、摄像头Paint事件、特征比对、摄像头播放完成事件>)

        /// <summary>
        /// 摄像头按钮点击事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnStartVideo_Click(object sender, EventArgs e)
        {
            try
            {
                //在点击开始的时候再坐下初始化检测，防止程序启动时有摄像头，在点击摄像头按钮之前将摄像头拔掉的情况
                initVideo();
                //必须保证有可用摄像头
                if (filterInfoCollection.Count == 0)
                {
                    MessageBox.Show("未检测到摄像头，请确保已安装摄像头或驱动!");
                    return;
                }
                if (rgbVideoSource.IsRunning || irVideoSource.IsRunning)
                {
                    btnStartVideo.Text = "启用摄像头";
                    //关闭摄像头
                    if (irVideoSource.IsRunning)
                    {
                        irVideoSource.SignalToStop();
                        irVideoSource.Hide();
                    }
                    if (rgbVideoSource.IsRunning)
                    {
                        rgbVideoSource.SignalToStop();
                        rgbVideoSource.Hide();
                    }
                    //“选择识别图”、“开始匹配”按钮可用，阈值控件禁用
                    ControlsEnable(true, chooseImgBtn, matchBtn, chooseMultiImgBtn, btnClearFaceList);
                    txtThreshold.Enabled = false;
                }
                else
                {
                    if (isCompare)
                    {
                        //比对结果清除
                        for (int i = 0; i < imagesFeatureList.Count; i++)
                        {
                            imageList.Items[i].Text = string.Format("{0}号", i);
                        }
                        lblCompareInfo.Text = string.Empty;
                        isCompare = false;
                    }
                    //“选择识别图”、“开始匹配”按钮禁用，阈值控件可用，显示摄像头控件
                    txtThreshold.Enabled = true;
                    rgbVideoSource.Show();
                    irVideoSource.Show();
                    ControlsEnable(false, chooseImgBtn, matchBtn, chooseMultiImgBtn, btnClearFaceList);
                    btnStartVideo.Text = "关闭摄像头";
                    //获取filterInfoCollection的总数
                    int maxCameraCount = filterInfoCollection.Count;
                    //如果配置了两个不同的摄像头索引
                    if (rgbCameraIndex != irCameraIndex && maxCameraCount >= 2)
                    {
                        //RGB摄像头加载
                        rgbDeviceVideo = new VideoCaptureDevice(filterInfoCollection[rgbCameraIndex < maxCameraCount ? rgbCameraIndex : 0].MonikerString);
                        rgbVideoSource.VideoSource = rgbDeviceVideo;
                        rgbVideoSource.Start();

                        //IR摄像头
                        irDeviceVideo = new VideoCaptureDevice(filterInfoCollection[irCameraIndex < maxCameraCount ? irCameraIndex : 0].MonikerString);
                        irVideoSource.VideoSource = irDeviceVideo;
                        irVideoSource.Start();
                        //双摄标志设为true
                        isDoubleShot = true;
                    }
                    else
                    {
                        //仅打开RGB摄像头，IR摄像头控件隐藏
                        rgbDeviceVideo = new VideoCaptureDevice(filterInfoCollection[rgbCameraIndex <= maxCameraCount ? rgbCameraIndex : 0].MonikerString);
                        rgbVideoSource.VideoSource = rgbDeviceVideo;
                        rgbVideoSource.Start();
                        irVideoSource.Hide();
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }

        /// <summary>
        /// RGB摄像头Paint事件，图像显示到窗体上，得到每一帧图像，并进行处理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void videoSource_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                if (rgbVideoSource.IsRunning)
                {
                    //得到当前RGB摄像头下的图片
                    Bitmap bitmap = rgbVideoSource.GetCurrentVideoFrame();
                    //校验图片宽高
                    CheckBitmapWidthAndHeight(ref bitmap);
                    if (bitmap == null)
                    {
                        return;
                    }
                    //检测人脸，得到Rect框
                    MultiFaceInfo multiFaceInfo = FaceUtil.DetectFace(videoEngine, bitmap);
                    if(multiFaceInfo == null || multiFaceInfo.faceNum == 0)
                    {
                        return;
                    }
                    //得到最大人脸
                    SingleFaceInfo maxFace = FaceUtil.GetMaxFace(multiFaceInfo);
                    //得到Rect
                    MRECT rect = maxFace.faceRect;
                    //检测RGB摄像头下最大人脸
                    Graphics g = e.Graphics;
                    float offsetX = rgbVideoSource.Width * 1f / bitmap.Width;
                    float offsetY = rgbVideoSource.Height * 1f / bitmap.Height;
                    float x = rect.left * offsetX;
                    float width = rect.right * offsetX - x;
                    float y = rect.top * offsetY;
                    float height = rect.bottom * offsetY - y;
                    //根据Rect进行画框
                    g.DrawRectangle(Pens.Red, x, y, width, height);
                    if (!string.Empty.Equals(trackRGBUnit.message) && x > 0 && y > 0)
                    {
                        //将上一帧检测结果显示到页面上
                        g.DrawString(trackRGBUnit.message, font, trackRGBUnit.liveness.Equals(1) ? blueBrush : yellowBrush, x, y - 15);
                    }
                    //判断faceId是否相同 如果相同，不必重复进行活体检测和特征比对
                    if (maxFace.faceID > 0 && maxFace.faceID.Equals(rgbTempFaceId))
                    {
                        return;
                    }

                    //保证只检测一帧，防止页面卡顿以及出现其他内存被占用情况
                    if (!isRGBLock)
                    {
                        isRGBLock = true;
                        //异步处理提取特征值和比对，不然页面会比较卡
                        ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                        {
                            if (rect.left != 0 && rect.right != 0 && rect.top != 0 && rect.bottom != 0)
                            {
                                try
                                {
                                    if(FaceUtil.ASFImageQualityDetectEx(videoRGBImageEngine, bitmap, maxFace))
                                    {
                                        int livenessResult = -1;
                                        int retCode_Liveness = -1;
                                        //RGB活体检测
                                        LivenessInfo liveInfo = FaceUtil.LivenessInfo_RGB(videoRGBImageEngine, bitmap, maxFace, out retCode_Liveness);
                                        //判断检测结果
                                        if (retCode_Liveness == 0 && liveInfo.num > 0)
                                        {
                                            livenessResult = liveInfo.isLive[0];
                                        }
                                        bool isCompare = true;
                                        if (livenessResult.Equals(1))
                                        {
                                            //提取人脸特征
                                            FaceFeature feature = FaceUtil.ExtractFeature(videoRGBImageEngine, bitmap, maxFace);
                                            if (feature != null && feature.featureSize > 0)
                                            {
                                                float similarity = 0f;
                                                //得到比对结果
                                                int result = compareFeature(feature, out similarity);

                                                if (result > -1)
                                                {
                                                    //记录faceId 之后相同的faceId不用进行比对
                                                    rgbTempFaceId = maxFace.faceID;
                                                    //将比对结果放到显示消息中，用于最新显示
                                                    trackRGBUnit.message = string.Format(" {0}号 {1},{2}", result, similarity, string.Format("RGB{0}", CommonUtil.TransLivenessResult(livenessResult)));
                                                    isCompare = false;
                                                }
                                            }
                                        }
                                        if (isCompare)
                                        {
                                            //只显示活体信息
                                            trackRGBUnit.message = string.Format("RGB{0}", CommonUtil.TransLivenessResult(livenessResult));
                                        }
                                        trackRGBUnit.liveness = livenessResult;
                                    }                                   
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                                finally
                                {
                                    if (bitmap != null)
                                    {
                                        bitmap.Dispose();
                                    }
                                    isRGBLock = false;
                                }
                            }
                            isRGBLock = false;
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }

        /// <summary>
        /// IR摄像头Paint事件,同步RGB人脸框，对比人脸框后进行IR活体检测
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void irVideoSource_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                if (isDoubleShot && irVideoSource.IsRunning)
                {
                    //如果双摄，且IR摄像头工作，获取IR摄像头图片
                    Bitmap irBitmap = irVideoSource.GetCurrentVideoFrame();
                    //校验图片宽高
                    CheckBitmapWidthAndHeight(ref irBitmap);
                    if (irBitmap == null)
                    {
                        return;
                    }
                    //检测人脸，得到Rect框
                    MultiFaceInfo multiFaceInfo = FaceUtil.DetectFaceIR(videoIRImageEngine, irBitmap);
                    if (multiFaceInfo.faceNum <= 0)
                    {
                        return;
                    }
                    //得到最大人脸
                    SingleFaceInfo maxFace = FaceUtil.GetMaxFace(multiFaceInfo);
                    //得到Rect
                    MRECT rect = maxFace.faceRect;
                    //检测RGB摄像头下最大人脸
                    Graphics g = e.Graphics;
                    float offsetX = irVideoSource.Width * 1f / irBitmap.Width;
                    float offsetY = irVideoSource.Height * 1f / irBitmap.Height;
                    float x = rect.left * offsetX;
                    float width = rect.right * offsetX - x;
                    float y = rect.top * offsetY;
                    float height = rect.bottom * offsetY - y;
                    //根据Rect进行画框
                    g.DrawRectangle(Pens.Red, x, y, width, height);
                    if (!string.Empty.Equals(trackIRUnit.message) && x > 0 && y > 0)
                    {
                        //将上一帧检测结果显示到页面上
                        g.DrawString(trackIRUnit.message, font, trackIRUnit.liveness.Equals(1) ? blueBrush : yellowBrush, x, y - 15);
                    }
                    //判断faceId是否相同 如果相同，不必重复进行活体检测和特征比对
                    if (maxFace.faceID > 0 && maxFace.faceID.Equals(irTempFaceId))
                    {
                        return;
                    }

                    //保证只检测一帧，防止页面卡顿以及出现其他内存被占用情况
                    if (!isIRLock)
                    {
                        isIRLock = true;
                        //异步处理活体检测，不然页面会比较卡
                        ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                        {
                            int livenessResult = -1;
                            if (rect.left != 0 && rect.right != 0 && rect.top != 0 && rect.bottom != 0)
                            {
                                try
                                {
                                    //得到当前摄像头下的图片
                                    if (irBitmap != null)
                                    {
                                        int retCode_Liveness = -1;

                                        //IR活体检测
                                        LivenessInfo liveInfo = FaceUtil.LivenessInfo_IR(videoIRImageEngine, irBitmap, maxFace, out retCode_Liveness);
                                        //判断检测结果
                                        if (retCode_Liveness == 0 && liveInfo.num > 0)
                                        {
                                            livenessResult = liveInfo.isLive[0];
                                            //如果是活体 则记录faceid 下次相同的人脸不用进行比对
                                            if (livenessResult.Equals(1))
                                            {
                                                irTempFaceId = maxFace.faceID;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                                finally
                                {
                                    trackIRUnit.message = string.Format("IR{0}", CommonUtil.TransLivenessResult(livenessResult));
                                    if (irBitmap != null)
                                    {
                                        irBitmap.Dispose();
                                    }
                                    isIRLock = false;
                                }
                            }
                            else
                            {
                                trackIRUnit.message = string.Empty;
                            }
                            trackIRUnit.liveness = livenessResult;
                            isIRLock = false;
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }

        /// <summary>
        /// 得到feature比较结果
        /// </summary>
        /// <param name="feature"></param>
        /// <returns></returns>
        private int compareFeature(FaceFeature feature, out float similarity)
        {
            int result = -1;
            similarity = 0f;
            try
            {
                //如果人脸库不为空，则进行人脸匹配
                if (imagesFeatureList != null && imagesFeatureList.Count > 0)
                {
                    for (int i = 0; i < imagesFeatureList.Count; i++)
                    {
                        //调用人脸匹配方法，进行匹配
                        videoRGBImageEngine.ASFFaceFeatureCompare(feature, imagesFeatureList[i], out similarity);
                        if (similarity >= threshold)
                        {
                            result = i;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
            return result;
        }

        /// <summary>
        /// 摄像头播放完成事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="reason"></param>
        private void videoSource_PlayingFinished(object sender, AForge.Video.ReasonToFinishPlaying reason)
        {
            try
            {
                Control.CheckForIllegalCrossThreadCalls = false;
                ControlsEnable(true, chooseImgBtn, matchBtn, chooseMultiImgBtn, btnClearFaceList);
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }

        #endregion

        #region 界面阈值相关
        /// <summary>
        /// 阈值文本框键按下事件，检测输入内容是否正确，不正确不能输入
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void txtThreshold_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                //阻止从键盘输入键
                e.Handled = true;
                //是数值键，回退键，.能输入，其他不能输入
                if (char.IsDigit(e.KeyChar) || e.KeyChar == 8 || e.KeyChar == '.')
                {
                    //渠道当前文本框的内容
                    string thresholdStr = txtThreshold.Text.Trim();
                    int countStr = 0;
                    int startIndex = 0;
                    //如果当前输入的内容是否是“.”
                    if (e.KeyChar == '.')
                    {
                        countStr = 1;
                    }
                    //检测当前内容是否含有.的个数
                    if (thresholdStr.IndexOf('.', startIndex) > -1)
                    {
                        countStr += 1;
                    }
                    //如果输入的内容已经超过12个字符，
                    if (e.KeyChar != 8 && (thresholdStr.Length > 12 || countStr > 1))
                    {
                        return;
                    }
                    e.Handled = false;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }

        /// <summary>
        /// 阈值文本框键抬起事件，检测阈值是否正确，不正确改为0.8f
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void txtThreshold_KeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                //如果输入的内容不正确改为默认值
                if (!float.TryParse(txtThreshold.Text.Trim(), out threshold))
                {
                    threshold = 0.8f;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }
        #endregion

        #region 窗体关闭
        /// <summary>
        /// 窗体关闭事件
        /// </summary>
        private void Form_Closed(object sender, FormClosedEventArgs e)
        {
            try
            {
                if (rgbVideoSource.IsRunning)
                {
                    btnStartVideo_Click(sender, e); //关闭摄像头
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }
        #endregion

        #region 公用方法
        /// <summary>
        /// 恢复使用/禁用控件列表控件
        /// </summary>
        /// <param name="isEnable"></param>
        /// <param name="controls">控件列表</param>
        private void ControlsEnable(bool isEnable, params Control[] controls)
        {
            try
            {
                if (controls == null || controls.Length <= 0)
                {
                    return;
                }
                foreach (Control control in controls)
                {
                    control.Enabled = isEnable;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
        }

        /// <summary>
        /// 校验图片
        /// </summary>
        /// <param name="imagePath"></param>
        /// <returns></returns>
        private bool CheckImage(string imagePath)
        {
            try
            {
                if (imagePath == null)
                {
                    AppendText("图片不存在，请确认后再导入\r\n");
                    return false;
                }
                try
                {
                    //判断图片是否正常，如将其他文件把后缀改为.jpg，这样就会报错
                    Image image = ImageUtil.readFromFile(imagePath);
                    if (image == null)
                    {
                        throw new Exception();
                    }
                    else
                    {
                        image.Dispose();
                    }
                }
                catch
                {
                    AppendText(string.Format("{0} 图片格式有问题，请确认后再导入\r\n", imagePath));
                    return false;
                }
                FileInfo fileCheck = new FileInfo(imagePath);
                if (!fileCheck.Exists)
                {
                    AppendText(string.Format("{0} 不存在\r\n", fileCheck.Name));
                    return false;
                }
                else if (fileCheck.Length > maxSize)
                {
                    AppendText(string.Format("{0} 图片大小超过2M，请压缩后再导入\r\n", fileCheck.Name));
                    return false;
                }
                else if (fileCheck.Length < 2)
                {
                    AppendText(string.Format("{0} 图像质量太小，请重新选择\r\n", fileCheck.Name));
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogInfo(GetType(), ex);
            }
            return true;
        }

        /// <summary>
        /// 追加公用方法
        /// </summary>
        /// <param name="message"></param>
        private void AppendText(string message)
        {
            logBox.AppendText(message);
        }

        /// <summary>
        /// 检查图片宽高
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        private void CheckImageWidthAndHeight(ref Image image)
        {
            if (image == null)
            {
                return;
            }
            try
            {
                if (image.Width > maxWidth || image.Height > maxHeight)
                {
                    image = ImageUtil.ScaleImage(image, maxWidth, maxHeight);
                }
            }
            catch { }
        }

        /// <summary>
        /// 检查图片宽高
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        private void CheckBitmapWidthAndHeight(ref Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return;
            }
            try
            {
                if (bitmap.Width > maxWidth || bitmap.Height > maxHeight)
                {
                    bitmap = (Bitmap)ImageUtil.ScaleImage(bitmap, maxWidth, maxHeight);
                }
            }
            catch { }
        }
        #endregion
    }
}
