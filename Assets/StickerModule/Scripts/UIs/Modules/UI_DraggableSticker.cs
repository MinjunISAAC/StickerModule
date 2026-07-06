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
        [SerializeField] private float suckDuration = 0.25f;   // 빨려들어가는 시간
        [SerializeField] private float returnDuration = 0.25f; // 빈 곳에 놓았을 때 복귀 시간
        [SerializeField] private float popScale = 1.15f;       // 흡입 중 살짝 커졌다 원복
        [SerializeField] private int draggingOrder = 100;      // 드래그 중 정렬 순서(맨 위)

        // --------------------------------------------------
        // Fields
        // --------------------------------------------------
        private SpriteRenderer sr = null;
        private IStickerWrap wrap = null;              // 배치 시 재생할 wrap(정점/본 방식 공통)
        private UI_SpriteScrollView owner = null;
        private Camera cam = null;

        private bool isPulled = false;   // 뽑혀서 손가락 따라다니는 중
        private bool isPlacing = false;  // 흡입 연출 중
        private Vector3 grabOffset = Vector3.zero;
        private int baseOrder = 0;
        private Vector3 baseScale = Vector3.one;
        private int originalSiblingIndex = 0; // 뽑히기 전 스크롤 목록에서의 순서

        // --------------------------------------------------
        // Unity
        // --------------------------------------------------
        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            wrap = GetComponentInChildren<IStickerWrap>(true);   // 자식 "WrapMesh"(구운 경우에만 존재)
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

            originalSiblingIndex = transform.GetSiblingIndex(); // 분리 전 순서 기억
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

            // 1순위: 가까운 슬롯(점+반경). 2순위: 붙이는 구역(면적) 안.
            float dist;
            var slot = UI_StickerSlot.FindNearest(transform.position, out dist);
            if (slot != null && !slot.IsOccupied && dist <= slot.SnapDistance)
            {
                slot.Occupy(this);
                StartCoroutine(CoPlace(slot.transform.position, slot.transform));
                return;
            }

            var zone = UI_StickerDropZone.FindContaining(transform.position);
            if (zone != null)
            {
                // 구역 안이면 놓은 위치(경계 안으로 클램프)에 그대로 붙인다.
                StartCoroutine(CoPlace(zone.ClampPoint(transform.position), zone.transform));
                return;
            }

            // 유효한 자리 없음 → 원래 순서로, 가속하며 미끄러져 복귀
            transform.localScale = baseScale;
            if (owner != null)
            {
                StartCoroutine(CoReturn());
            }
            else
            {
                sr.sortingOrder = baseOrder;
                sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            }
        }

        // 빈 곳에 놓았을 때: 원래 자리로 가속하며(ease-in) 미끄러져 복귀.
        private IEnumerator CoReturn()
        {
            isPlacing = true; // 복귀 중 재-뽑기 방지

            Vector3 from = transform.position;

            // 원래 순서로 되돌려 최종(정착) 위치를 구한다.
            owner.ReturnItem(transform, originalSiblingIndex);
            Vector3 to = transform.position;

            // 비행 중엔 잘리지 않게(마스크 밖) + 맨 위로.
            if (sr != null)
            {
                sr.maskInteraction = SpriteMaskInteraction.None;
                sr.sortingOrder = draggingOrder;
            }

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.0001f, returnDuration);
                float e = Mathf.Clamp01(t); e *= e; // 가속(ease-in)
                transform.position = Vector3.LerpUnclamped(from, to, e);
                yield return null;
            }
            transform.position = to;

            // 도착 → 스크롤 상태로 복원(다시 클리핑).
            if (sr != null)
            {
                sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                sr.sortingOrder = baseOrder;
            }
            isPlacing = false;
        }

        // 목표 위치로 흡입 이동 후, 그 부모의 자식으로 붙이고 wrap 연출.
        private IEnumerator CoPlace(Vector3 to, Transform parent)
        {
            isPlacing = true;
            if (owner != null) owner.EndLeaving(); // 스크롤 빈자리 메꿈

            Vector3 from = transform.position;
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
            transform.SetParent(parent, true); // 해당 구역의 자식으로 배치 완료
            sr.sortingOrder = baseOrder;

            // 도착하는 순간 메시로 전환하고 곡면 wrap 연출("쫘악" 붙기).
            PlaceAsWrappedMesh();

            isPlacing = false;
        }

        // 스프라이트 → 구워둔 격자 메시로 바꾸고 wrap 재생.
        // (메시를 굽지 않았다면 스프라이트 그대로 둔다.)
        // 붙는 순간: 스프라이트 끄고 → 메시로 wrap 연출 → 끝나면 다시 스프라이트로.
        private void PlaceAsWrappedMesh()
        {
            if (wrap == null) return; // 굽지 않았으면 스프라이트 그대로

            if (sr != null) sr.enabled = false;
            wrap.PlayWrap(OnWrapComplete);
        }

        private void OnWrapComplete()
        {
            wrap.Hide();                       // 메시 렌더러 끄기
            if (sr != null) sr.enabled = true; // 최종 상태는 다시 SpriteRenderer
        }
    }
}
