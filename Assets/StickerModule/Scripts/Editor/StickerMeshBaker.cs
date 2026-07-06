using System.IO;
using UnityEditor;
using UnityEngine;

namespace Gunter.Sticker.EditorTools
{
    // --------------------------------------------------
    // SpriteRenderer 를 "격자 메시(grid mesh)" 로 굽는 에디터 툴.
    //  - 스프라이트의 월드 크기(sprite.bounds)와 UV 를 그대로 유지 → 겉보기 동일.
    //  - 격자로 세분화해 두어야 런타임에서 정점(가상 본)을 곡면으로 감싸는 wrap 연출이 가능.
    //  - 생성한 Mesh/Material 은 에셋으로 저장(=에디터에서 미리 굽기).
    //  - 대상 오브젝트에 MeshFilter + MeshRenderer 를 세팅하고 SpriteRenderer 는 비활성화.
    //
    // 사용법: Hierarchy 에서 SpriteRenderer 오브젝트 선택 후
    //         [Tools > Gunter Sticker > Bake Mesh From Sprite]
    // --------------------------------------------------
    public static class StickerMeshBaker
    {
        // 격자 분할 수(한 축당 셀 개수). 런타임 wrap 컴포넌트와 값이 일치해야 한다.
        public const int SUBDIVISIONS = 12;

        private const string MESH_DIR = "Assets/StickerModule/Meshes/Baked";
        private const string MAT_DIR = "Assets/StickerModule/Materials/Baked";

        // SpriteRenderer 와 MeshRenderer 는 같은 오브젝트에 공존할 수 없으므로
        // 메시는 이 이름의 자식 오브젝트에 둔다.
        private const string MESH_CHILD = "WrapMesh";

        [MenuItem("Tools/Gunter Sticker/Bake Mesh From Sprite")]
        public static void BakeSelection()
        {
            var objs = Selection.gameObjects;
            if (objs == null || objs.Length == 0)
            {
                Debug.LogWarning("[StickerMeshBaker] SpriteRenderer 를 가진 오브젝트를 먼저 선택하세요.");
                return;
            }

            EnsureFolder(MESH_DIR);
            EnsureFolder(MAT_DIR);

            int baked = 0;
            foreach (var go in objs)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;

                BakeOne(go, sr);
                baked++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[StickerMeshBaker] {baked}개 오브젝트를 격자 메시로 구웠습니다. (분할 {SUBDIVISIONS})");
        }

        // --------------------------------------------------
        // Bake One
        // --------------------------------------------------
        // 외부(샘플 빌더 등)에서 단일 오브젝트를 정점-wrap 메시로 굽는 진입점.
        public static void BakeSprite(GameObject go)
        {
            if (go == null) return;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return;

            EnsureFolder(MESH_DIR);
            EnsureFolder(MAT_DIR);
            BakeOne(go, sr);
        }

        private static void BakeOne(GameObject go, SpriteRenderer sr)
        {
            var sprite = sr.sprite;

            var mesh = BuildGridMesh(sprite, SUBDIVISIONS);
            mesh.name = sprite.name + "_grid";

            // 스프라이트별 고정 경로 → 재베이크 시 기존 에셋을 덮어써서 파일이 쌓이지 않게.
            mesh = SaveOrUpdateMesh(mesh, $"{MESH_DIR}/{sprite.name}_grid.asset");

            var mat = GetOrCreateMaterial(sprite.texture);

            // 메시는 별도 자식("WrapMesh")에 둔다(SpriteRenderer 와 공존 불가).
            Transform child = go.transform.Find(MESH_CHILD);
            if (child == null)
            {
                var childGo = new GameObject(MESH_CHILD);
                child = childGo.transform;
                child.SetParent(go.transform, false);
            }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            var childObj = child.gameObject;

            // SkinnedMeshRenderer 는 MeshRenderer 와 공존 불가 → 이전 스킨 굽기 흔적 제거.
            RemoveIfExists<UI_StickerBoneWrap>(childObj);
            RemoveIfExists<SkinnedMeshRenderer>(childObj);
            var oldBones = child.Find("Bones");
            if (oldBones != null) Object.DestroyImmediate(oldBones.gameObject);

            var mf = childObj.GetComponent<MeshFilter>();
            if (mf == null) mf = childObj.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = childObj.GetComponent<MeshRenderer>();
            if (mr == null) mr = childObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingLayerID = sr.sortingLayerID; // 정렬 유지
            mr.sortingOrder = sr.sortingOrder;
            mr.enabled = false; // 스크롤/드래그 중엔 스프라이트, 배치 순간에 켬

            // wrap 연출 컴포넌트 부착(배치 시점에 UI_DraggableSticker 가 재생).
            if (childObj.GetComponent<UI_StickerMeshWrap>() == null)
                childObj.AddComponent<UI_StickerMeshWrap>();

            // 스프라이트 렌더러는 부모에 그대로 유지(스크롤/드래그용).

            EditorUtility.SetDirty(go);
        }

