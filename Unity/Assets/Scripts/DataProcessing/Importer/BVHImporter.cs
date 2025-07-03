using UnityEngine; // Unity核心引擎命名空间
using UnityEditor; // Unity编辑器功能命名空间
using System.IO; // 系统文件输入输出命名空间
using System.Collections; // 集合和协程命名空间
using System.Collections.Generic; // 泛型集合命名空间

// BVH导入器：用于导入BVH(BioVision Hierarchy)运动捕捉文件的Unity编辑器窗口
public class BVHImporter : EditorWindow {

	[System.Serializable] // 标记为可序列化，允许在Inspector中显示
	public class Asset {
		public FileInfo Object = null; // 文件信息对象，存储BVH文件的元数据
		public bool Import = true; // 是否导入此文件的标志
	}

	public static EditorWindow Window; // 静态窗口引用
	public static Vector2 Scroll; // 滚动视图的位置

	public float Scale = 1f; // 导入时的缩放比例

	public bool Flip = false; // 是否翻转动画数据
	public Axis Axis = Axis.XPositive; // 翻转轴向

	public string Source = string.Empty; // 源文件夹路径
	public string Destination = string.Empty; // 目标文件夹路径（相对于Assets/）
	public string Filter = string.Empty; // 文件名过滤器
	public Asset[] Assets = new Asset[0]; // 所有发现的BVH文件资源
	public Asset[] Instances = new Asset[0]; // 经过过滤后显示的文件实例
	public bool Importing = false; // 是否正在导入过程中
	
	public int Page = 1; // 当前页码（用于分页显示）
	public const int Items = 25; // 每页显示的文件数量

	[MenuItem ("AI4Animation/Importer/BVH Importer")] // 在Unity菜单栏创建菜单项
	static void Init() {
		Window = EditorWindow.GetWindow(typeof(BVHImporter)); // 获取或创建编辑器窗口
		Scroll = Vector3.zero; // 初始化滚动位置
	}
	
