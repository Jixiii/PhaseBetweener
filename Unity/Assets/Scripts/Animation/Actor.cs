using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode] // 允许在编辑器模式下执行
public class Actor : MonoBehaviour {

    public enum DRAW {Skeleton, Sketch}; // 绘制模式枚举：骨骼模式或简单线条模式

    public static bool Inspect = false; // 静态变量，控制编辑器检视器的展开状态
    
    public bool AllowRealignment = true; // 是否允许骨骼重新对齐

    // 绘制设置
    public bool DrawRoot = false; // 是否绘制根节点
    public bool DrawSkeleton = true; // 是否绘制骨骼
    public bool DrawTransforms = false; // 是否绘制变换坐标轴
    public bool DrawVelocities = false; // 是否绘制速度向量
    public bool DrawAlignment = false; // 是否绘制对齐轴

    // 绘制样式设置
    public float BoneSize = 0.025f; // 骨骼绘制大小
    public Color BoneColor = UltiDraw.Cyan; // 骨骼颜色
    public Color JointColor = UltiDraw.Mustard; // 关节颜色

    public Bone[] Bones = new Bone[0]; // 骨骼数组

    private string[] BoneNames = null; // 骨骼名称缓存数组

    void Reset() {
        // Unity组件重置时调用，自动创建骨骼系统
        Create(GetComponentsInChildren<Transform>());
    }

    public void CopySetup(Actor reference) {
        // 从参考Actor复制骨骼设置
        Create(reference.GetBoneNames());
    }

    public void RenameBones(string from, string to) {
        // 批量重命名骨骼：将包含from字符串的名称替换为to
        void Recursion(Transform t) {
            t.name = t.name.Replace(from, to);
            for(int i=0; i<t.childCount; i++) {
                Recursion(t.GetChild(i));
            }
        }
        Recursion(transform);
        BoneNames = new string[0]; // 清空缓存，强制重新生成
    }

    public void SwitchNames(string a, string b) {
        // 交换骨骼名称：包含a的替换为b，包含b的替换为a
        void Recursion(Transform t) {
            string name = t.name;
            if(name.Contains(a)) {
                t.name = t.name.Replace(a, b);
            }
            if(name.Contains(b)) {
                t.name = t.name.Replace(b, a);
            }
            for(int i=0; i<t.childCount; i++) {
                Recursion(t.GetChild(i));
            }
        }
        Recursion(transform);
        BoneNames = new string[0]; // 清空名称缓存
    }

    public Transform GetRoot() {
        // 获取根变换节点
        return transform;
    }

    public Bone[] GetRootBones() {
        // 获取所有根骨骼（没有父骨骼的骨骼）
        List<Bone> bones = new List<Bone>();
        for(int i=0; i<Bones.Length; i++) {
            if(Bones[i].GetParent() == null) {
                bones.Add(Bones[i]);
            }
        }
        return bones.ToArray();
    }

    public Transform[] FindTransforms(params string[] names) {
        // 根据名称数组查找对应的Transform组件数组
        Transform[] transforms = new Transform[names.Length];
        for(int i=0; i<transforms.Length; i++) {
            transforms[i] = FindTransform(names[i]);
        }
        return transforms;
    }

    public Transform FindTransform(string name) {
        // 根据名称查找单个Transform组件（递归搜索）
        Transform element = null;
        Action<Transform> recursion = null;
        recursion = new Action<Transform>((transform) => {
            if(transform.name == name) {
                element = transform;
                return;
            }
            for(int i=0; i<transform.childCount; i++) {
                recursion(transform.GetChild(i));
            }
        });
        recursion(GetRoot());
        return element;
    }

    public Bone[] FindBones(params Transform[] transforms) {
        // 根据Transform数组查找对应的Bone数组
        Bone[] bones = new Bone[transforms.Length];
        for(int i=0; i<bones.Length; i++) {
            bones[i] = FindBone(transforms[i]);
        }
        return bones;
    }

    public Bone[] FindBones(params string[] names) {
        // 根据名称数组查找对应的Bone数组
        Bone[] bones = new Bone[names.Length];
        for(int i=0; i<bones.Length; i++) {
            bones[i] = FindBone(names[i]);
        }
        return bones;
    }

    public Bone FindBone(Transform transform) {
        // 根据Transform查找对应的Bone
        return Array.Find(Bones, x => x.GetTransform() == transform);
    }

