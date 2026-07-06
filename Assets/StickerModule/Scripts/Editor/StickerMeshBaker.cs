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
        private static void BakeOne(GameObject go, SpriteRenderer sr)
        {
            var sprite = sr.sprite;

            var mesh = BuildGridMesh(sprite, SUBDIVISIONS);
            mesh.name = sprite.name + "_grid";

            string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{MESH_DIR}/{mesh.name}.asset");
            AssetDatabase.CreateAsset(mesh, meshPath);

            var mat = GetOrCreateMaterial(sprite.texture);

            // MeshFilter / MeshRenderer 세팅.
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingLayerID = sr.sortingLayerID; // 정렬 유지
            mr.sortingOrder = sr.sortingOrder;
            mr.enabled = false; // 스크롤/드래그 중엔 스프라이트, 배치 순간에 켬

            // wrap 연출 컴포넌트 부착(배치 시점에 UI_DraggableSticker 가 재생).
            if (go.GetComponent<UI_StickerMeshWrap>() == null)
                go.AddComponent<UI_StickerMeshWrap>();

            // 스프라이트 렌더러는 그대로 유지(스크롤/드래그용).

            EditorUtility.SetDirty(go);
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
