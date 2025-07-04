#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Threading;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;

/// <summary>
/// 动作导出器 - 用于导出动作数据到深度学习训练所需的格式
/// Motion Exporter - Exports motion data to format required for deep learning training
/// 
/// 【文件功能概述】
/// 本文件是PhaseBetweener项目中的核心数据导出模块，负责将Unity中的角色动画数据
/// 转换为神经网络训练所需的数值格式。特别重要的是，本模块实现了多坐标系数据表示法，
/// 这是实现高质量动作插值的关键技术。
/// 
/// 【主要坐标系】
/// 1. 以自我为中心坐标系(Root/Ego-centric Space)：
///    - 以角色当前根部为原点的局部坐标系
///    - 用于学习角色自身的运动模式
///    - 消除全局位置的影响，提高泛化能力
/// 
/// 2. 以目标关键帧为坐标系(Target-centric Space)：
///    - 以目标姿态的根部或关节为原点的坐标系
///    - 用于学习朝向目标的定向运动
///    - 提高动作精度和目标导向性
/// 
/// 【数据导出流程】
/// Unity动画 → 多模块特征提取 → 多坐标系变换 → 标准化处理 → 训练数据文件
/// 
/// 【关键类和方法】
/// - MotionExporter: 主导出器，管理整个导出流程
/// - MotionInBetweeningSetup: 插值数据设置，核心坐标变换逻辑
/// - Container: 数据容器，封装单帧的完整状态信息
/// - Data: 数据处理类，负责数据写入和标准化
/// 
/// 【输出文件】
/// - Input.txt: 神经网络输入特征
/// - Output.txt: 神经网络目标输出  
/// - InputNorm.txt/OutputNorm.txt: 数据标准化参数
/// - InputLabels.txt/OutputLabels.txt: 特征标签说明
/// </summary>
public class MotionExporter : EditorWindow {

	// 导出管道类型 - Export pipeline type
	public enum PIPELINE {MotionInBetweening};
	// 相位处理类型 - Phase processing type
	public enum PHASES {NoPhases, LocalPhases, DeepPhases};
	// 角色类型 - Character type
	public enum CHARACTER {LaFAN, Dog};
	// 是否导出样式标签 - Whether to export style labels
	public bool ExportStyleLabels = false;

	/// <summary>
	/// 资源类 - 表示一个动作数据资源
	/// Asset class - Represents a motion data asset
	/// </summary>
	[Serializable]
	public class Asset {
		public string GUID = string.Empty;  // 资源的唯一标识符 - Unique identifier for the asset
		public bool Selected = true;        // 是否被选中导出 - Whether selected for export
		public bool Exported = false;       // 是否已导出 - Whether already exported
	}

	public static EditorWindow Window;
	public static Vector2 Scroll;

	public PIPELINE Pipeline = PIPELINE.MotionInBetweening; 
	public PHASES PhaseSelection = PHASES.DeepPhases;  // 相位选择 - Phase selection
	public CHARACTER Character = CHARACTER.LaFAN;       // 角色类型 - Character type
	public int FrameShifts = 0;                        // 帧偏移 - Frame shifts
	public int FrameBuffer = 30;                       // 帧缓冲 - Frame buffer
	public bool WriteMirror = true;                    // 是否写入镜像数据 - Whether to write mirror data
	private string Filter = string.Empty;              // 过滤器 - Filter
	private Asset[] Assets = new Asset[0];              // 资源数组 - Asset array
	[NonSerialized] private Asset[] Instances = null;  // 实例数组 - Instance array

	private static bool Aborting = false;   // 是否正在中止 - Whether aborting
	private static bool Exporting = false; // 是否正在导出 - Whether exporting

	private int Page = 0;    // 当前页 - Current page
	private int Items = 25;  // 每页项目数 - Items per page

	private float Progress = 0f;     // 进度 - Progress
	private float Performance = 0f;  // 性能指标 - Performance metric

	// 数据格式设置 - Data format settings
	private static string Separator = " ";     // 分隔符 - Separator
	private static string Accuracy = "F5";     // 精度格式 - Accuracy format
	private CultureInfo Culture = new CultureInfo("en-US");  // 文化信息 - Culture info
	private MotionEditor Editor = null;         // 动作编辑器引用 - Motion editor reference

	[MenuItem ("AI4Animation/Exporter/Motion Exporter")]
	static void Init() {
		Window = EditorWindow.GetWindow(typeof(MotionExporter));
		Scroll = Vector3.zero;
	}
	
	public void OnInspectorUpdate() {
		Repaint();
	}
	
	public void Refresh() {
		if(Editor == null) {
			Editor = GameObject.FindObjectOfType<MotionEditor>();
		}
		if(Editor != null && Assets.Length != Editor.Assets.Length) {
			Assets = new Asset[Editor.Assets.Length];
			for(int i=0; i<Editor.Assets.Length; i++) {
				Assets[i] = new Asset();
				Assets[i].GUID = Editor.Assets[i];
				Assets[i].Selected = true;
				Assets[i].Exported = false;
			}
			Aborting = false;
			Exporting = false;
			ApplyFilter(string.Empty);
		}
		if(Instances == null) {
			ApplyFilter(string.Empty);
		}
	}

	public void ApplyFilter(string filter) {
		Filter = filter;
		if(Filter == string.Empty) {
			Instances = Assets;
		} else {
			List<Asset> instances = new List<Asset>();
			for(int i=0; i<Assets.Length; i++) {
				if(Utility.GetAssetName(Assets[i].GUID).ToLowerInvariant().Contains(Filter.ToLowerInvariant())) {
					instances.Add(Assets[i]);
				}
			}
			Instances = instances.ToArray();
		}
		LoadPage(1);
	}

	public void LoadPage(int page) {
		Page = Mathf.Clamp(page, 1, GetPages());
	}

	public int GetPages() {
		return Mathf.CeilToInt(Instances.Length/Items)+1;
	}

	public int GetStart() {
		return (Page-1)*Items;
	}