    public Bone FindBone(string name) {
        // 根据名称查找对应的Bone
        return Array.Find(Bones, x => x.GetName() == name);
    }

    public string[] GetBoneNames() {
        // 获取所有骨骼名称（带缓存机制）
        if(BoneNames == null || BoneNames.Length != Bones.Length) {
            BoneNames = new string[Bones.Length];
            for(int i=0; i<BoneNames.Length; i++) {
                BoneNames[i] = Bones[i].GetName();
            }
        }
        return BoneNames;
    }

    public int[] GetBoneIndices(params string[] names) {
        // 根据骨骼名称获取对应的索引数组
        int[] indices = new int[names.Length];
        for(int i=0; i<indices.Length; i++) {
            indices[i] = FindBone(names[i]).GetIndex();
        }
        return indices;
    }

    public Transform[] GetBoneTransforms(params string[] names) {
        // 根据骨骼名称获取对应的Transform数组
        Transform[] transforms = new Transform[names.Length];
        for(int i=0; i<names.Length; i++) {
            transforms[i] = FindTransform(names[i]);
        }
        return transforms;
    }

    public void Create() {
        // 创建空的骨骼系统
        Create(null as Transform);
    }

    public void Create(params Transform[] bones) {
        // 根据Transform数组创建骨骼系统
        ArrayExtensions.Clear(ref Bones); // 清空现有骨骼数组
        Action<Transform, Bone> recursion = null;
        recursion = new Action<Transform, Bone>((transform, parent) => {
            // 如果bones为空或当前transform在bones数组中，则创建骨骼
            if(bones == null || System.Array.Find(bones, x => x == transform)) {
                Bone bone = new Bone(this, transform, Bones.Length, parent);
                ArrayExtensions.Append(ref Bones, bone);
                parent = bone;
            }
            // 递归处理子节点
            for(int i=0; i<transform.childCount; i++) {
                recursion(transform.GetChild(i), parent);
            }
        });
        recursion(GetRoot(), null);
        BoneNames = new string[0]; // 清空名称缓存
    }

    public void Create(params string[] bones) {
        // 根据骨骼名称数组创建骨骼系统
        Create(FindTransforms(bones));
    }

    public void SetBoneTransformations(Matrix4x4[] values) {
        // 设置所有骨骼的变换矩阵
        if(values.Length != Bones.Length) {
            return;
        }
        for(int i=0; i<Bones.Length; i++) {
            Bones[i].SetTransformation(values[i]);
        }
    }

    public void SetBoneTransformations(Matrix4x4[] values, params string[] bones) {
        // 设置指定骨骼的变换矩阵
        for(int i=0; i<bones.Length; i++) {
            SetBoneTransformation(values[i], bones[i]);
        }
    }

    public void SetBoneTransformation(Matrix4x4 value, string bone) {
        // 设置单个骨骼的变换矩阵
        Bone b = FindBone(bone);
        if(b != null) {
            b.SetTransformation(value);
        }
    }

    public Matrix4x4[] GetBoneTransformations() {
        // 获取所有骨骼的变换矩阵
        Matrix4x4[] transformations = new Matrix4x4[Bones.Length];
        for(int i=0; i<transformations.Length; i++) {
            transformations[i] = Bones[i].GetTransformation();
        }
        return transformations;
    }

    public Matrix4x4[] GetBoneTransformations(params int[] bones) {
        // 根据索引数组获取对应骨骼的变换矩阵
        Matrix4x4[] transformations = new Matrix4x4[bones.Length];
        for(int i=0; i<transformations.Length; i++) {
            transformations[i] = Bones[bones[i]].GetTransformation();
        }
        return transformations;
    }

    public Matrix4x4[] GetBoneTransformations(params string[] bones) {
        // 根据名称数组获取对应骨骼的变换矩阵
        Matrix4x4[] transformations = new Matrix4x4[bones.Length];
        for(int i=0; i<transformations.Length; i++) {
            transformations[i] = GetBoneTransformation(bones[i]);
        }
        return transformations;
    }

    public Matrix4x4 GetBoneTransformation(string bone) {
        // 获取单个骨骼的变换矩阵
        return FindBone(bone).GetTransformation();
    }

    public void SetBoneVelocities(Vector3[] values) {
        // 设置所有骨骼的速度
        if(values.Length != Bones.Length) {
            return;
        }
        for(int i=0; i<Bones.Length; i++) {
            Bones[i].SetVelocity(values[i]);
        }
    }

