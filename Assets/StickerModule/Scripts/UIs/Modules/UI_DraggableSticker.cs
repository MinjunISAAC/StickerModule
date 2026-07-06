using System.Collections;
using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 스크롤 뷰에서 위로 뽑혀 나온 스티커.
    //  - 뽑히면 스크롤에서 분리되어 포인터를 따라다닌다.
    //  - 손을 놓으면 가장 가까운 슬롯(UI_StickerSlot)을 찾아,
    //    흡입 거리 안이면 "서서히 빨려들어가는" 연출로 붙는다.
    //  - 유효한 자리가 없으면 스크롤 목록으로 복귀한다.
    // --------------------------------------------------
    [RequireComponent(typeof(SpriteRenderer))]
    public class UI_DraggableSticker : MonoBehaviour
    {
        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [Header("Placement FX")]
        [SerializeField] private float suckDuration = 0.25f; // 빨려들어가는 시간
        [SerializeField] private float popScale = 1.15f;     // 흡입 중 살짝 커졌다 원복
        [SerializeField] private int draggingOrder = 100;    // 드래그 중 정렬 순서(맨 위)

        // --------------------------------------------------
        // Fields
        // --------------------------------------------------
        private SpriteRenderer sr = null;
        private UI_SpriteScrollView owner = null;
        private Camera cam = null;

        private bool isPulled = false;   // 뽑혀서 손가락 따라다니는 중
        private bool isPlacing = false;  // 흡입 연출 중
        private Vector3 grabOffset = Vector3.zero;
        private int baseOrder = 0;
        private Vector3 baseScale = Vector3.one;

        // --------------------------------------------------
        // Unity
        // --------------------------------------------------
        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            baseOrder = sr.sortingOrder;
            baseScale = transform.localScale;
        }

        private void Update()
        {
            if (!isPulled) return;

            if (SPointer.Held)
            {
                Vector3 wp = cam.ScreenToWorldPoint(SPointer.Position);
                wp.z = transform.position.z;
                transform.position = wp + grabOffset;
            }
            else
            {
                EndPull();
            }
        }

        // --------------------------------------------------
        // Public API - 스크롤 뷰가 호출
        // --------------------------------------------------
        public void BeginPull(UI_SpriteScrollView scrollView, Camera camera, Vector2 pointerWorld)
        {
            if (isPulled || isPlacing) return;

            owner = scrollView;
            cam = camera != null ? camera : Camera.main;

            owner.DetachItem(transform);

            sr.maskInteraction = SpriteMaskInteraction.None; // 뷰포트 밖에서도 안 잘리게
            sr.sortingOrder = draggingOrder;

            grabOffset = transform.position - (Vector3)pointerWorld;
            grabOffset.z = 0f;

            isPulled = true;
        }

        // --------------------------------------------------
        // Internal
        // --------------------------------------------------
        private void EndPull()
        {
            isPulled = false;

            float dist;
            var slot = UI_StickerSlot.FindNearest(transform.position, out dist);

            if (slot != null && !slot.IsOccupied && dist <= slot.SnapDistance)
            {
                StartCoroutine(CoSuckIn(slot));
            }
            else
            {
                // 유효한 자리 없음 → 스크롤로 복귀
                sr.sortingOrder = baseOrder;
                if (owner != null) owner.ReturnItem(transform);
            }
        }

        private IEnumerator CoSuckIn(UI_StickerSlot slot)
        {
            isPlacing = true;
            slot.Occupy(this);

            Vector3 from = transform.position;
            Vector3 to = slot.transform.position;
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.0001f, suckDuration);

                // 가속하며 빨려들어가는 느낌(ease-in).
                float e = t * t;
                transform.position = Vector3.LerpUnclamped(from, to, e);

                // 흡입 중 살짝 커졌다 원래 크기로(pop).
                float s = 1f + (popScale - 1f) * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                transform.localScale = baseScale * s;

                yield return null;
            }

            transform.position = to;
            transform.localScale = baseScale;
            transform.SetParent(slot.transform, true); // 특정 구역의 자식으로 배치 완료
            sr.sortingOrder = baseOrder;

            isPlacing = false;
        }
    }
}
