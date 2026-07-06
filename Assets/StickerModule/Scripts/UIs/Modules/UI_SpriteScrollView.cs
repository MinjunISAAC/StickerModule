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
    [ExecuteAlways] // 에디터에서도 배열(레이아웃)이 보이도록
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
        [SerializeField] private float paddingStart = 0f;     // 앞(왼쪽) 여백
        [SerializeField] private float paddingEnd = 0f;       // 뒤(오른쪽) 여백
        [SerializeField] private bool applyMaskInteraction = true;

        [Header("Scroll")]
        [SerializeField] private bool snapToItem = true;      // 손 놓으면 가장 가까운 아이템 위치로 스냅
        [SerializeField] private float settleSmoothTime = 0.15f; // 정착 부드러움(작을수록 빠름)
        [SerializeField] private float momentum = 0.12f;      // 놓을 때 속도 반영량(플릭 넘김)
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
        private float velocity = 0f;      // 월드 단위/초 (드래그 속도, 플릭 판정용)
        private float minX = 0f;          // content.localPosition.x 허용 최소
        private float maxX = 0f;          // content.localPosition.x 허용 최대(=홈, 왼쪽 정렬)
        private float homeX = 0f;         // 시작 시점 Content 위치(왼쪽 정렬 기준)
        private float contentWidth = 0f;

        private bool settling = false;    // 손 놓은 뒤 목표로 감쇠 이동 중
        private float targetX = 0f;       // 정착 목표 위치
        private float smoothVel = 0f;     // SmoothDamp 내부 속도

        private Transform leavingItem = null; // 뽑혀 나가는 중인 아이템(빈자리 애니용)
        private int gapIndex = -1;            // 빈자리 위치(레이아웃 인덱스)
        private float gapFill = 0f;           // 0=빈자리 유지, 1=완전히 메꿔짐

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
            // 시작 위치를 홈(왼쪽 정렬 기준)으로 기억. 이후 여기가 오른쪽 경계가 된다.
            if (content != null) homeX = content.localPosition.x;
            if (autoLayout) Rebuild();
            else RecalculateBounds();
        }

        private void Update()
        {
            // 에디터(비플레이)에서는 배열만 갱신하고 입력/스크롤 로직은 돌리지 않는다.
            if (!Application.isPlaying)
            {
                EditorRelayoutIfDirty();
                return;
            }

            HandlePointer();
            if (leavingItem != null) UpdateGap();
            if (dragMode != EDragMode.Scroll && settling) UpdateSettle();
        }

        // --------------------------------------------------
        // Editor Layout - 파라미터/자식 개수가 바뀌면 재배열
        // --------------------------------------------------
        private int _lastCount = -1;
        private float _lastSpacing, _lastPadStart, _lastPadEnd;

        private void EditorRelayoutIfDirty()
        {
            if (content == null) return;

            bool changed =
                content.childCount != _lastCount ||
                !Mathf.Approximately(spacing, _lastSpacing) ||
                !Mathf.Approximately(paddingStart, _lastPadStart) ||
                !Mathf.Approximately(paddingEnd, _lastPadEnd);
            if (!changed) return;

            _lastCount = content.childCount;
            _lastSpacing = spacing;
            _lastPadStart = paddingStart;
            _lastPadEnd = paddingEnd;

            Rebuild();
        }

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------
        // 아이템을 추가/제거한 뒤 호출하면 재정렬 + 경계 재계산.
        public void Rebuild()
        {
            if (content == null) return;

            int j = 0;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (!child.gameObject.activeSelf) continue;

                float baseX = paddingStart + j * spacing;
                // 빈자리(gapIndex) 이후 아이템은 빈자리 유지 시 한 칸 오른쪽,
                // 메꿔질수록(gapFill→1) 원위치로 당겨진다.
                float openExtra = (gapIndex >= 0 && j >= gapIndex) ? spacing : 0f;
                float finalX = Mathf.Lerp(baseX + openExtra, baseX, gapFill);

                child.localPosition = new Vector3(finalX, 0f, child.localPosition.z);
                j++;

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

            gapIndex = item.GetSiblingIndex(); // 빈자리 위치 기억
            leavingItem = item;
            gapFill = 0f;                       // 아직 빈자리 유지(드래그 진행에 따라 메꿔짐)

            item.SetParent(dragRoot, true);
            Rebuild();
        }

        // 유효한 자리에 못 놓았을 때 스크롤 목록의 원래 순서로 되돌린다.
        public void ReturnItem(Transform item, int siblingIndex)
        {
            if (item == null || content == null) return;

            leavingItem = null;
            gapIndex = -1;
            gapFill = 0f;

            item.SetParent(content, true);
            int idx = Mathf.Clamp(siblingIndex, 0, content.childCount - 1);
            item.SetSiblingIndex(idx);
            Rebuild();
        }

        // 아이템이 슬롯/구역에 배치되어 스크롤을 완전히 떠났을 때: 빈자리 메꿈.
        public void EndLeaving()
        {
            leavingItem = null;
            gapIndex = -1;
            gapFill = 0f;
            Rebuild();
        }

        // 뽑히는 아이템이 뷰포트 위로 빠져나간 정도에 따라 빈자리를 서서히 메꾼다.
        private void UpdateGap()
        {
            float topEdge = transform.position.y + viewportHeight * 0.5f;
            float outDist = leavingItem.position.y - topEdge;
            float range = Mathf.Max(0.01f, spacing);
            float f = Mathf.Clamp01(outDist / range); // 0=아직 안 빠짐, 1=완전히 빠져나감

            if (!Mathf.Approximately(f, gapFill))
            {
                gapFill = f;
                Rebuild();
            }
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

            contentWidth = paddingStart + count * spacing + paddingEnd;

            // 홈(왼쪽 정렬)에서 시작해, 화면을 넘치는 만큼만 왼쪽으로 스크롤 가능.
            float scrollable = Mathf.Max(0f, contentWidth - viewportWidth);
            maxX = homeX;
            minX = homeX - scrollable;
        }

        // --------------------------------------------------
        // Internal - Input / Gesture
        // --------------------------------------------------
        private void HandlePointer()
        {
            if (SPointer.Down)
            {
                Vector2 wp = ScreenToWorld(SPointer.Position);
                if (!viewportCollider.OverlapPoint(wp)) return;

                pressWorld = wp;
                pointerPrevWorldX = wp.x;
                velocity = 0f;
                settling = false; // 잡는 순간 정착 중단

                // 뷰포트 트리거 콜라이더와 겹쳐도 아이템을 정확히 찾도록 전부 검사.
                candidate = null;
                var hits = Physics2D.OverlapPointAll(wp, itemLayerMask);
                for (int i = 0; i < hits.Length; i++)
                {
                    var d = hits[i].GetComponentInParent<UI_DraggableSticker>();
                    if (d != null) { candidate = d; break; }
                }
                dragMode = EDragMode.Undecided;
            }
            else if (SPointer.Held && dragMode != EDragMode.None)
            {
                Vector2 wp = ScreenToWorld(SPointer.Position);

                if (dragMode == EDragMode.Undecided)
                {
                    float dx = wp.x - pressWorld.x;
                    float dy = wp.y - pressWorld.y;

                    // 위로 충분히 끌었고, 세로 성분이 가로보다 크면 → 뽑기로 위임
                    if (candidate != null && dy > pullThreshold && dy >= Mathf.Abs(dx))
                    {
                        // 처음 누른 지점 기준으로 잡아야 밀리지 않는다.
                        candidate.BeginPull(this, targetCamera, pressWorld);
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
            else if (SPointer.Up)
            {
                if (dragMode == EDragMode.Scroll) BeginSettle();
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

        // 손을 놓는 순간, 속도(플릭)를 반영해 목표 위치를 정하고 정착을 시작한다.
        private void BeginSettle()
        {
            if (content == null) return;

            float predicted = content.localPosition.x + velocity * momentum;
            targetX = ComputeSnap(predicted);
            smoothVel = velocity;   // 드래그 속도를 이어받아 부드럽게 감쇠
            settling = true;
        }

        // 목표 위치를 경계 안으로 클램프하고, 필요하면 가장 가까운 아이템 위치로 스냅.
        private float ComputeSnap(float x)
        {
            float clamped = Mathf.Clamp(x, minX, maxX);
            if (!snapToItem || spacing <= 0f) return clamped;

            // maxX(홈)에서 spacing 간격으로 아이템이 정렬되므로, 그 격자에 맞춘다.
            float steps = Mathf.Round((maxX - clamped) / spacing);
            float snapped = maxX - steps * spacing;
            return Mathf.Clamp(snapped, minX, maxX);
        }

        // 목표 위치로 임계감쇠 이동(SmoothDamp) → 오버슈트/진동 없이 딱 멈춘다.
        private void UpdateSettle()
        {
            if (content == null) { settling = false; return; }

            float x = content.localPosition.x;
            x = Mathf.SmoothDamp(x, targetX, ref smoothVel, settleSmoothTime);

            if (Mathf.Abs(x - targetX) < 0.001f && Mathf.Abs(smoothVel) < 0.01f)
            {
                x = targetX;
                smoothVel = 0f;
                settling = false;
            }

            content.localPosition = new Vector3(x, content.localPosition.y, content.localPosition.z);
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