    public void SetBoneVelocities(Vector3[] values, params string[] bones) {
        // 设置指定骨骼的速度
        for(int i=0; i<bones.Length; i++) {
            SetBoneVelocity(values[i], bones[i]);
        }
    }

    public void SetBoneVelocity(Vector3 value, string bone) {
        // 设置单个骨骼的速度
        Bone b = FindBone(bone);
        if(b != null) {
            b.SetVelocity(value);
        }
    }

    public Vector3[] GetBoneVelocities() {
        // 获取所有骨骼的速度
        Vector3[] velocities = new Vector3[Bones.Length];
        for(int i=0; i<velocities.Length; i++) {
            velocities[i] = Bones[i].GetVelocity();
        }
        return velocities;
    }

    public Vector3[] GetBoneVelocities(params int[] bones) {
        // 根据索引数组获取对应骨骼的速度
        Vector3[] velocities = new Vector3[bones.Length];
        for(int i=0; i<velocities.Length; i++) {
            velocities[i] = Bones[bones[i]].GetVelocity();
        }
        return velocities;
    }

    public Vector3[] GetBoneVelocities(params string[] bones) {
        // 根据名称数组获取对应骨骼的速度
        Vector3[] velocities = new Vector3[bones.Length];
        for(int i=0; i<velocities.Length; i++) {
            velocities[i] = GetBoneVelocity(bones[i]);
        }
        return velocities;
    }

    public Vector3 GetBoneVelocity(string bone) {
        // 获取单个骨骼的速度
        return FindBone(bone).GetVelocity();
    }

    public Vector3[] GetBonePositions() {
        // 获取所有骨骼的位置
        Vector3[] positions = new Vector3[Bones.Length];
        for(int i=0; i<positions.Length; i++) {
            positions[i] = Bones[i].GetPosition();
        }
        return positions;
    }

    public Vector3[] GetBonePositions(params int[] bones) {
        // 根据索引数组获取对应骨骼的位置
        Vector3[] positions = new Vector3[bones.Length];
        for(int i=0; i<positions.Length; i++) {
            positions[i] = Bones[bones[i]].GetPosition();
        }
        return positions;
    }

    public Vector3[] GetBonePositions(params string[] bones) {
        // 根据名称数组获取对应骨骼的位置
        Vector3[] positions = new Vector3[bones.Length];
        for(int i=0; i<positions.Length; i++) {
            positions[i] = GetBonePosition(bones[i]);
        }
        return positions;
    }

    public Vector3 GetBonePosition(string bone) {
        // 获取单个骨骼的位置
        return FindBone(bone).GetPosition();
    }

    public Quaternion[] GetBoneRotations() {
        // 获取所有骨骼的旋转
        Quaternion[] rotation = new Quaternion[Bones.Length];
        for(int i=0; i<rotation.Length; i++) {
            rotation[i] = Bones[i].GetRotation();
        }
        return rotation;
    }

    public Quaternion[] GetBoneRotations(params string[] bones) {
        // 根据名称数组获取对应骨骼的旋转
        Quaternion[] rotation = new Quaternion[bones.Length];
        for(int i=0; i<rotation.Length; i++) {
            rotation[i] = GetBoneRotation(bones[i]);
        }
        return rotation;
    }

    public Quaternion[] GetBoneRotations(params int[] bones) {
        // 根据索引数组获取对应骨骼的旋转
        Quaternion[] rotations = new Quaternion[bones.Length];
        for(int i=0; i<rotations.Length; i++) {
            rotations[i] = Bones[bones[i]].GetRotation();
        }
        return rotations;
    }

    public Quaternion GetBoneRotation(string bone) {
        // 获取单个骨骼的旋转
        return FindBone(bone).GetRotation();
    }

    public void RestoreAlignment() {
        // 恢复所有骨骼的对齐状态（用于动画重定向）
        foreach(Bone bone in Bones) {
            bone.RestoreAlignment();
        }
    }