        // ==================================================
        // Skinned Bake (본 라인 → 실제 본 + 스킨 메시)
        // 사용법: SpriteRenderer + UI_StickerBoneLine(선 2점 이상) 오브젝트 선택 후
        //         [Tools > Gunter Sticker > Bake Skinned Mesh (From Bone Line)]
        // ==================================================
        [MenuItem("Tools/Gunter Sticker/Bake Skinned Mesh (From Bone Line)")]
        public static void BakeSkinnedSelection()
        {
            var objs = Selection.gameObjects;
            if (objs == null || objs.Length == 0)
            {
                Debug.LogWarning("[StickerMeshBaker] SpriteRenderer + UI_StickerBoneLine 오브젝트를 선택하세요.");
                return;
            }

            EnsureFolder(MESH_DIR);
            EnsureFolder(MAT_DIR);

            int baked = 0;
            foreach (var go in objs)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;

                var line = go.GetComponentInChildren<UI_StickerBoneLine>(true);
                if (line == null || line.Points.Count < 2)
                {
                    Debug.LogWarning($"[StickerMeshBaker] '{go.name}' 에 선(2점 이상)이 있는 UI_StickerBoneLine 이 필요합니다.");
                    continue;
                }

                BakeSkinnedOne(go, sr, line);
                baked++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[StickerMeshBaker] {baked}개를 스킨 메시로 구웠습니다.");
        }

        // 외부(샘플 빌더 등)에서 단일 오브젝트를 본 스킨 메시로 굽는 진입점.
        public static void BakeSkinned(GameObject go)
        {
            if (go == null) return;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return;

            var line = go.GetComponentInChildren<UI_StickerBoneLine>(true);
            if (line == null || line.Points.Count < 2) return;

            EnsureFolder(MESH_DIR);
            EnsureFolder(MAT_DIR);
            BakeSkinnedOne(go, sr, line);
        }

        private static void BakeSkinnedOne(GameObject go, SpriteRenderer sr, UI_StickerBoneLine line)
        {
            var sprite = sr.sprite;

            var mesh = BuildGridMesh(sprite, SUBDIVISIONS);
            mesh.name = sprite.name + "_skinned";

            var mat = GetOrCreateMaterial(sprite.texture);

            // 메시/본을 담을 자식 준비.
            Transform child = go.transform.Find(MESH_CHILD);
            if (child == null)
            {
                var childGo = new GameObject(MESH_CHILD);
                child = childGo.transform;
                child.SetParent(go.transform, false);
            }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            var childObj = child.gameObject;

            // MeshRenderer/MeshFilter/정점 wrap 은 SkinnedMeshRenderer 와 공존 불가 → 제거.
            RemoveIfExists<UI_StickerMeshWrap>(childObj);
            RemoveIfExists<MeshRenderer>(childObj);
            RemoveIfExists<MeshFilter>(childObj);

            // 선 위 본 위치(월드).
            Vector3[] onLine = line.SampleBonePositions();  // line 로컬
            int n = onLine.Length;
            var boneWorld = new Vector3[n];
            for (int i = 0; i < n; i++)
                boneWorld[i] = line.transform.TransformPoint(onLine[i]);

            // 본 체인 생성(기존 것 제거 후).
            Transform oldBones = child.Find("Bones");
            if (oldBones != null) Object.DestroyImmediate(oldBones.gameObject);
            var bonesRoot = new GameObject("Bones").transform;
            bonesRoot.SetParent(child, false);

            var bones = new Transform[n];
            Transform prev = bonesRoot;
            for (int i = 0; i < n; i++)
            {
                var boneGo = new GameObject("Bone_" + i);
                boneGo.transform.SetParent(prev, false);
                boneGo.transform.position = boneWorld[i];   // 월드 위치 지정
                boneGo.transform.rotation = child.rotation; // 메시와 축 정렬(로컬 회전 ≈ identity)
                bones[i] = boneGo.transform;
                prev = boneGo.transform;
            }

            // 스킨 웨이트(정점을 본 폴리라인에 투영해 인접 두 본에 분배).
            var boneLocal = new Vector3[n];
            for (int i = 0; i < n; i++)
                boneLocal[i] = child.InverseTransformPoint(boneWorld[i]);

            var verts = mesh.vertices;
            var weights = new BoneWeight[verts.Length];
            for (int v = 0; v < verts.Length; v++)
                weights[v] = ComputeWeight(verts[v], boneLocal);
            mesh.boneWeights = weights;

            // 바인드 포즈.
            var bindposes = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
                bindposes[i] = bones[i].worldToLocalMatrix * child.localToWorldMatrix;
            mesh.bindposes = bindposes;

            // 오브젝트별 고정 경로 → 재베이크 시 덮어쓰기.
            mesh = SaveOrUpdateMesh(mesh, $"{MESH_DIR}/{go.name}_{sprite.name}_skinned.asset");

            // SkinnedMeshRenderer 세팅.
            var smr = childObj.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) smr = childObj.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = mat;
            smr.sortingLayerID = sr.sortingLayerID;
            smr.sortingOrder = sr.sortingOrder;
            smr.updateWhenOffscreen = true;
            smr.enabled = false; // 배치 순간에 켬

