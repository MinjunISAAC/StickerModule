using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // SpriteRenderer 기반 가로(Horizontal) 스크롤 뷰
    //  - UGUI ScrollView 를 Canvas 없이 SpriteRenderer 로 대체한다.
    //  - 클리핑 : 뷰포트에 SpriteMask 를 두고, 자식 아이템의 maskInteraction 을 자동 설정.
    //  - 입력   : 뷰포트 BoxCollider2D 영역 안에서 드래그(마우스/터치 공용).
    //  - 제스처 : 가로로 끌면 스크롤, 위로 끌면 해당 아이템을 뽑기(Pull)로 위임.
    //  - 로직   : Content 이동 + 관성(inertia) + 경계 탄성(elastic) 을 직접 구현.
    // --------------------------------------------------
    [RequireComponent(typeof(BoxCollider2D))]
    public class UI_SpriteScrollView : MonoBehaviour
    {
        // --------------------------------------------------
        // Enums
        // --------------------------------------------------
        private enum EDragMode
        {
            None,
            Undecided,  // 방향 결정 전
            Scroll,     // 가로 스크롤 확정
        }

        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [Header("References")]
        [SerializeField] private Transform content = null;   // 아이템(SpriteRenderer)들의 부모
        [SerializeField] private Camera targetCamera = null; // 스크린→월드 변환용 (비우면 Camera.main)

        [Header("Viewport (World Units)")]
        [SerializeField] private float viewportWidth = 5f;
        [SerializeField] private float viewportHeight = 3f;

        [Header("Layout")]
        [SerializeField] private bool autoLayout = true;      // 시작 시 자식들을 spacing 간격으로 정렬
        [SerializeField] private float spacing = 1.2f;        // 아이템 간 간격(월드 단위, stride)
        [SerializeField] private bool applyMaskInteraction = true;

        [Header("Scroll")]
        [SerializeField] private bool useInertia = true;
        [SerializeField] private float inertiaDamping = 8f;   // 클수록 빨리 멈춤
        [SerializeField] private float elasticStrength = 12f; // 경계 밖에서 되돌아오는 속도
        [SerializeField] private float elasticLimit = 1.5f;   // 경계 밖 최대 드래그(월드 단위)

        [Header("Item Pull (뽑기)")]
        [SerializeField] private LayerMask itemLayerMask = ~0;  // 아이템 Collider2D 레이어
        [SerializeField] private Transform dragRoot = null;     // 뽑힌 아이템이 머무는 부모(null=씬 루트)
        [SerializeField] private float decideThreshold = 0.15f; // 방향 결정 최소 이동(월드)
        [SerializeField] private float pullThreshold = 0.4f;    // 위로 이만큼 끌면 뽑기 시작

        // --------------------------------------------------
        // Fields
        // --------------------------------------------------
        private BoxCollider2D viewportCollider = null;
        private EDragMode dragMode = EDragMode.None;
        private Vector2 pressWorld = Vector2.zero;
        private UI_DraggableSticker candidate = null;
        private float pointerPrevWorldX = 0f;
        private float velocity = 0f;      // 월드 단위/초
        private float minX = 0f;          // content.localPosition.x 허용 최소
        private float maxX = 0f;          // content.localPosition.x 허용 최대
        private float contentWidth = 0f;

        // --------------------------------------------------
        // Properties
        // --------------------------------------------------
        public Transform Content => content;

        // --------------------------------------------------
        // Unity
        // --------------------------------------------------
        private void Awake()
        {
            viewportCollider = GetComponent<BoxCollider2D>();
            viewportCollider.isTrigger = true;
            if (targetCamera == null) targetCamera = Camera.main;
        }

        private void OnValidate()
        {
            var col = GetComponent<BoxCollider2D>();
            if (col != null) col.size = new Vector2(viewportWidth, viewportHeight);
        }

        private void Start()
        {
            viewportCollider.size = new Vector2(viewportWidth, viewportHeight);
            if (autoLayout) Rebuild();
            else RecalculateBounds();
        }

        private void Update()
        {
            HandlePointer();
            if (dragMode != EDragMode.Scroll) UpdateInertia();
        }

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------
        // 아이템을 추가/제거한 뒤 호출하면 재정렬 + 경계 재계산.
        public void Rebuild()
        {
            if (content == null) return;

            float x = 0f;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (!child.gameObject.activeSelf) continue;

                child.localPosition = new Vector3(x, 0f, child.localPosition.z);
                x += spacing;

                if (applyMaskInteraction)
                {
                    var sr = child.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                }
            }

            RecalculateBounds();
        }

        // 아이템을 스크롤에서 분리한다(뽑기 시작 시 호출). 월드 위치 유지.
        public void DetachItem(Transform item)
        {
            if (item == null) return;
            item.SetParent(dragRoot, true);
            Rebuild();
        }

        // 유효한 자리에 못 놓았을 때 스크롤 목록으로 되돌린다.
        public void ReturnItem(Transform item)
        {
            if (item == null || content == null) return;
            item.SetParent(content, true);
            Rebuild();
        }

        // --------------------------------------------------
        // Internal - Bounds
        // --------------------------------------------------
        private void RecalculateBounds()
        {
            int count = 0;
            if (content != null)
            {
                for (int i = 0; i < content.childCount; i++)
                    if (content.GetChild(i).gameObject.activeSelf) count++;
            }

            contentWidth = count * spacing;

            // 왼쪽 끝(0)에서 시작해, 화면을 넘치는 만큼만 왼쪽으로 스크롤 가능.
            float scrollable = Mathf.Max(0f, contentWidth - viewportWidth);
            maxX = 0f;
            minX = -scrollable;
        }

        // --------------------------------------------------
        // Internal - Input / Gesture
        // --------------------------------------------------
        private void HandlePointer()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector2 wp = ScreenToWorld(Input.mousePosition);
                if (!viewportCollider.OverlapPoint(wp)) return;

                pressWorld = wp;
                pointerPrevWorldX = wp.x;
                velocity = 0f;

                var hit = Physics2D.OverlapPoint(wp, itemLayerMask);
                candidate = hit != null ? hit.GetComponentInParent<UI_DraggableSticker>() : null;
                dragMode = EDragMode.Undecided;
            }
            else if (Input.GetMouseButton(0) && dragMode != EDragMode.None)
            {
                Vector2 wp = ScreenToWorld(Input.mousePosition);

                if (dragMode == EDragMode.Undecided)
                {
                    float dx = wp.x - pressWorld.x;
                    float dy = wp.y - pressWorld.y;

                    // 위로 충분히 끌었고, 세로 성분이 가로보다 크면 → 뽑기로 위임
                    if (candidate != null && dy > pullThreshold && dy >= Mathf.Abs(dx))
                    {
                        candidate.BeginPull(this, targetCamera, wp);
                        dragMode = EDragMode.None; // 이후 입력은 아이템이 처리
                        candidate = null;
                        return;
                    }

                    // 가로로 충분히 끌었으면(또는 빈 영역) → 스크롤 확정
                    if (Mathf.Abs(dx) > decideThreshold)
                    {
                        dragMode = EDragMode.Scroll;
                        pointerPrevWorldX = wp.x; // 튐 방지
                    }
                }

                if (dragMode == EDragMode.Scroll)
                {
                    float delta = wp.x - pointerPrevWorldX;
                    pointerPrevWorldX = wp.x;

                    MoveContent(delta, true);

                    if (Time.deltaTime > 0f)
                        velocity = Mathf.Lerp(velocity, delta / Time.deltaTime, 0.5f);
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                dragMode = EDragMode.None;
                candidate = null;
            }
        }

        // --------------------------------------------------
        // Internal - Movement
        // --------------------------------------------------
        private void MoveContent(float delta, bool elastic)
        {
            if (content == null) return;

            float x = content.localPosition.x + delta;

            if (elastic)
            {
                if (x > maxX) x = maxX + RubberBand(x - maxX);
                else if (x < minX) x = minX - RubberBand(minX - x);
            }
            else
            {
                x = Mathf.Clamp(x, minX, maxX);
            }

            content.localPosition = new Vector3(x, content.localPosition.y, content.localPosition.z);
        }

        // 경계 밖으로 나갈수록 점점 덜 밀리도록 저항을 준다(고무줄 효과).
        private float RubberBand(float over)
        {
            return elasticLimit * (1f - 1f / (over / elasticLimit + 1f));
        }

        private void UpdateInertia()
        {
            if (content == null) return;

            float x = content.localPosition.x;

            // 경계 밖이면 탄성으로 되돌린다.
            if (x > maxX || x < minX)
            {
                float target = x > maxX ? maxX : minX;
                x = Mathf.Lerp(x, target, Time.deltaTime * elasticStrength);
                if (Mathf.Abs(x - target) < 0.001f) x = target;

                velocity = 0f;
                content.localPosition = new Vector3(x, content.localPosition.y, content.localPosition.z);
            }
            // 경계 안이면 관성으로 감속.
            else if (useInertia && Mathf.Abs(velocity) > 0.01f)
            {
                velocity = Mathf.Lerp(velocity, 0f, Time.deltaTime * inertiaDamping);
                MoveContent(velocity * Time.deltaTime, true);
            }
        }

        private Vector2 ScreenToWorld(Vector3 screen)
        {
            if (targetCamera == null) targetCamera = Camera.main;
            Vector3 w = targetCamera.ScreenToWorldPoint(screen);
            return new Vector2(w.x, w.y);
        }

        // --------------------------------------------------
        // Gizmos - 씬 뷰에서 뷰포트 영역 확인용
        // --------------------------------------------------
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(viewportWidth, viewportHeight, 0f));
        }
    }
}