    public void Draw(Matrix4x4[] transformations, Color boneColor, Color jointColor, DRAW mode) {
        // 绘制骨骼系统（使用自定义变换矩阵）
        if(transformations.Length != Bones.Length) {
            Debug.Log("Number of given transformations does not match number of bones.");
            return;
        }
        UltiDraw.Begin(); // 开始绘制
        if(mode == DRAW.Skeleton) {
            // 骨骼模式：绘制3D骨骼和球形关节
            void Recursion(Bone bone) {
                Matrix4x4 current = transformations[bone.GetIndex()];
                if(bone.GetParent() != null) {
                    Matrix4x4 parent = transformations[bone.GetParent().GetIndex()];
                    // 绘制骨骼（从父节点到当前节点的3D骨骼形状）
                    UltiDraw.DrawBone(
                        parent.GetPosition(),
                        Quaternion.FromToRotation(parent.GetForward(), current.GetPosition() - parent.GetPosition()) * parent.GetRotation(),
                        12.5f*BoneSize*bone.GetLength(), bone.GetLength(),
                        boneColor
                    );
                }
                // 绘制关节（球形）
                UltiDraw.DrawSphere(
                    current.GetPosition(),
                    Quaternion.identity,
                    5f/8f * BoneSize,
                    jointColor
                );
                // 递归绘制子骨骼
                for(int i=0; i<bone.GetChildCount(); i++) {
                    Recursion(bone.GetChild(i));
                }
            }
            foreach(Bone bone in GetRootBones()) {
                Recursion(bone);
            }
        }
        if(mode == DRAW.Sketch) {
            // 简单模式：绘制线条和立方体关节
            void Recursion(Bone bone) {
                Matrix4x4 current = transformations[bone.GetIndex()];
                if(bone.GetParent() != null) {
                    Matrix4x4 parent = transformations[bone.GetParent().GetIndex()];
                    // 绘制连接线
                    UltiDraw.DrawLine(parent.GetPosition(), current.GetPosition(), boneColor);
                }
                // 绘制立方体关节
                UltiDraw.DrawCube(current.GetPosition(), current.GetRotation(), 0.02f, jointColor);
                // 递归绘制子骨骼
                for(int i=0; i<bone.GetChildCount(); i++) {
                    Recursion(bone.GetChild(i));
                }
            }
            foreach(Bone bone in GetRootBones()) {
                Recursion(bone);
            }
        }
        UltiDraw.End(); // 结束绘制
    }

    public void Draw(Matrix4x4[] transformations, string[] bones, Color boneColor, Color jointColor, DRAW mode) {
        // 绘制指定骨骼的骨骼系统
        if(transformations.Length != bones.Length) {
            Debug.Log("Number of given transformations does not match number of given bones.");
            return;
        }
        UltiDraw.Begin();
        if(mode == DRAW.Skeleton) {
            // 骨骼模式绘制
            void Recursion(Bone bone, int parent) {
                int index = bones.FindIndex(bone.GetName()); // 查找骨骼在指定数组中的索引
                if(index >= 0) {
                    Matrix4x4 boneMatrix = transformations[index];
                    if(parent >= 0) {
                        Matrix4x4 parentMatrix = transformations[parent];
                        float length = Vector3.Distance(parentMatrix.GetPosition(), boneMatrix.GetPosition());
                        UltiDraw.DrawBone(
                            parentMatrix.GetPosition(),
                            Quaternion.FromToRotation(parentMatrix.GetForward(), boneMatrix.GetPosition() - parentMatrix.GetPosition()) * parentMatrix.GetRotation(),
                            12.5f*BoneSize*length, length,
                            boneColor
                        );
                    }
                    UltiDraw.DrawSphere(
                        boneMatrix.GetPosition(),
                        Quaternion.identity,
                        5f/8f * BoneSize,
                        jointColor
                    );
                    parent = index;
                }
                for(int i=0; i<bone.GetChildCount(); i++) {
                    Recursion(bone.GetChild(i), parent);
                }
            }
            foreach(Bone bone in GetRootBones()) {
                Recursion(bone, -1);
            }
        }
        if(mode == DRAW.Sketch) {
            // 简单模式绘制
            void Recursion(Bone bone, int parent) {
                int index = bones.FindIndex(bone.GetName());
                if(index >= 0) {
                    Matrix4x4 boneMatrix = transformations[index];
                    if(parent >= 0) {
                        Matrix4x4 parentMatrix = transformations[parent];
                        UltiDraw.DrawLine(parentMatrix.GetPosition(), boneMatrix.GetPosition(), boneColor);
                    }
                    UltiDraw.DrawCube(boneMatrix.GetPosition(), boneMatrix.GetRotation(), 0.02f, jointColor);
                    parent = index;
                }
                for(int i=0; i<bone.GetChildCount(); i++) {
                    Recursion(bone.GetChild(i), parent);
                }
            }
            foreach(Bone bone in GetRootBones()) {
                Recursion(bone, -1);
            }
        }
        UltiDraw.End();
    }