	void OnGUI() {
		Scroll = EditorGUILayout.BeginScrollView(Scroll); // 开始滚动视图

		Utility.SetGUIColor(UltiDraw.Black); // 设置GUI颜色为黑色
		using(new EditorGUILayout.VerticalScope ("Box")) { // 创建垂直布局的盒子
			Utility.ResetGUIColor(); // 重置GUI颜色

			Utility.SetGUIColor(UltiDraw.Grey); // 设置GUI颜色为灰色
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Utility.ResetGUIColor();

				Utility.SetGUIColor(UltiDraw.Orange); // 设置标题颜色为橙色
				using(new EditorGUILayout.VerticalScope ("Box")) {
					Utility.ResetGUIColor();
					EditorGUILayout.LabelField("BVH Importer"); // 显示标题
				}
		
				if(!Importing) { // 如果没有在导入过程中
					if(Utility.GUIButton("Load Directory", UltiDraw.DarkGrey, UltiDraw.White)) {
						LoadDirectory(); // 加载目录按钮
					}
					if(Utility.GUIButton("Import Motion Data", UltiDraw.DarkGrey, UltiDraw.White)) {
						this.StartCoroutine(ImportMotionData()); // 开始导入动画数据的协程
					}
				} else { // 如果正在导入中
					if(Utility.GUIButton("Stop", UltiDraw.DarkRed, UltiDraw.White)) {
						this.StopAllCoroutines(); // 停止所有协程
						Importing = false; // 设置导入状态为false
					}
				}

				using(new EditorGUILayout.VerticalScope ("Box")) {
					EditorGUILayout.LabelField("Source"); // 源文件夹标签
					EditorGUILayout.BeginHorizontal(); // 开始水平布局
					EditorGUILayout.LabelField("<Path>", GUILayout.Width(50)); // 路径标签
					Source = EditorGUILayout.TextField(Source); // 源路径输入框
					GUI.skin.button.alignment = TextAnchor.MiddleCenter; // 设置按钮文本居中对齐
					if(GUILayout.Button("O", GUILayout.Width(20))) { // 打开文件夹选择对话框的按钮
						Source = EditorUtility.OpenFolderPanel("BVH Importer", Source == string.Empty ? Application.dataPath : Source, "");
						GUIUtility.ExitGUI(); // 退出当前GUI绘制，防止布局错误
					}
					EditorGUILayout.EndHorizontal(); // 结束水平布局

					EditorGUILayout.LabelField("Destination"); // 目标文件夹标签
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Assets/", GUILayout.Width(50)); // Assets/前缀标签
					Destination = EditorGUILayout.TextField(Destination); // 目标路径输入框（相对于Assets/）
					EditorGUILayout.EndHorizontal();

					string filter = EditorGUILayout.TextField("Filter", Filter); // 过滤器输入框
					if(Filter != filter) { // 如果过滤器发生变化
						Filter = filter;
						ApplyFilter(); // 应用新的过滤器
					}

					Scale = EditorGUILayout.FloatField("Scale", Scale); // 缩放比例输入框

					EditorGUILayout.BeginHorizontal();
					Flip = EditorGUILayout.Toggle("Flip", Flip); // 翻转开关
					Axis = (Axis)EditorGUILayout.EnumPopup(Axis); // 轴向选择下拉菜单
					EditorGUILayout.EndHorizontal();

					int start = (Page-1)*Items; // 计算当前页的起始索引
					int end = Mathf.Min(start+Items, Instances.Length); // 计算当前页的结束索引
					int pages = Mathf.CeilToInt(Instances.Length/Items)+1; // 计算总页数
					Utility.SetGUIColor(UltiDraw.Orange);
					using(new EditorGUILayout.VerticalScope ("Box")) {
						Utility.ResetGUIColor();
						EditorGUILayout.BeginHorizontal();
						if(Utility.GUIButton("<", UltiDraw.DarkGrey, UltiDraw.White)) {
							Page = Mathf.Max(Page-1, 1); // 上一页按钮，确保不小于1
						}
						EditorGUILayout.LabelField("Page " + Page + "/" + pages); // 显示页码信息
						if(Utility.GUIButton(">", UltiDraw.DarkGrey, UltiDraw.White)) {
							Page = Mathf.Min(Page+1, pages); // 下一页按钮，确保不超过总页数
						}
						EditorGUILayout.EndHorizontal();
					}
					EditorGUILayout.BeginHorizontal();
					if(Utility.GUIButton("Enable All", UltiDraw.DarkGrey, UltiDraw.White)) {
						for(int i=0; i<Instances.Length; i++) {
							Instances[i].Import = true; // 启用所有文件的导入标志
						}
					}
					if(Utility.GUIButton("Disable All", UltiDraw.DarkGrey, UltiDraw.White)) {
						for(int i=0; i<Instances.Length; i++) {
							Instances[i].Import = false; // 禁用所有文件的导入标志
						}
					}
					EditorGUILayout.EndHorizontal();
					for(int i=start; i<end; i++) { // 遍历当前页的文件
						if(Instances[i].Import) {
							Utility.SetGUIColor(UltiDraw.DarkGreen); // 启用导入的文件显示为绿色
						} else {
							Utility.SetGUIColor(UltiDraw.DarkRed); // 禁用导入的文件显示为红色
						}
						using(new EditorGUILayout.VerticalScope ("Box")) {
							Utility.ResetGUIColor();
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.LabelField((i+1).ToString(), GUILayout.Width(20f)); // 显示文件序号
							Instances[i].Import = EditorGUILayout.Toggle(Instances[i].Import, GUILayout.Width(20f)); // 导入开关
							EditorGUILayout.LabelField(Instances[i].Object.Name); // 显示文件名
							EditorGUILayout.EndHorizontal();
						}
					}
				}
		
			}
		}

