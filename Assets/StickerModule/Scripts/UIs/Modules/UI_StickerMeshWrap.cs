using System.Collections;
using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 격자 메시(StickerMeshBaker 로 구운) 를 곡면에 감싸는 wrap 연출.
    //  - 정점(가상 본)을 원기둥 곡면에 매핑 → 가장자리가 말려 들어가며 붙는 느낌.
    //  - 직교 카메라에서도 보이도록 XY 단축(foreshortening) 으로 표현하고,
    //    깊이(Z) 굽힘도 함께 주어 Lit/원근 카메라에서도 자연스럽게 동작.
    //  - 공유 메시 에셋을 건드리지 않도록 런타임 인스턴스를 복제해서 변형.
    //
    // 붙는 순간 PlayWrap() 을 호출하면 평평 → 곡면 conform 으로 안정화된다.
    // --------------------------------------------------
    [RequireComponent(typeof(MeshFilter))]
    public class UI_StickerMeshWrap : MonoBehaviour
    {
        // --------------------------------------------------
        // Enums
        // --------------------------------------------------
        public enum EWrapAxis
        {
            Horizontal, // 좌우로 감김(세로 능선)
            Vertical,   // 상하로 감김(가로 능선)
        }

        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [Header("Wrap")]
        [SerializeField] private EWrapAxis axis = EWrapAxis.Vertical;
        [SerializeField] private float wrapAngle = 1.0f;   // 최종 감김 정도(라디안, 클수록 강함)
        [SerializeField] private float depthScale = 0.15f; // 깊이(Z) 굽힘량 (Lit/원근용)
        [SerializeField] private float duration = 0.4f;

        [Header("Options")]
        [SerializeField] private bool autoPlayOnEnable = false; // 켜지면 자동 재생(테스트용)
        [SerializeField] private bool overshoot = true;         // 살짝 지나쳤다 안정(탄력)

        // --------------------------------------------------
        // Fields
        // --------------------------------------------------
        private MeshFilter mf = null;
        private Mesh mesh = null;
        private Vector3[] baseVerts = null;
        private Vector3[] work = null;
        private Vector3 center = Vector3.zero;
        private Vector2 size = Vector2.one;
        private Coroutine playing = null;

        // --------------------------------------------------
        // Unity
        // --------------------------------------------------
        private void Awake()
        {
            mf = GetComponent<MeshFilter>();
            if (mf.sharedMesh == null) return;

            // 공유 에셋 보호를 위해 인스턴스 복제.
            mesh = Instantiate(mf.sharedMesh);
            mesh.name = mf.sharedMesh.name + " (inst)";
            mf.mesh = mesh;

            baseVerts = mesh.vertices;
            work = new Vector3[baseVerts.Length];

            var b = mesh.bounds;
            center = b.center;
            size = new Vector2(Mathf.Max(1e-4f, b.size.x), Mathf.Max(1e-4f, b.size.y));
        }

        private void OnEnable()
        {
            if (autoPlayOnEnable) PlayWrap();
        }

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------
        public void PlayWrap()
        {
            if (mesh == null) return;
            if (playing != null) StopCoroutine(playing);
            playing = StartCoroutine(CoPlay());
        }

        // 즉시 최종 곡면 상태로.
        public void SetWrapped()
        {
            ApplyWrap(1f);
        }

        // 평평한 원본 상태로.
        public void ResetFlat()
        {
            if (mesh == null) return;
            mesh.vertices = baseVerts;
            mesh.RecalculateBounds();
        }

        // --------------------------------------------------
        // Internal
        // --------------------------------------------------
        private IEnumerator CoPlay()
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.0001f, duration);
                float p = Mathf.Clamp01(t);
                ApplyWrap(overshoot ? EaseOutBack(p) : EaseOutCubic(p));
                yield return null;
            }
            ApplyWrap(1f);
            playing = null;
        }

        // amount: 0(평평) ~ 1(최종 곡면). overshoot 시 1을 살짝 넘을 수 있음.
        private void ApplyWrap(float amount)
        {
            float angle = wrapAngle * amount;

            if (angle < 1e-3f)
            {
                mesh.vertices = baseVerts;
                mesh.RecalculateBounds();
                return;
            }

            float half = angle * 0.5f;
            bool horizontal = axis == EWrapAxis.Horizontal;
            float axisSize = horizontal ? size.x : size.y;
            float axisCenter = horizontal ? center.x : center.y;

            float radius = axisSize / angle;           // 호 길이 = 원래 크기
            float cosHalf = Mathf.Cos(half);
            float depth = depthScale * amount;

            for (int i = 0; i < baseVerts.Length; i++)
            {
                Vector3 v = baseVerts[i];

                // 감김 축 방향 정규화 좌표 n ∈ [-0.5, 0.5]
                float coord = horizontal ? v.x : v.y;
                float n = (coord - axisCenter) / axisSize;

                float theta = n * angle;
                float along = radius * Mathf.Sin(theta);            // 단축된 위치(가장자리가 말려 들어감)
                float z = -radius * (Mathf.Cos(theta) - cosHalf) * depth; // 중앙이 앞으로 볼록

                if (horizontal) v.x = axisCenter + along;
                else v.y = axisCenter + along;
                v.z = z;

                work[i] = v;
            }

            mesh.vertices = work;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // --------------------------------------------------
        // Easing
        // --------------------------------------------------
        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