    public void DrawIcon(Color color) {
        // 绘制Actor图标（金字塔形状，位于骨骼系统顶部）
        UltiDraw.Begin();
        UltiDraw.DrawPyramid(transform.position.SetY(GetBonePositions().Max().y+0.6f), transform.rotation, 0.3f, -0.3f, color);
        UltiDraw.End();
    }

    public Transform CreateVisualInstance() {
        // 创建用于可视化的实例（只保留渲染组件，移除其他组件）
        Transform instance = Instantiate(gameObject).transform;
        foreach(Component c in instance.GetComponentsInChildren<Component>()) {
            if(c is SkinnedMeshRenderer || c is Renderer) {
                // 保留渲染组件
            } else {
                Utility.Destroy(c); // 删除其他组件
            }
        }
        return instance;
    }

    void OnRenderObject() {
        // Unity渲染回调，在每帧渲染时绘制骨骼系统
        if(DrawSkeleton) {
            Draw(GetBoneTransformations(), BoneColor, JointColor, DRAW.Skeleton);
        }

        UltiDraw.Begin();
        if(DrawRoot) {
            // 绘制根节点（黑色立方体和坐标轴）
            UltiDraw.DrawCube(GetRoot().position, GetRoot().rotation, 0.1f, UltiDraw.Black);
            UltiDraw.DrawTranslateGizmo(GetRoot().position, GetRoot().rotation, 0.1f);
        }

        if(DrawVelocities) {
            // 绘制速度向量（绿色箭头）
            for(int i=0; i<Bones.Length; i++) {
                UltiDraw.DrawArrow(
                    Bones[i].GetPosition(),
                    Bones[i].GetPosition() + Bones[i].GetVelocity(),
                    0.75f,
                    0.0075f,
                    0.05f,
                    UltiDraw.DarkGreen.Opacity(0.5f)
                );
            }
        }

        if(DrawTransforms) {
            // 绘制每个骨骼的坐标轴
            foreach(Bone bone in Bones) {
                UltiDraw.DrawTranslateGizmo(bone.GetPosition(), bone.GetRotation(), 0.05f);
            }
        }

        if(DrawAlignment) {
            // 绘制对齐轴（紫色线条）
            foreach(Bone bone in Bones) {
                UltiDraw.DrawLine(bone.GetPosition(), bone.GetPosition() + bone.GetRotation() * bone.GetAlignment(), 0.05f, 0f, UltiDraw.Magenta);
            }
        }
        UltiDraw.End();
    }

    public class State {
        // 状态类：存储骨骼的变换和速度信息（用于状态保存/恢复）
        public Matrix4x4[] Transformations;
        public Vector3[] Velocities;
    }

    [Serializable]
    public class Bone {
        // 骨骼类：表示骨骼层次结构中的单个骨骼
        [SerializeField] private Actor Actor; // 所属的Actor
        [SerializeField] private Transform Transform; // 对应的Unity Transform组件
        [SerializeField] private Vector3 Velocity; // 骨骼速度
        [SerializeField] private int Index; // 在骨骼数组中的索引
        [SerializeField] private int Parent; // 父骨骼索引（-1表示无父骨骼）
        [SerializeField] private int[] Childs; // 子骨骼索引数组
        [SerializeField] private Vector3 Alignment; // 对齐轴：从关节指向子关节的方向，用于旋转对齐 //The axis pointing from the joint's child to this joint along which the rotation needs to be aligned.

        public Bone(Actor actor, Transform transform, int index, Bone parent) {
            // 骨骼构造函数
            Actor = actor;
            Transform = transform;
            Velocity = Vector3.zero;
            Index = index;
            Childs = new int[0];
            if(parent != null) {
                Parent = parent.Index;
                ArrayExtensions.Append(ref parent.Childs, Index); // 将自己添加到父骨骼的子列表中
            } else {
                Parent = -1;
            }
        }

        public Actor GetActor() {
            return Actor;
        }

        public Transform GetTransform() {
            return Transform;
        }

        public string GetName() {
            return Transform.name;
        }

        public int GetIndex() {
            return Index;
        }

        public Bone GetParent() {
            // 获取父骨骼（如果没有则返回null）
            return Parent == -1 ? null : Actor.Bones[Parent];
        }