            if (childObj.GetComponent<UI_StickerBoneWrap>() == null)
                childObj.AddComponent<UI_StickerBoneWrap>();

            EditorUtility.SetDirty(go);
        }

        // 정점을 본 폴리라인에 투영해 가장 가까운 세그먼트의 두 본에 가중치 분배.
        private static BoneWeight ComputeWeight(Vector3 v, Vector3[] boneLocal)
        {
            int bestSeg = 0;
            float bestT = 0f;
            float bestDist = float.MaxValue;

            for (int i = 0; i < boneLocal.Length - 1; i++)
            {
                Vector3 a = boneLocal[i];
                Vector3 ab = boneLocal[i + 1] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(v - a, ab) / len2) : 0f;
                float d = (v - (a + ab * t)).sqrMagnitude;
                if (d < bestDist) { bestDist = d; bestSeg = i; bestT = t; }
            }

            var w = new BoneWeight
            {
                boneIndex0 = bestSeg,
                weight0 = 1f - bestT,
                boneIndex1 = Mathf.Min(bestSeg + 1, boneLocal.Length - 1),
                weight1 = bestT,
            };

            float sum = w.weight0 + w.weight1;
            if (sum < 1e-5f) { w.weight0 = 1f; w.weight1 = 0f; }
            else { w.weight0 /= sum; w.weight1 /= sum; }
            return w;
        }

        private static void RemoveIfExists<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null) Object.DestroyImmediate(c);
        }

        // 같은 경로에 에셋이 있으면 내용만 덮어써(참조 유지), 없으면 새로 생성.
        private static Mesh SaveOrUpdateMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing); // 기존 에셋 참조 유지한 채 데이터 교체
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // --------------------------------------------------
        // Grid Mesh - 스프라이트 크기/UV 를 그대로 격자로 재구성
        // --------------------------------------------------
        private static Mesh BuildGridMesh(Sprite sprite, int sub)
        {
            int cols = Mathf.Max(1, sub);
            int rows = Mathf.Max(1, sub);
            int vx = cols + 1;
            int vy = rows + 1;

            // 위치: SpriteRenderer 가 그리는 실제 영역(sprite.bounds) 그대로.
            Bounds b = sprite.bounds;
            Vector3 min = b.min;
            Vector3 size = b.size;

            // UV: 텍스처 상의 스프라이트 rect(픽셀) → 0~1 정규화.
            float texW = sprite.texture != null ? sprite.texture.width : 1f;
            float texH = sprite.texture != null ? sprite.texture.height : 1f;
            Rect r = sprite.rect;
            Vector2 uvMin = new Vector2(r.xMin / texW, r.yMin / texH);
            Vector2 uvMax = new Vector2(r.xMax / texW, r.yMax / texH);

            var vertices = new Vector3[vx * vy];
            var uvs = new Vector2[vx * vy];

            for (int y = 0; y < vy; y++)
            {
                float ty = (float)y / rows;
                for (int x = 0; x < vx; x++)
                {
                    float tx = (float)x / cols;
                    int i = y * vx + x;

                    vertices[i] = new Vector3(min.x + tx * size.x, min.y + ty * size.y, 0f);
                    uvs[i] = new Vector2(
                        Mathf.Lerp(uvMin.x, uvMax.x, tx),
                        Mathf.Lerp(uvMin.y, uvMax.y, ty));
                }
            }

            var triangles = new int[cols * rows * 6];
            int t = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    int i0 = y * vx + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vx;
                    int i3 = i2 + 1;

                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // --------------------------------------------------
        // Material - 텍스처별로 1개 재사용(Sprites/Default 로 알파 지원)
        // --------------------------------------------------
        private static Material GetOrCreateMaterial(Texture tex)
        {
            string matName = (tex != null ? tex.name : "sticker") + "_mat";
            string matPath = $"{MAT_DIR}/{matName}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null)
            {
                existing.mainTexture = tex;
                return existing;
            }

            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");

            var mat = new Material(shader) { name = matName, mainTexture = tex };
            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
        }

        // --------------------------------------------------
        // Helpers
        // --------------------------------------------------
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
