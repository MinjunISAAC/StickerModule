using System;
using System.Collections;
using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 실제 본 체인(SkinnedMeshRenderer)을 휘어 스티커를 감싸는 wrap 연출.
    //  - StickerMeshBaker 의 "Bake Skinned" 로 만든 본 체인을 사용.
    //  - 붙는 순간 각 본을 굽혔다가(curled) 서서히 펴며(flatten) 안정화.
    //  - overshoot 로 살짝 튕겼다 붙는 "쫘악" 느낌.
    // --------------------------------------------------
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class UI_StickerBoneWrap : MonoBehaviour, IStickerWrap
    {
        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [Header("Bone Wrap")]
        [SerializeField] private Vector3 bendAxis = Vector3.forward; // 각 본이 회전하는 로컬 축
        [SerializeField] private float bendAnglePerBone = 12f;       // 본당 굽힘 각도(도)
        [SerializeField] private float duration = 0.4f;

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
            smr.updateWhenOffscreen = true; // 본 굽힘으로 bounds 가 변해도 잘 보이게

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
            ApplyBend(0f); // 평평(rest)
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
                // 굽힘량 amount: 1(curled) → 0(flat). overshoot 시 살짝 음수로 반대 튕김.
                float eased = overshoot ? EaseOutBack(p) : EaseOutCubic(p);
                ApplyBend(1f - eased);
                yield return null;
            }
            ApplyBend(0f);
            playing = null;
            onComplete?.Invoke();
        }

        // amount: 0=평평, 1=최대 굽힘. 체인이라 본마다 같은 각을 주면 호(arc)가 된다.
        private void ApplyBend(float amount)
        {
            if (bones == null || restRot == null) return;

            float ang = bendAnglePerBone * amount;
            var q = Quaternion.AngleAxis(ang, bendAxis);

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                // 루트(0)는 고정, 이후 본들만 부모 대비 굽힘 → 누적되어 감김.
                bones[i].localRotation = (i == 0) ? restRot[i] : restRot[i] * q;
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