        public Bone GetChild(int index) {
            // 获取指定索引的子骨骼
            return index >= Childs.Length ? null : Actor.Bones[Childs[index]];
        }

        public int GetChildCount() {
            return Childs.Length;
        }

        public float GetLength() {
            // 获取骨骼长度（到父骨骼的距离）
            return GetParent() == null ? 0f : Vector3.Distance(GetParent().Transform.position, Transform.position);
        }

        public void SetTransformation(Matrix4x4 matrix) {
            // 设置变换矩阵（位置和旋转）
            Transform.position = matrix.GetPosition();
            Transform.rotation = matrix.GetRotation();
        }

        public Matrix4x4 GetTransformation() {
            // 获取世界变换矩阵
            return Transform.GetWorldMatrix();
        }

        public void SetPosition(Vector3 position) {
            Transform.position = position;
        }

        public Vector3 GetPosition() {
            return Transform.position;
        }

        public void SetRotation(Quaternion rotation) {
            Transform.rotation = rotation;
        }

        public Quaternion GetRotation() {
            return Transform.rotation;
        }

        public void SetVelocity(Vector3 velocity) {
            Velocity = velocity;
        }

        public Vector3 GetVelocity() {
            return Velocity;
        }

        public bool HasAlignment() {
            // 检查是否有有效的对齐轴
            return Alignment != Vector3.zero;
        }

        public Vector3 GetAlignment() {
            return Alignment;
        }

        public void ComputeAlignment() {
            // 计算对齐轴：只有当骨骼有且仅有一个子骨骼时才计算
            if(Childs.Length != 1) {
                Alignment = Vector3.zero;
            } else {
                // 计算从当前骨骼指向子骨骼的本地方向向量
                Alignment = (GetChild(0).GetPosition() - GetPosition()).GetRelativeDirectionTo(GetTransformation());
            }
        }

        public void RestoreAlignment() {
            // 恢复骨骼对齐（用于动画重定向）
            if(!Actor.AllowRealignment || !HasAlignment()) {
                return;
            }
            Vector3 position = GetPosition();
            Quaternion rotation = GetRotation();
            Vector3 childPosition = GetChild(0).GetPosition();
            Quaternion childRotation = GetChild(0).GetRotation();
            Vector3 target = (childPosition-position); // 当前指向子骨骼的向量
            Vector3 aligned = rotation * Alignment; // 期望的对齐方向
            
            // 计算并应用旋转以对齐到目标方向
            SetRotation(Quaternion.FromToRotation(aligned, target) * rotation);
            // 调整子骨骼位置以保持正确的骨骼长度
            GetChild(0).SetPosition(position + Alignment.magnitude * target.normalized);
            GetChild(0).SetRotation(childRotation);
        }
    }

    #if UNITY_EDITOR
    [CustomEditor(typeof(Actor))]
    public class Actor_Editor : Editor {
        // Unity编辑器扩展类

        public Actor Target;

        private string AName = string.Empty; // 重命名功能的源字符串
        private string BName = string.Empty; // 重命名功能的目标字符串

        void Awake() {
            Target = (Actor)target;
        }