		EditorGUILayout.EndScrollView(); // 结束滚动视图
	}

	private void LoadDirectory() {
		if(Directory.Exists(Source)) { // 检查源目录是否存在
			DirectoryInfo info = new DirectoryInfo(Source); // 创建目录信息对象
			FileInfo[] assets = info.GetFiles("*.bvh"); // 获取所有.bvh文件
			Assets = new Asset[assets.Length]; // 创建资源数组
			for(int i=0; i<assets.Length; i++) {
				Assets[i] = new Asset(); // 创建新的资源对象
				Assets[i].Object = assets[i]; // 设置文件信息
				Assets[i].Import = true; // 默认设置为导入
			}
		} else {
			Assets = new Asset[0]; // 如果目录不存在，创建空数组
		}
		ApplyFilter(); // 应用过滤器
		Page = 1; // 重置到第一页
	}

	private void ApplyFilter() {
		if(Filter == string.Empty) { // 如果没有过滤器
			Instances = Assets; // 显示所有资源
		} else {
			List<Asset> instances = new List<Asset>(); // 创建过滤后的列表
			for(int i=0; i<Assets.Length; i++) {
				if(Assets[i].Object.Name.ToLowerInvariant().Contains(Filter.ToLowerInvariant())) { // 不区分大小写的文件名过滤
					instances.Add(Assets[i]); // 添加符合过滤条件的文件
				}
			}
			Instances = instances.ToArray(); // 转换为数组
		}
	}

	private IEnumerator ImportMotionData() {
		string destination = "Assets/" + Destination; // 构建完整的目标路径
		if(!AssetDatabase.IsValidFolder(destination)) { // 检查目标文件夹是否有效
			Debug.Log("Folder " + "'" + destination + "'" + " is not valid.");
		} else {
			Importing = true; // 设置导入状态为true
			for(int f=0; f<Assets.Length; f++) { // 遍历所有资源文件
				if(Assets[f].Import) { // 如果该文件被标记为导入
					string assetName = Assets[f].Object.Name.Replace(".bvh", ""); // 移除文件扩展名作为资源名
					if(!Directory.Exists(destination+"/"+assetName) ) { // 如果目标目录不存在
						AssetDatabase.CreateFolder(destination, assetName); // 创建文件夹
						MotionData data = ScriptableObject.CreateInstance<MotionData>(); // 创建MotionData实例
						data.name = assetName; // 设置名称
						AssetDatabase.CreateAsset(data, destination+"/"+assetName+"/"+data.name+".asset"); // 创建资源文件

						string[] lines = System.IO.File.ReadAllLines(Assets[f].Object.FullName); // 读取BVH文件的所有行
						char[] whitespace = new char[] {' '}; // 定义空格分隔符
						int index = 0; // 当前处理行的索引

						//创建源数据结构
						//Create Source Data
						List<Vector3> offsets = new List<Vector3>(); // 存储骨骼偏移量
						List<int[]> channels = new List<int[]>(); // 存储通道信息（位置和旋转）
						List<float[]> motions = new List<float[]>(); // 存储运动数据
						data.Source = new MotionData.Hierarchy(); // 创建层次结构
						string name = string.Empty; // 当前骨骼名称
						string parent = string.Empty; // 父骨骼名称
						Vector3 offset = Vector3.zero; // 当前骨骼偏移量
						int[] channel = null; // 当前骨骼的通道数组
						for(index = 0; index<lines.Length; index++) { // 解析BVH文件的层次结构部分
							if(lines[index] == "MOTION") { // 如果遇到MOTION关键字，停止解析层次结构
								break;
							}
							string[] entries = lines[index].Split(whitespace); // 按空格分割当前行
							for(int entry=0; entry<entries.Length; entry++) { // 遍历当前行的每个词条
								if(entries[entry].Contains("ROOT")) { // 如果是根节点
									parent = "None"; // 父节点设为None
									name = entries[entry+1]; // 获取根节点名称
									break;
								} else if(entries[entry].Contains("JOINT")) { // 如果是关节节点
									parent = name; // 当前节点成为父节点
									name = entries[entry+1]; // 获取关节名称
									break;
								} else if(entries[entry].Contains("End")) { // 如果是末端节点
									parent = name; // 当前节点成为父节点
									name = name+entries[entry+1]; // 末端节点名称为父节点名+End标识
									string[] subEntries = lines[index+2].Split(whitespace); // 解析偏移量行
									for(int subEntry=0; subEntry<subEntries.Length; subEntry++) {
										if(subEntries[subEntry].Contains("OFFSET")) { // 解析偏移量
											offset.x = FileUtility.ReadFloat(subEntries[subEntry+1]);
											offset.y = FileUtility.ReadFloat(subEntries[subEntry+2]);
											offset.z = FileUtility.ReadFloat(subEntries[subEntry+3]);
											break;
										}
									}
									data.Source.AddBone(name, parent); // 添加骨骼到层次结构
									offsets.Add(offset); // 添加偏移量
									channels.Add(new int[0]); // 末端节点没有通道数据
									index += 2; // 跳过偏移量行
									break;
								} else if(entries[entry].Contains("OFFSET")) { // 解析偏移量
									offset.x = FileUtility.ReadFloat(entries[entry+1]);
									offset.y = FileUtility.ReadFloat(entries[entry+2]);
									offset.z = FileUtility.ReadFloat(entries[entry+3]);
									break;
								} else if(entries[entry].Contains("CHANNELS")) { // 解析通道信息
									channel = new int[FileUtility.ReadInt(entries[entry+1])]; // 创建通道数组
									for(int i=0; i<channel.Length; i++) {
										// 映射通道类型到数字：1-3为位置，4-6为旋转
										if(entries[entry+2+i] == "Xposition") {
											channel[i] = 1;
										} else if(entries[entry+2+i] == "Yposition") {
											channel[i] = 2;
										} else if(entries[entry+2+i] == "Zposition") {
											channel[i] = 3;
										} else if(entries[entry+2+i] == "Xrotation") {
											channel[i] = 4;
										} else if(entries[entry+2+i] == "Yrotation") {
											channel[i] = 5;
										} else if(entries[entry+2+i] == "Zrotation") {
											channel[i] = 6;
										}
									}
									data.Source.AddBone(name, parent); // 添加骨骼到层次结构
									offsets.Add(offset); // 添加偏移量
									channels.Add(channel); // 添加通道信息
									break;
								} else if(entries[entry].Contains("}")) { // 如果遇到右花括号，回到上一级
									name = parent; // 回到父节点
									parent = name == "None" ? "None" : data.Source.FindBone(name).Parent; // 获取父节点的父节点
									break;
								}
							}
						}

						//设置帧数
						//Set Frames
						index += 1; // 跳到帧数行
						while(lines[index].Length == 0) { // 跳过空行
							index += 1;
						}
						ArrayExtensions.Resize(ref data.Frames, FileUtility.ReadInt(lines[index].Substring(8))); // 从"Frames: "后读取帧数

						//设置帧率
						//Set Framerate
						index += 1; // 跳到帧率行
						data.Framerate = Mathf.RoundToInt(1f / FileUtility.ReadFloat(lines[index].Substring(12))); // 从"Frame Time: "后读取并转换为帧率

						//计算帧数据
						//Compute Frames
						index += 1; // 跳到运动数据开始行
						for(int i=index; i<lines.Length; i++) { // 读取所有运动数据行
							motions.Add(FileUtility.ReadArray(lines[i])); // 将每行数据解析为浮点数组
						}
						for(int k=0; k<data.GetTotalFrames(); k++) { // 为每一帧计算变换矩阵
							Matrix4x4[] matrices = new Matrix4x4[data.Source.Bones.Length]; // 存储每个骨骼的变换矩阵
							int idx = 0; // 运动数据索引
							for(int i=0; i<data.Source.Bones.Length; i++) { // 遍历每个骨骼
								MotionData.Hierarchy.Bone info = data.Source.Bones[i]; // 获取骨骼信息
								Vector3 position = Vector3.zero; // 初始化位置
								Quaternion rotation = Quaternion.identity; // 初始化旋转
								for(int j=0; j<channels[i].Length; j++) { // 遍历该骨骼的所有通道
									// 根据通道类型读取相应的数据
									if(channels[i][j] == 1) { // X位置
										position.x = motions[k][idx]; idx += 1;
									}
									if(channels[i][j] == 2) { // Y位置
										position.y = motions[k][idx]; idx += 1;
									}
									if(channels[i][j] == 3) { // Z位置
										position.z = motions[k][idx]; idx += 1;
									}
									if(channels[i][j] == 4) { // X旋转
										rotation *= Quaternion.AngleAxis(motions[k][idx], Vector3.right); idx += 1;
									}
									if(channels[i][j] == 5) { // Y旋转
										rotation *= Quaternion.AngleAxis(motions[k][idx], Vector3.up); idx += 1;
									}
									if(channels[i][j] == 6) { // Z旋转
										rotation *= Quaternion.AngleAxis(motions[k][idx], Vector3.forward); idx += 1;
									}
								}

								position = (position == Vector3.zero ? offsets[i] : position) * Scale; // 如果没有位置数据则使用偏移量，并应用缩放
								Matrix4x4 local = Matrix4x4.TRS(position, rotation, Vector3.one); // 创建局部变换矩阵
								if(Flip) { // 如果需要翻转
									local = local.GetMirror(Axis); // 应用镜像变换
								}
								matrices[i] = info.Parent == "None" ? local : matrices[data.Source.FindBone(info.Parent).Index] * local; // 计算世界变换矩阵
							}
							data.Frames[k] = new Frame(data, k+1, (float)k / data.Framerate, matrices); // 创建帧对象
							/*
							// 可选的校正代码（已注释）
							for(int i=0; i<data.Source.Bones.Length; i++) {
								data.Frames[k].Local[i] *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(data.Corrections[i]), Vector3.one);
								data.Frames[k].World[i] *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(data.Corrections[i]), Vector3.one);
							}
							*/
						}

						if(data.GetTotalFrames() == 1) { // 如果只有一帧数据（静态姿态）
							Frame reference = data.Frames.First(); // 获取参考帧
							ArrayExtensions.Resize(ref data.Frames, Mathf.RoundToInt(data.Framerate)); // 扩展为一秒的帧数
							for(int k=0; k<data.GetTotalFrames(); k++) { // 复制参考帧到所有帧
								data.Frames[k] = new Frame(data, k+1, (float)k / data.Framerate, reference.GetSourceTransformations(false));
							}
						}

						//检测对称性
						//Detect Symmetry
						data.DetectSymmetry(); // 自动检测骨骼的左右对称关系

						//添加场景
						//Add Scene
						data.CreateScene(); // 创建场景预制体
						data.AddSequence(); // 添加动画序列

						//保存
						//Save
						EditorUtility.SetDirty(data); // 标记资源为已修改
					} else {
						Debug.Log("Asset with name " + assetName + " already exists."); // 如果资源已存在则输出日志
					}

					yield return new WaitForSeconds(0f); // 让出控制权，避免阻塞编辑器
				}
			}
			AssetDatabase.SaveAssets(); // 保存所有资源
			AssetDatabase.Refresh(); // 刷新资源数据库
			Importing = false; // 设置导入状态为false
		}
		yield return new WaitForSeconds(0f); // 协程结束
	}
}