	public int GetEnd() {
		return Mathf.Min(Page*Items, Instances.Length);
	}

	private string GetExportPath() {
		string path = Application.dataPath;
		path = path.Substring(0, path.LastIndexOf("/"));
		path = path.Substring(0, path.LastIndexOf("/"));
		path += "/DeepLearningONNX";
		return path;
	}

	void OnGUI() {
		Refresh();

		if(Editor == null) {
			EditorGUILayout.LabelField("No editor available in scene.");
			return;
		}

		Scroll = EditorGUILayout.BeginScrollView(Scroll);

		Utility.SetGUIColor(UltiDraw.Black);
		using(new EditorGUILayout.VerticalScope ("Box")) {
			Utility.ResetGUIColor();

			Utility.SetGUIColor(UltiDraw.Grey);
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();

				Utility.SetGUIColor(UltiDraw.Mustard);
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();
					EditorGUILayout.LabelField("Motion Exporter");
				}

				Utility.SetGUIColor(UltiDraw.White);
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();
					EditorGUI.BeginDisabledGroup(true);
					EditorGUILayout.FloatField("Export Framerate", Editor.TargetFramerate);
					EditorGUILayout.TextField("Export Path", GetExportPath());
					EditorGUI.EndDisabledGroup();
				}

				Pipeline = (PIPELINE)EditorGUILayout.EnumPopup("Pipeline", Pipeline);
				FrameShifts = EditorGUILayout.IntField("Frame Shifts", FrameShifts);
				FrameBuffer = Mathf.Max(1, EditorGUILayout.IntField("Frame Buffer", FrameBuffer));
				WriteMirror = EditorGUILayout.Toggle("Write Mirror", WriteMirror);
				PhaseSelection = (PHASES)EditorGUILayout.EnumPopup("Phases", PhaseSelection);
				Character = (CHARACTER)EditorGUILayout.EnumPopup("Character", Character);
				ExportStyleLabels = EditorGUILayout.Toggle("Export Style Labels", ExportStyleLabels);

				if(!Exporting) {
					if(Utility.GUIButton("Export Data", UltiDraw.DarkGrey, UltiDraw.White)) {
						this.StartCoroutine(ExportData());
					}
				} else {
					EditorGUILayout.LabelField("Asset: " + Editor.GetAsset().GetName());
					EditorGUILayout.LabelField("Index: " + (Editor.GetAssetIndex()+1) + " / " + Assets.Length);
					EditorGUILayout.LabelField("Mirror: " + Editor.Mirror);
					EditorGUILayout.LabelField("Frames Per Second: " + Performance.ToString("F3"));
					EditorGUI.DrawRect(new Rect(EditorGUILayout.GetControlRect().x, EditorGUILayout.GetControlRect().y, (float)(Editor.GetAssetIndex()+1) / (float)Assets.Length * EditorGUILayout.GetControlRect().width, 25f), UltiDraw.Green.Opacity(0.75f));
					EditorGUI.DrawRect(new Rect(EditorGUILayout.GetControlRect().x, EditorGUILayout.GetControlRect().y, Progress * EditorGUILayout.GetControlRect().width, 25f), UltiDraw.Green.Opacity(0.75f));

					EditorGUI.BeginDisabledGroup(Aborting);
					if(Utility.GUIButton(Aborting ? "Aborting" : "Stop", Aborting ? UltiDraw.Gold : UltiDraw.DarkRed, UltiDraw.White)) {
						Aborting = true;
					}
					EditorGUI.EndDisabledGroup();
				}

				Utility.SetGUIColor(UltiDraw.LightGrey);
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();

					Utility.SetGUIColor(UltiDraw.Mustard);
					using(new EditorGUILayout.VerticalScope ("Box")) {
						Utility.ResetGUIColor();
						EditorGUILayout.BeginHorizontal();

						EditorGUILayout.LabelField("Page", GUILayout.Width(40f));
						EditorGUI.BeginChangeCheck();
						int page = EditorGUILayout.IntField(Page, GUILayout.Width(40f));
						if(EditorGUI.EndChangeCheck()) {
							LoadPage(page);
						}
						EditorGUILayout.LabelField("/" + GetPages());
						
						EditorGUILayout.LabelField("Filter", GUILayout.Width(40f));
						EditorGUI.BeginChangeCheck();
						string filter = EditorGUILayout.TextField(Filter, GUILayout.Width(200f));
						if(EditorGUI.EndChangeCheck()) {
							ApplyFilter(filter);
						}

						EditorGUILayout.BeginHorizontal();
						if(Utility.GUIButton("Enable All", UltiDraw.DarkGrey, UltiDraw.White, 80f, 16f)) {
							foreach(Asset a in Assets) {
								a.Selected = true;
							}
						}
						if(Utility.GUIButton("Disable All", UltiDraw.DarkGrey, UltiDraw.White, 80f, 16f)) {
							foreach(Asset a in Assets) {
								a.Selected = false;
							}
						}
						if(Utility.GUIButton("Current", UltiDraw.DarkGrey, UltiDraw.White, 80f, 16f)) {
							string guid = Utility.GetAssetGUID(Editor.GetAsset());
							foreach(Asset a in Assets) {
								a.Selected = a.GUID == guid;
							}
						}
						EditorGUILayout.EndHorizontal();

						if(Utility.GUIButton("<", UltiDraw.DarkGrey, UltiDraw.White, 80f, 16f)) {
							LoadPage(Mathf.Max(Page-1, 1));
						}
						if(Utility.GUIButton(">", UltiDraw.DarkGrey, UltiDraw.White, 80f, 16f)) {
							LoadPage(Mathf.Min(Page+1, GetPages()));
						}
						EditorGUILayout.EndHorizontal();
					}
					