        public override void OnInspectorGUI() {
            // 自定义检视器界面
            Undo.RecordObject(Target, Target.name); // 记录撤销操作

            // 绘制设置选项
            Target.AllowRealignment = EditorGUILayout.Toggle("Allow Realignment", Target.AllowRealignment);
            Target.DrawRoot = EditorGUILayout.Toggle("Draw Root", Target.DrawRoot);
            Target.DrawSkeleton = EditorGUILayout.Toggle("Draw Skeleton", Target.DrawSkeleton);
            Target.DrawTransforms = EditorGUILayout.Toggle("Draw Transforms", Target.DrawTransforms);
            Target.DrawVelocities = EditorGUILayout.Toggle("Draw Velocities", Target.DrawVelocities);
            Target.DrawAlignment = EditorGUILayout.Toggle("Draw Alignment", Target.DrawAlignment);

            // 骨骼系统管理界面
            Utility.SetGUIColor(Color.grey);
            using(new EditorGUILayout.VerticalScope ("Box")) {
                Utility.ResetGUIColor();
                if(Utility.GUIButton("Skeleton", UltiDraw.DarkGrey, UltiDraw.White)) {
                    Inspect = !Inspect; // 切换检视状态
                }
                if(Inspect) {
                    // 参考Actor复制功能
                    Actor reference = (Actor)EditorGUILayout.ObjectField("Reference", null, typeof(Actor), true);
                    if(reference != null) {
                        Target.CopySetup(reference);
                    }

                    // 重命名功能界面
                    EditorGUILayout.BeginHorizontal();
                    AName = EditorGUILayout.TextField(AName, GUILayout.Width(175f));
                    EditorGUILayout.LabelField(">", GUILayout.Width(10f));
                    BName = EditorGUILayout.TextField(BName, GUILayout.Width(175f));
                    if(Utility.GUIButton("Rename", UltiDraw.DarkGrey, UltiDraw.White)) {
                        Target.RenameBones(AName, BName);
                    }
                    if(Utility.GUIButton("Switch", UltiDraw.DarkGrey, UltiDraw.White)) {
                        Target.SwitchNames(AName, BName);
                    }
                    EditorGUILayout.EndHorizontal();

                    // 骨骼数量和清空功能
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Bones: " + Target.Bones.Length);
                    if(Utility.GUIButton("Clear", UltiDraw.DarkGrey, UltiDraw.White)) {
                        Target.Create(new Transform[0]);
                    }
                    EditorGUILayout.EndHorizontal();

                    // 计算对齐轴功能
                    if(Utility.GUIButton("Compute Alignment", UltiDraw.DarkGrey, UltiDraw.White)) {
                        foreach(Bone bone in Target.Bones) {
                            bone.ComputeAlignment();
                        }
                    }

                    // 绘制设置
                    Target.BoneSize = EditorGUILayout.FloatField("Bone Size", Target.BoneSize);
                    Target.BoneColor = EditorGUILayout.ColorField("Bone Color", Target.BoneColor);
                    Target.JointColor = EditorGUILayout.ColorField("Joint Color", Target.JointColor);

                    // 骨骼层次结构检视
                    InspectSkeleton(Target.GetRoot(), 0);
                }
            }

            if(GUI.changed) {
                EditorUtility.SetDirty(Target); // 标记为已修改
            }
        }

        private void InspectSkeleton(Transform transform, int indent) {
            // 递归绘制骨骼层次结构检视界面
            float indentSpace = 10f;
            Bone bone = Target.FindBone(transform);
            // 根据是否为骨骼设置不同颜色
            Utility.SetGUIColor(bone == null ? UltiDraw.LightGrey : UltiDraw.Mustard);
            using(new EditorGUILayout.HorizontalScope ("Box")) {
                Utility.ResetGUIColor();
                EditorGUILayout.BeginHorizontal();
                
                // 绘制缩进
                for(int i=0; i<indent; i++) {
                    EditorGUILayout.LabelField("|", GUILayout.Width(indentSpace));
                }
                EditorGUILayout.LabelField("-", GUILayout.Width(indentSpace));
                EditorGUILayout.LabelField(transform.name, GUILayout.Width(400f - indent*indentSpace));
                GUILayout.FlexibleSpace();
                
                if(bone != null) {
                    // 显示骨骼信息
                    EditorGUILayout.LabelField("Index: " + bone.GetIndex().ToString(), GUILayout.Width(60f));
                    EditorGUILayout.LabelField("Length: " + bone.GetLength().ToString("F3"), GUILayout.Width(90f));
                    if(bone.HasAlignment()) {
                        EditorGUILayout.LabelField(bone.GetAlignment().ToString(), GUILayout.Width(100f));
                    }
                }
                
                // 添加/移除骨骼按钮
                if(Utility.GUIButton("Bone", bone == null ? UltiDraw.White : UltiDraw.DarkGrey, bone == null ? UltiDraw.DarkGrey : UltiDraw.White)) {
                    Transform[] bones = new Transform[Target.Bones.Length];
                    for(int i=0; i<bones.Length; i++) {
                        bones[i] = Target.Bones[i].GetTransform();
                    }
                    if(bone == null) {
                        // 添加骨骼
                        ArrayExtensions.Append(ref bones, transform);
                        Target.Create(bones);
                    } else {
                        // 移除骨骼
                        ArrayExtensions.Remove(ref bones, transform);
                        Target.Create(bones);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            
            // 递归处理子节点
            for(int i=0; i<transform.childCount; i++) {
                InspectSkeleton(transform.GetChild(i), indent+1);
            }
        }
    }
    #endif
}
