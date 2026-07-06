using System;
using System.Collections;
using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 실제 본 체인(SkinnedMeshRenderer)을 선 방향(시작→끝)으로 말았다가 펴며 붙이는 연출.
    //  - 처음엔 말려있고, 시작쪽부터 끝쪽으로 "롤"이 풀리듯 펴지며 표면에 붙는다.
    //  - reverse 로 끝→시작 방향으로 말 수도 있음.
    //  - OnDrawGizmos 로 베이크된 본 체인/방향(초록=시작, 빨강=끝)을 씬에 표시.
    // --------------------------------------------------
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class UI_StickerBoneWrap : MonoBehaviour, IStickerWrap
    {
        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [Header("Roll Wrap")]
        [SerializeField] private Vector3 bendAxis = Vector3.forward; // 감기는 회전 축(로컬)
        [SerializeField] private float bendAnglePerBone = 20f;       // 본당 굽힘 각도(도, 클수록 강하게 말림)
        [SerializeField] private float rollBand = 0.4f;              // 말림 전이 폭(0~1, 클수록 완만하게 풀림)
        [SerializeField] private bool reverse = false;               // 끝→시작 방향으로 말기
        [SerializeField] private float duration = 0.5f;

        [Header("Options")]
        [SerializeField] private bool autoPlayOnEnable = false;
        [SerializeField] private bool overshoot = true;

        // --------------------------------------------------
        // Fields
        // --------------------------------------------------
        private SkinnedMeshRenderer smr = null;
        private Transform[] bones = null;
        private Quaternion[] restRot = null;
        private Coroutine playing = null;
        private Action onComplete = null;

        // --------------------------------------------------
        // Unity
        // --------------------------------------------------
        private void Awake()
        {
            smr = GetComponent<SkinnedMeshRenderer>();
            smr.updateWhenOffscreen = true; // 말림으로 bounds 가 변해도 잘 보이게

            bones = smr.bones;
            if (bones != null)
            {
                restRot = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) restRot[i] = bones[i].localRotation;
            }
        }

        private void OnEnable()
        {
            if (autoPlayOnEnable) PlayWrap(null);
        }

        // --------------------------------------------------
        // IStickerWrap
        // --------------------------------------------------
        public void PlayWrap(Action onComplete)
        {
            this.onComplete = onComplete;

            if (smr == null || bones == null) { onComplete?.Invoke(); return; }

            smr.enabled = true;
            if (playing != null) StopCoroutine(playing);
            playing = StartCoroutine(CoPlay());
        }

        public void Hide()
        {
            ApplyRoll(1f); // 완전히 펴진(평평) 상태
            if (smr != null) smr.enabled = false;
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
                float eased = overshoot ? EaseOutBack(p) : EaseOutCubic(p);
                ApplyRoll(eased); // 0=말림 → 1=평평, 시작쪽부터 풀림
                yield return null;
            }
            ApplyRoll(1f);
            playing = null;
            onComplete?.Invoke();
        }

        // p: 0 = 완전히 말림, 1 = 완전히 펴짐(평평).
        //   말림 경계(front)가 시작→끝으로 이동하며, 경계 앞쪽(끝 방향)은 아직 말려 있다.
        private void ApplyRoll(float p)
        {
            if (bones == null || restRot == null) return;

            int n = bones.Length;
            float front = Mathf.Lerp(-rollBand, 1f, p); // -band(전부 말림) → 1(전부 펴짐)
            float band = Mathf.Max(1e-4f, rollBand);
            Vector3 axis = bendAxis.sqrMagnitude < 1e-6f ? Vector3.forward : bendAxis.normalized;

            for (int i = 0; i < n; i++)
            {
                if (bones[i] == null) continue;

                float bt = n > 1 ? (float)i / (n - 1) : 0f; // 0=시작(root) ~ 1=끝
                if (reverse) bt = 1f - bt;

                float amt = Mathf.Clamp01((bt - front) / band); // 0=펴짐, 1=말림
                bones[i].localRotation = restRot[i] * Quaternion.AngleAxis(bendAnglePerBone * amt, axis);
            }
        }

        // --------------------------------------------------
        // Gizmos - 베이크된 본 체인/방향 표시(초록=시작, 빨강=끝)
        // --------------------------------------------------
        private void OnDrawGizmos()
        {
            var r = GetComponent<SkinnedMeshRenderer>();
            if (r == null || r.bones == null || r.bones.Length == 0) return;

            var b = r.bones;
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] == null) continue;

                float u = b.Length > 1 ? (float)i / (b.Length - 1) : 0f;
                Gizmos.color = Color.Lerp(Color.green, Color.red, u); // 시작→끝
                Gizmos.DrawSphere(b[i].position, 0.06f);

                if (i < b.Length - 1 && b[i + 1] != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(b[i].position, b[i + 1].position);
                }
            }
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
