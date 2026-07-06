using System.Collections.Generic;
using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 에디터에서 "선"을 긋고, 그 선 위에 본을 몇 개 놓을지 정하는 authoring 데이터.
    //  - 선(폴리라인)을 따라 boneCount 개의 본이 균등 분포된다.
    //  - 이 정보를 StickerMeshBaker 가 읽어 실제 Bone + SkinnedMesh 로 굽는다.
    //  - 점은 로컬 공간(오브젝트 XY 평면) 기준으로 저장.
    // --------------------------------------------------
    public class UI_StickerBoneLine : MonoBehaviour
    {
        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [SerializeField] private List<Vector3> points = new List<Vector3>(); // 로컬 공간 선
        [Min(2)] [SerializeField] private int boneCount = 5;                  // 선 위 본 개수

        // --------------------------------------------------
        // Properties
        // --------------------------------------------------
        public int BoneCount => Mathf.Max(2, boneCount);
        public IReadOnlyList<Vector3> Points => points;
        public List<Vector3> EditablePoints => points; // 에디터 핸들 편집용

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------
        public void AddPoint(Vector3 local) { points.Add(local); }
        public void RemoveLast() { if (points.Count > 0) points.RemoveAt(points.Count - 1); }
        public void ClearPoints() { points.Clear(); }

        // 선 위를 균등 샘플한 본 위치(로컬)를 반환.
        public Vector3[] SampleBonePositions()
        {
            int n = BoneCount;
            var result = new Vector3[n];

            if (points.Count == 0) return result;
            if (points.Count == 1)
            {
                for (int i = 0; i < n; i++) result[i] = points[0];
                return result;
            }

            float total = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                total += Vector3.Distance(points[i], points[i + 1]);

            if (total < 1e-5f)
            {
                for (int i = 0; i < n; i++) result[i] = points[0];
                return result;
            }

            for (int i = 0; i < n; i++)
            {
                float d = total * i / (n - 1);
                result[i] = SampleAtDistance(d);
            }
            return result;
        }

        // --------------------------------------------------
        // Internal
        // --------------------------------------------------
        private Vector3 SampleAtDistance(float d)
        {
            float acc = 0f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                float seg = Vector3.Distance(points[i], points[i + 1]);
                if (acc + seg >= d)
                {
                    float t = seg > 1e-5f ? (d - acc) / seg : 0f;
                    return Vector3.Lerp(points[i], points[i + 1], t);
                }
                acc += seg;
            }
            return points[points.Count - 1];
        }
    }
}