					int start = GetStart();
					int end = GetEnd();
					for(int i=start; i<end; i++) {
						if(Instances[i].Exported) {
							Utility.SetGUIColor(UltiDraw.DarkGreen);
						} else if(Instances[i].Selected) {
							Utility.SetGUIColor(UltiDraw.Gold);
						} else {
							Utility.SetGUIColor(UltiDraw.DarkRed);
						}
						using(new EditorGUILayout.VerticalScope ("Box")) {
							Utility.ResetGUIColor();
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.LabelField((i+1).ToString(), GUILayout.Width(20f));
							Instances[i].Selected = EditorGUILayout.Toggle(Instances[i].Selected, GUILayout.Width(20f));
							EditorGUILayout.LabelField(Utility.GetAssetName(Instances[i].GUID));
							EditorGUILayout.EndHorizontal();
						}
					}
				}
			}
		}

		EditorGUILayout.EndScrollView();
	}

	/// <summary>
	/// 数据类 - 用于处理和存储导出的数据
	/// Data class - Handles and stores exported data
	/// </summary>
	public class Data {
		public StreamWriter File, Norm, Labels;  // 文件流：数据文件、归一化文件、标签文件

		public RunningStatistics[] Statistics = null;  // 运行时统计信息 - Runtime statistics

		private Queue<float[]> Buffer = new Queue<float[]>();  // 数据缓冲队列 - Data buffer queue
		private Task Writer = null;  // 写入任务 - Writer task

		private float[] Values = new float[0];    // 数值数组 - Values array
		private string[] Names = new string[0];   // 名称数组 - Names array
		private float[] Weights = new float[0];   // 权重数组 - Weights array
		private int Dim = 0;  // 当前维度 - Current dimension

		private bool Finished = false;  // 是否完成 - Whether finished
		private bool Setup = false;     // 是否已设置 - Whether setup

		/// <summary>
		/// 构造函数 - 初始化数据处理器
		/// Constructor - Initialize data processor
		/// </summary>
		public Data(StreamWriter file, StreamWriter norm, StreamWriter labels) {
			File = file;
			Norm = norm;
			Labels = labels;
			Writer = Task.Factory.StartNew(() => WriteData());
		}

		/// <summary>
		/// 输入单个浮点数值 - Feed a single float value
		/// </summary>
		public void Feed(float value, string name, float weight=1f) {
			if(!Setup) {
				ArrayExtensions.Append(ref Values, value);
				ArrayExtensions.Append(ref Names, name);
				ArrayExtensions.Append(ref Weights, weight);
			} else {
				Dim += 1;
				Values[Dim-1] = value;
			}
		}

		/// <summary>
		/// 输入浮点数组 - Feed float array
		/// </summary>
		public void Feed(float[] values, string name, float weight=1f) {
			for(int i=0; i<values.Length; i++) {
				Feed(values[i], name + (i+1), weight);
			}
		}

		/// <summary>
		/// 输入布尔数组 - Feed boolean array
		/// </summary>
		public void Feed(bool[] values, string name, float weight=1f) {
			for(int i=0; i<values.Length; i++) {
				Feed(values[i] ? 1f : 0f, name + (i+1), weight);
			}
		}

		public void Feed(float[,] values, string name, float weight=1f) {
			for(int i=0; i<values.GetLength(0); i++) {
				for(int j=0; j<values.GetLength(1); j++) {
					Feed(values[i,j], name+(i*values.GetLength(1)+j+1), weight);
				}
			}
		}

		public void Feed(bool[,] values, string name, float weight=1f) {
			for(int i=0; i<values.GetLength(0); i++) {
				for(int j=0; j<values.GetLength(1); j++) {
					Feed(values[i,j] ? 1f : 0f, name+(i*values.GetLength(1)+j+1), weight);
				}
			}
		}

		public void Feed(Vector2 value, string name, float weight=1f) {
			Feed(value.x, name+"X", weight);
			Feed(value.y, name+"Y", weight);
		}

		/// <summary>
		/// 输入Vector3位置数据 - Feed Vector3 position data
		/// </summary>
		public void Feed(Vector3 value, string name, float weight=1f) {
			Feed(value.x, name+"X", weight);
			Feed(value.y, name+"Y", weight);
			Feed(value.z, name+"Z", weight);
		}

		/// <summary>
		/// 输入Vector3的XZ分量（忽略Y轴） - Feed XZ components of Vector3 (ignore Y axis)
		/// </summary>
		public void FeedXZ(Vector3 value, string name, float weight=1f) {
			Feed(value.x, name+"X", weight);
			Feed(value.z, name+"Z", weight);
		}

		/// <summary>
		/// 输入Vector3的XY分量（忽略Z轴） - Feed XY components of Vector3 (ignore Z axis)
		/// </summary>
		public void FeedXY(Vector3 value, string name, float weight=1f) {
			Feed(value.x, name+"X", weight);
			Feed(value.y, name+"Y", weight);
		}

		/// <summary>
		/// 输入Vector3的YZ分量（忽略X轴） - Feed YZ components of Vector3 (ignore X axis)  
		/// </summary>
		public void FeedYZ(Vector3 value, string name, float weight=1f) {
			Feed(value.y, name+"Y", weight);
			Feed(value.z, name+"Z", weight);
		}

		/// <summary>
		/// 数据写入线程方法 - Data writing thread method
		/// </summary>
		private void WriteData() {
			while(Exporting && (!Finished || Buffer.Count > 0)) {
				if(Buffer.Count > 0) {
					float[] item;
					lock(Buffer) {
						item = Buffer.Dequeue();	
					}
					//Update Mean and Std - 更新均值和标准差
					for(int i=0; i<item.Length; i++) {
						Statistics[i].Add(item[i]);
					}
					//Write to File - 写入文件
					File.WriteLine(String.Join(Separator, Array.ConvertAll(item, x => x.ToString(Accuracy))));
				} else {
					Thread.Sleep(1);
				}
			}
		}

		/// <summary>
		/// 存储当前数据样本 - Store current data sample
		/// </summary>
		public void Store() {
			if(!Setup) {
				//Setup Mean and Std - 设置均值和标准差
				Statistics = new RunningStatistics[Values.Length];
				for(int i=0; i<Statistics.Length; i++) {
					Statistics[i] = new RunningStatistics();
				}

				//Write Labels - 写入标签
				for(int i=0; i<Names.Length; i++) {
					Labels.WriteLine("[" + i + "]" + " " + Names[i]);
				}
				Labels.Close();

				Setup = true;
			}

			//Enqueue Sample - 将样本加入队列
			float[] item = (float[])Values.Clone();
			lock(Buffer) {
				Buffer.Enqueue(item);
			}

			//Reset Running Index - 重置运行索引
			Dim = 0;
		}

		/// <summary>
		/// 完成数据导出并写入统计信息 - Finish data export and write statistics
		/// </summary>
		public void Finish() {
			Finished = true;

			Task.WaitAll(Writer);

			File.Close();

			if(Setup) {
				//Write Mean - 写入均值
				float[] mean = new float[Statistics.Length];
				for(int i=0; i<mean.Length; i++) {
					mean[i] = Statistics[i].Mean();
				}
				Norm.WriteLine(String.Join(Separator, Array.ConvertAll(mean, x => x.ToString(Accuracy))));

				//Write Std - 写入标准差
				float[] std = new float[Statistics.Length];
				for(int i=0; i<std.Length; i++) {
					std[i] = Statistics[i].Std();
				}
				std.Replace(0f, 1f);
				Norm.WriteLine(String.Join(Separator, Array.ConvertAll(std, x => x.ToString(Accuracy))));
			}

			Norm.Close();
		}
	}

	private IEnumerator ExportData() {
		if(Editor == null) {
			Debug.Log("No editor found.");
		} else if(!System.IO.Directory.Exists(GetExportPath())) {
			Debug.Log("No export folder found at " + GetExportPath() + ".");
		} else {
			Aborting = false;
			Exporting = true;
			Thread.CurrentThread.CurrentCulture = Culture;
			Progress = 0f;

			int sequence = 0;
			int items = 0;
			int samples = 0;
			DateTime timestamp = Utility.GetTimestamp();

			StreamWriter S = CreateFile("Sequences");
			Data X = new Data(CreateFile("Input"), CreateFile("InputNorm"), CreateFile("InputLabels"));
			Data Y = new Data(CreateFile("Output"), CreateFile("OutputNorm"), CreateFile("OutputLabels"));
			StreamWriter CreateFile(string name) {
				return File.CreateText(GetExportPath() + "/" + name + ".txt");
			}

			for(int i=0; i<Assets.Length; i++) {
				Assets[i].Exported = false;
			}
			for(int i=0; i<Assets.Length; i++) {
				if(Aborting) {
					break;
				}
				if(Assets[i].Selected) {
					MotionData data = Editor.LoadData(Assets[i].GUID);
					while(!data.GetScene().isLoaded) {
						Debug.Log("Waiting for scene being loaded...");
						yield return new WaitForSeconds(0f);
					}
					if(!data.Export) {
						Debug.Log("Skipping Asset: " + data.GetName());
						yield return new WaitForSeconds(0f);
						continue;
					}
					for(int m=1; m<=2; m++) {
						if(m==1) {
							Editor.SetMirror(false);
						}
						if(m==2) {
							Editor.SetMirror(true);
						}
						if(!Editor.Mirror || WriteMirror && Editor.Mirror) {
							// Debug.Log("Exporting asset " + data.GetName() + " " + (Editor.Mirror ? "[Mirror]" : "[Default]"));
							for(int shift=0; shift<=FrameShifts; shift++) {
								foreach(Sequence seq in data.Sequences) {
									sequence += 1;
									float start = Editor.CeilToTargetTime(data.GetFrame(seq.Start).Timestamp);
									float end = Editor.FloorToTargetTime(data.GetFrame(seq.End).Timestamp);
									int index = 0;
									while(start + (index+1)/Editor.TargetFramerate + shift/data.Framerate <= end) {
										Editor.SetRandomSeed(Editor.GetCurrentFrame().Index);
										S.WriteLine(sequence.ToString());

										float tCurrent = start + index/Editor.TargetFramerate + shift/data.Framerate;
										float tNext = start + (index+1)/Editor.TargetFramerate + shift/data.Framerate;

										if(Pipeline == PIPELINE.MotionInBetweening){
											/* for(int s=0; s<5; s++) {
												MotionInBetweeningSetup.Export(this, X, Y, tCurrent, tNext);
												X.Store();
												Y.Store();
											} */
											MotionInBetweeningSetup.Export(this, X, Y, tCurrent, tNext);
										}
										X.Store();
										Y.Store();
										
										index += 1;
										Progress = (index/Editor.TargetFramerate) / (end-start);
										items += 1;
										samples += 1;
										if(items >= FrameBuffer) {
											Performance = items / (float)Utility.GetElapsedTime(timestamp);
											timestamp = Utility.GetTimestamp();
											items = 0;
											yield return new WaitForSeconds(0f);
										}
									}
									Progress = 0f;
								}
							}
						}
					}
					Assets[i].Exported = true;
				}
			}
			
			S.Close();
			X.Finish();
			Y.Finish();

			Aborting = false;
			Exporting = false;
			Progress = 0f;
			foreach(Asset a in Assets) {
				a.Exported = false;
			}
			yield return new WaitForSeconds(0f);

			Debug.Log("Exported " + samples + " samples.");
		}
	}

	/// <summary>
	/// 动作插值设置类 - 处理动作之间的插值数据导出
	/// Motion In-betweening Setup class - Handles motion interpolation data export
	/// 
	/// 【核心概念：多坐标系数据表示】
	/// 此类实现了一个关键的设计理念：使用多个坐标系来表示同一份动作数据，
	/// 从而为神经网络提供更丰富的空间关系信息。主要包含两种坐标系：
	/// 
	/// 1. 【以自我为中心坐标系 (Ego-centric/Root Space)】
	///    - 以当前帧或下一帧的角色根部为原点的坐标系
	///    - 所有位置、方向都相对于当前角色根部进行表示
	///    - 优势：消除全局位置影响，网络学习局部运动模式
	///    - 实现：current.Root, next.Root 作为坐标系原点
	/// 
	/// 2. 【以目标关键帧为坐标系 (Target-centric Space)】
	///    - 以目标姿态的根部或各个关节为原点的坐标系
	///    - 所有预测都相对于目标状态进行表示
	///    - 优势：直接学习朝向目标的运动趋势，提高动作精度
	///    - 实现：current.TargetRoot, targetMatrix 作为坐标系原点
	/// 
	/// 【数据流程】
	/// 输入数据(X): 当前状态 → 多坐标系变换 → 神经网络输入特征
	/// 输出数据(Y): 目标状态 → 多坐标系变换 → 神经网络预测目标
	/// 
	/// 【坐标变换函数】
	/// - GetRelativePositionTo(): 将世界坐标转换为相对坐标
	/// - GetRelativeDirectionTo(): 将世界方向转换为相对方向
	/// - Matrix4x4.TRS(): 构建局部坐标系变换矩阵
	/// 
	/// 这种多坐标系设计使得神经网络能够同时学习：
	/// - 自身的运动模式（ego-centric）
	/// - 朝向目标的运动趋势（target-centric）
	/// - 局部关节的精细运动（joint-specific）
	/// </summary>
	public class MotionInBetweeningSetup
	{
		/// <summary>
		/// 导出动作插值数据 - Export motion in-betweening data
		/// 此方法是整个数据导出的核心，负责将动作数据转换为神经网络训练所需的格式
		/// 涉及多种坐标系变换：
		/// 1. 以自我为中心坐标系(Root Space) - 以当前角色根部为原点的坐标系
		/// 2. 以目标关键帧为坐标系(Target Root/Joint Space) - 以目标姿态根部/关节为原点的坐标系
		/// 这些坐标系变换使得神经网络能够学习局部的相对运动模式，而不受全局位置影响
		/// </summary>
		/// <param name="exporter">导出器实例 - Exporter instance</param>
		/// <param name="X">输入数据 - Input data (用于神经网络的输入)</param>
		/// <param name="Y">输出数据 - Output data (用于神经网络的目标输出)</param>
		/// <param name="tCurrent">当前时间戳 - Current timestamp</param>
		/// <param name="tNext">下一时间戳 - Next timestamp</param>
		public static void Export(MotionExporter exporter, Data X, Data Y, float tCurrent, float tNext)
		{
			// 创建当前帧和下一帧的容器 - Create containers for current and next frames
			Container current = new Container(exporter.Editor, tCurrent);
			Container next = new Container(exporter.Editor, tNext);

			if (current.Frame.Index == next.Frame.Index)
			{
				Debug.LogError("Same frames for input output pairs selected!");
			}

			// 根据角色类型设置接触点 - Set contact points based on character type
			string[] contacts = new string[0];
			if(exporter.Character == CHARACTER.LaFAN){
				contacts = new string[] { "Hips", "LeftHand", "RightHand", "LeftFoot", "RightFoot" };
			}
			if(exporter.Character == CHARACTER.Dog){
				contacts = new string[] { "LeftHandSite", "RightHandSite", "LeftFootSite", "RightFootSite" };
			}

			// 设置样式标签 - Set style labels
			string[] styles = new string[0];
			if(exporter.ExportStyleLabels) {
				styles = new string[] {"Move", "Aiming", "Crouching"};
			}
			
			//============ 输入数据 Input Data ============
			// 控制轨迹数据 - Control trajectory data
			// 这些数据用于指导动作生成，采用目标根部坐标系进行坐标变换
			// 分辨率 = 1 - Resolution = 1
			for (int k = 0; k < current.TimeSeries.Samples.Length; k++)
			{
				// 【重要】相对于目标根部坐标系的轨迹数据 - Trajectory data relative to target root coordinate system
				// GetRelativePositionTo/GetRelativeDirectionTo: 将世界坐标转换为相对于目标根部的局部坐标
				// 这种变换使得网络学习的是相对运动模式，而非绝对位置
 				X.FeedXZ(next.RootSeries.GetPosition(k).GetRelativePositionTo(current.TargetRoot), "TrajectoryPosition" + (k + 1));
				X.FeedXZ(next.RootSeries.GetDirection(k).GetRelativeDirectionTo(current.TargetRoot), "TrajectoryDirection" + (k + 1));
				X.FeedXZ(next.RootSeries.GetVelocity(k).GetRelativeDirectionTo(current.TargetRoot), "TrajectoryVelocity" + (k + 1));
				X.Feed(current.TargetPose.TimeOffset - current.TimeSeries.Samples[k].Timestamp, "TimeOffset" + (k + 1)); 
				if(exporter.ExportStyleLabels) 
					X.Feed(next.StyleSeries.GetStyles(k, styles), "Style"+(k+1));
				/*
                X.Feed(next.RootSeries.Lengths[k], "TrajectoryLength"+(k+1));
                X.Feed(next.RootSeries.Arcs[k], "TrajectoryArc"+(k+1)); */
			} 

			//自回归姿态数据 - Auto-Regressive Posture
			// 【坐标系说明】相对于当前根部坐标系 - Relative to current root coordinate system
			// 这里采用"以自我为中心的坐标系"，即以当前帧的角色根部为原点
			// 所有骨骼的位置、方向都转换为相对于当前根部的局部坐标
			// 这种表示方法使得网络能够学习角色的局部姿态模式，而不受全局位置影响
			for (int k = 0; k < current.ActorPosture.Length; k++)
			{
				// GetRelativePositionTo(current.Root): 将骨骼的世界坐标转换为相对于当前根部的局部坐标
				X.Feed(current.ActorPosture[k].GetPosition().GetRelativePositionTo(current.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Position");
				// GetRelativeDirectionTo(current.Root): 将骨骼的世界方向转换为相对于当前根部的局部方向
				X.Feed(current.ActorPosture[k].GetForward().GetRelativeDirectionTo(current.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Forward");
				X.Feed(current.ActorPosture[k].GetUp().GetRelativeDirectionTo(current.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Up");
				X.Feed(current.ActorVelocities[k].GetRelativeDirectionTo(current.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Velocity");
			}

			//目标姿态数据 - Target Posture
			// 【坐标系说明】相对于当前根部坐标系 - Relative to current root coordinate system
			// 目标姿态也转换为相对于当前根部的坐标系，用于指导动作生成
			// 这提供了从当前姿态到目标姿态的相对变换信息
			for (int k = 0; k < current.TargetPose.Pose.Length; k++)
			{
				X.Feed(current.TargetPose.Pose[k].GetPosition().GetRelativePositionTo(current.Root), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Position");
				X.Feed(current.TargetPose.Pose[k].GetForward().GetRelativeDirectionTo(current.Root), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Forward");
				X.Feed(current.TargetPose.Pose[k].GetUp().GetRelativeDirectionTo(current.Root), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Up");
				X.Feed(current.TargetPose.Velocities[k].GetRelativeDirectionTo(current.Root), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Velocity");
			}

/* 			for (int k = 0; k < current.TargetPose.Pose.Length; k++)
			{
				X.Feed(current.TargetPose.Pose[k].GetPosition().GetRelativePositionTo(current.ActorPosture[k]), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Position");
				X.Feed(current.TargetPose.Pose[k].GetForward().GetRelativeDirectionTo(current.ActorPosture[k]), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Forward");
				X.Feed(current.TargetPose.Pose[k].GetUp().GetRelativeDirectionTo(current.ActorPosture[k]), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Up");
				X.Feed(current.TargetPose.Velocities[k].GetRelativeDirectionTo(current.ActorPosture[k]), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Velocity");
			} */

			// X.Feed(current.TargetBoneDistances, "TargetBoneDistances");

			//接触点数据 - Contact data
			// 接触点信息用于约束足部等关键部位的接地状态
			// 这对于维持动作的物理合理性非常重要
			for (int k = 0; k <= current.TimeSeries.Pivot; k++)
			{
				X.Feed(current.ContactSeries.GetContacts(k, contacts), "Contacts" + (k + 1) + "-");
			}

			//相位数据 - Phase data  
			// 相位信息用于捕捉周期性动作（如走路、跑步）的时序特征
			// 不同的相位表示方法适用于不同的动作类型和网络架构
			switch (exporter.PhaseSelection)
			{
				case PHASES.NoPhases:
					// 不使用相位信息
					break;
				case PHASES.LocalPhases:
					// 局部相位：为每个关键骨骼独立计算相位信息
					{
						int index = 0;
						for (int k = 0; k < current.TimeSeries.Samples.Length; k++)
						{
							for (int b = 0; b < current.PhaseSeries.Bones.Length; b++)
							{
								Vector2 phase = Utility.PhaseVector(current.PhaseSeries.Phases[k][b], current.PhaseSeries.Amplitudes[k][b]);
								index += 1;
								X.Feed(phase.x, "Gating" + index + "-Key" + (k + 1) + "-Bone" + current.PhaseSeries.Bones[b]);
								index += 1;
								X.Feed(phase.y, "Gating" + index + "-Key" + (k + 1) + "-Bone" + current.PhaseSeries.Bones[b]);
							}
						}
					}
					break;
				case PHASES.DeepPhases:
					// 深度相位：使用神经网络学习的高维相位表示
					X.Feed(current.DeepPhaseSeries.GetAlignment(), "PhaseSpace-");
					break;	
				default:
					break;
			}

			//============ 输出数据 Output Data ============
			// 输出数据是神经网络需要预测的目标值，同样采用多种坐标系变换

			//根部更新 - Root Update  
			// 【坐标系1】相对于当前根部坐标系 - Relative to current root coordinate system
			// 预测下一帧根部相对于当前根部的变化量，这是"以自我为中心"的表示方法
			Y.FeedXZ(next.RootSeries.Transformations[next.TimeSeries.Pivot].GetPosition().GetRelativePositionTo(current.Root), "RootPosition");
			Y.FeedXZ(next.RootSeries.Transformations[next.TimeSeries.Pivot].GetForward().GetRelativeDirectionTo(current.Root), "RootDirection");
			Y.FeedXZ(next.RootSeries.Velocities[next.TimeSeries.Pivot].GetRelativeDirectionTo(current.Root), "RootVelocity");

			// 【坐标系2】相对于目标根部坐标系 - Relative to target root coordinate system
			// 预测下一帧根部相对于目标根部的位置，这是"以目标关键帧为坐标系"的表示方法
			// 这种表示有助于网络学习朝向目标的运动模式
			Y.FeedXZ(next.RootSeries.Transformations[next.TimeSeries.Pivot].GetPosition().GetRelativePositionTo(current.TargetRoot), "TargetRootPosition");
			Y.FeedXZ(next.RootSeries.Transformations[next.TimeSeries.Pivot].GetForward().GetRelativeDirectionTo(current.TargetRoot), "TargetRootDirection");
			Y.FeedXZ(next.RootSeries.Velocities[next.TimeSeries.Pivot].GetRelativeDirectionTo(current.TargetRoot), "TargetRootVelocity");
			
			//根部样式 - Root Style
			if(exporter.ExportStyleLabels)
				Y.Feed(next.StyleSeries.GetStyles(next.TimeSeries.Pivot, styles), "RootStyle");

			// 未来轨迹预测 - Future Trajectory Prediction
			// 【坐标系1】相对于下一帧根部坐标系 - Relative to next frame root coordinate system
			// 预测未来轨迹相对于下一帧根部的位置，这是"以自我为中心"的未来轨迹表示
			for (int k = next.TimeSeries.Pivot + 1; k < next.TimeSeries.Samples.Length; k++)
			{
				Y.FeedXZ(next.RootSeries.GetPosition(k).GetRelativePositionTo(next.Root), "TrajectoryPosition" + (k + 1));
				Y.FeedXZ(next.RootSeries.GetDirection(k).GetRelativeDirectionTo(next.Root), "TrajectoryDirection" + (k + 1));
				Y.FeedXZ(next.RootSeries.GetVelocity(k).GetRelativeDirectionTo(next.Root), "TrajectoryVelocity" + (k + 1));
			}
			// 【坐标系2】相对于目标根部坐标系 - Relative to target root coordinate system  
			// 预测未来轨迹相对于目标根部的位置，这是"以目标关键帧为坐标系"的未来轨迹表示
			// 这种双坐标系表示提供了更丰富的空间关系信息
			for (int k = next.TimeSeries.Pivot + 1; k < next.TimeSeries.Samples.Length; k++)
			{
				Y.FeedXZ(next.RootSeries.GetPosition(k).GetRelativePositionTo(current.TargetRoot), "TargetTrajectoryPosition" + (k + 1));
				Y.FeedXZ(next.RootSeries.GetDirection(k).GetRelativeDirectionTo(current.TargetRoot), "TargetTrajectoryDirection" + (k + 1));
				Y.FeedXZ(next.RootSeries.GetVelocity(k).GetRelativeDirectionTo(current.TargetRoot), "TargetTrajectoryVelocity" + (k + 1));
				
				//未来预测的样式 - Predicted styles in future
				if(exporter.ExportStyleLabels)
					Y.Feed(next.StyleSeries.GetStyles(k, styles), "Style"+(k+1));
			}

/* 			//+ root
			for (int k = next.TimeSeries.Pivot; k < next.TimeSeries.Samples.Length; k++)
			{
                Y.Feed(next.RootSeries.Lengths[k], "TrajectoryLength"+(k+1));
                Y.Feed(next.RootSeries.Arcs[k], "TrajectoryArc"+(k+1));
			} */

			//自回归姿态预测 - Auto-Regressive Posture Prediction
			// 【坐标系1】相对于下一帧根部坐标系 - Relative to next frame root coordinate system
			// 预测下一帧的完整姿态，所有骨骼位置都相对于下一帧的根部进行表示
			// 这是"以自我为中心坐标系"的姿态预测，确保预测结果在局部坐标系中的一致性
			for (int k = 0; k < next.ActorPosture.Length; k++)
			{
				Y.Feed(next.ActorPosture[k].GetPosition().GetRelativePositionTo(next.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Position");
				Y.Feed(next.ActorPosture[k].GetForward().GetRelativeDirectionTo(next.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Forward");
				Y.Feed(next.ActorPosture[k].GetUp().GetRelativeDirectionTo(next.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Up");
				Y.Feed(next.ActorVelocities[k].GetRelativeDirectionTo(next.Root), "Bone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Velocity");
			}

			//目标关节空间预测 - Target Joint Space Prediction
			// 【坐标系2】相对于目标骨骼坐标系 - Relative to target bone coordinate system
			// 这是一个更复杂的坐标系变换：每个骨骼的预测都相对于对应的目标骨骼位置
			// targetMatrix 构建了以目标骨骼位置为原点、目标根部旋转为方向的坐标系
			// 这种"以目标关键帧为坐标系"的表示有助于网络学习精确的局部关节运动
			for (int k = 0; k < next.ActorPosture.Length; k++)
			{
				// 构建目标骨骼的局部坐标系：位置来自目标姿态，旋转来自目标根部
				Matrix4x4 targetMatrix = Matrix4x4.TRS(current.TargetPose.Pose[k].GetPosition(), current.TargetRoot.GetRotation(), Vector3.one);

				// 将预测的骨骼状态转换到目标骨骼的局部坐标系中
				Y.Feed(next.ActorPosture[k].GetPosition().GetRelativePositionTo(targetMatrix), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Position");
				Y.Feed(next.ActorPosture[k].GetForward().GetRelativeDirectionTo(targetMatrix), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Forward");
				Y.Feed(next.ActorPosture[k].GetUp().GetRelativeDirectionTo(targetMatrix), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Up");
				Y.Feed(next.ActorVelocities[k].GetRelativeDirectionTo(targetMatrix), "TargetBone" + (k + 1) + exporter.Editor.GetActor().Bones[k].GetName() + "Velocity");
			}

			//接触点数据 - Contact point data
			// 输出当前时刻的接触状态，用于维持物理约束
			for (int k = next.TimeSeries.Pivot; k <= next.TimeSeries.Pivot; k++)
			{
				Y.Feed(next.ContactSeries.GetContacts(k, contacts), "Contacts-");
			}

			//相位更新 - Phase Update
			// 预测相位的变化，用于维持动作的周期性和连续性
			switch (exporter.PhaseSelection)
			{
				case PHASES.NoPhases:
					break;
				case PHASES.LocalPhases:
					// 预测每个骨骼的相位更新和新状态
					for (int k = next.TimeSeries.Pivot; k < next.TimeSeries.Samples.Length; k++)
					{
						for (int b = 0; b < next.PhaseSeries.Bones.Length; b++)
						{
							// 相位更新：从当前相位到下一相位的变化量
							Y.Feed(Utility.PhaseVector(Utility.SignedPhaseUpdate(current.PhaseSeries.Phases[k][b], next.PhaseSeries.Phases[k][b]), next.PhaseSeries.Amplitudes[k][b]), "PhaseUpdate-" + (k + 1) + "-" + (b + 1));
							// 相位状态：下一时刻的相位值
							Y.Feed(Utility.PhaseVector(next.PhaseSeries.Phases[k][b], next.PhaseSeries.Amplitudes[k][b]), "PhaseState-" + (k + 1) + "-" + (b + 1));	
						}
					}
					break;
				case PHASES.DeepPhases:
					// 深度相位更新：高维相位空间的变化
					Y.Feed(next.DeepPhaseSeries.GetUpdate(), "PhaseUpdate-");
					break;	
				default:
					break;
			}
		}

		/// <summary>
		/// 容器类 - 存储动作数据的各种特征信息
		/// Container class - Stores various feature information of motion data
		/// 
		/// 这个类封装了某个时间戳下的完整角色状态信息，包括：
		/// 1. 基础动作数据：资源、帧信息、时间序列
		/// 2. 多种序列数据：根部、样式、接触、相位等
		/// 3. 角色状态：当前姿态、速度、局部旋转
		/// 4. 目标信息：目标姿态、目标根部变换等
		/// 
		/// 特别重要的是，这里定义了两个关键的坐标系：
		/// - Root: 当前帧的根部坐标系（"以自我为中心坐标系"）
		/// - TargetRoot: 目标帧的根部坐标系（"以目标关键帧为坐标系"）
		/// </summary>
		private class Container
		{
			public MotionData Asset;    // 动作数据资源 - Motion data asset
			public Frame Frame;         // 当前帧 - Current frame

			// 时间序列数据 - Time series data
			public TimeSeries TimeSeries;               // 时间序列基础信息
			public RootSeries RootSeries;               // 根部轨迹序列 - Root trajectory series
			public StyleSeries StyleSeries;             // 样式标签序列 - Style label series  
			public ContactSeries ContactSeries;         // 接触点序列 - Contact point series
			public PhaseSeries PhaseSeries;             // 相位序列 - Phase series
			public DeepPhaseSeries DeepPhaseSeries;     // 深度相位序列 - Deep phase series
			
			// 角色特征数据 - Actor feature data
			public Matrix4x4 Root;                      // 【坐标系1】当前根部变换矩阵 - Current root transformation matrix ("以自我为中心坐标系")
			public Matrix4x4[] ActorPosture;            // 角色姿态变换矩阵数组 - Actor posture transformation matrices
			public Vector3[] ActorVelocities;           // 角色各骨骼速度 - Actor bone velocities
			public Quaternion[] ActorLocalRotations;    // 角色局部旋转 - Actor local rotations
			
			// 目标信息数据 - Target information data
			public InBetweeningModule.SamplePose TargetPose;  // 目标姿态采样 - Target pose sample
			public RootSeries TargetRootSeries;         // 目标根部序列 - Target root series
			public Matrix4x4 TargetRoot;                // 【坐标系2】目标根部变换矩阵 - Target root transformation matrix ("以目标关键帧为坐标系")
			public float[] TargetBoneDistances;         // 到目标骨骼的距离 - Distances to target bones

			/// <summary>
			/// 容器构造函数 - 根据给定时间戳提取所有相关的动作特征
			/// Container constructor - Extracts all relevant motion features for given timestamp
			/// </summary>
			/// <param name="editor">动作编辑器</param>
			/// <param name="timestamp">时间戳</param>
			public Container(MotionEditor editor, float timestamp)
			{
				// 加载指定时间戳的帧数据
				editor.LoadFrame(timestamp);

				Asset = editor.GetAsset();
				Frame = editor.GetCurrentFrame();

				// 提取各种时间序列数据
				TimeSeries = editor.GetTimeSeries();
				RootSeries = (RootSeries)Asset.GetModule<RootModule>().ExtractSeries(TimeSeries, timestamp, editor.Mirror);
				ContactSeries = (ContactSeries)Asset.GetModule<ContactModule>().ExtractSeries(TimeSeries, timestamp, editor.Mirror);
				StyleSeries = (StyleSeries)Asset.GetModule<StyleModule>().ExtractSeries(TimeSeries, timestamp, editor.Mirror);
				if(Asset.HasModule<PhaseModule>()) {
					PhaseSeries = (PhaseSeries)Asset.GetModule<PhaseModule>().ExtractSeries(TimeSeries, timestamp, editor.Mirror);
				}
				DeepPhaseSeries = (DeepPhaseSeries)Asset.GetModule<InBetweeningModule>().ExtractSeries(TimeSeries, timestamp, editor.Mirror);
				
				// 【重要】建立"以自我为中心坐标系" - Establish "ego-centric coordinate system"
				Root = editor.GetActor().transform.GetWorldMatrix();
				ActorPosture = editor.GetActor().GetBoneTransformations();

				// 提取角色的局部旋转信息
				ActorLocalRotations = new Quaternion[ActorPosture.Length];
				for(int i=0; i<ActorPosture.Length; i++)
				{
					ActorLocalRotations[i] = editor.GetActor().Bones[i].GetTransform().localRotation;
				}

				// 提取角色的骨骼速度信息
				ActorVelocities = editor.GetActor().GetBoneVelocities();

				// 【核心逻辑】采样未来目标姿态 - Sample future target pose
				// 这是建立"以目标关键帧为坐标系"的关键步骤
				InBetweeningModule betweening = Asset.GetModule<InBetweeningModule>();
				betweening.FuturePose = betweening.SampleFuturePose(timestamp, editor.Mirror, editor.GetActor().GetBoneNames(), minFrames:1, maxFrames:60);
				TargetPose = betweening.FuturePose;

				// 【重要】建立"以目标关键帧为坐标系" - Establish "target keyframe coordinate system"
				// 计算目标时间戳的根部变换，这将作为第二个坐标系的原点
				TargetRootSeries = (RootSeries)Asset.GetModule<RootModule>().ExtractSeries(TimeSeries, timestamp + betweening.FuturePose.TimeOffset, editor.Mirror);
				TargetRoot = Asset.GetModule<RootModule>().GetRootTransformation(timestamp + betweening.FuturePose.TimeOffset, editor.Mirror);	
				
				// 计算当前姿态到目标姿态的骨骼距离，用于分析动作幅度
				TargetBoneDistances = GetBoneDistances(ActorPosture, betweening.FuturePose.Pose);
			}

			/// <summary>
			/// 计算两组骨骼变换之间的欧氏距离
			/// Calculate Euclidean distances between two sets of bone transformations
			/// </summary>
			/// <param name="from">起始骨骼变换</param>
			/// <param name="to">目标骨骼变换</param>
			/// <returns>距离数组</returns>
			public float[] GetBoneDistances(Matrix4x4[] from, Matrix4x4[] to) {
				float[] distances = new float[from.Length];
				for(int i=0; i<distances.Length; i++) {
					distances[i] = Vector3.Distance(from[i].GetPosition(), to[i].GetPosition());
				}
				return distances;
			}
		}
	}
}
#endif