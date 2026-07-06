using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gunter.Sticker.EditorTools
{
    // --------------------------------------------------
    // 스프라이트 스크롤뷰 + 스티커 뽑기/배치 예시를 씬에 자동 생성하는 에디터 툴.
    //  - Unity 메뉴: [Tools > Gunter Sticker > Build Sample]
    //  - 스크롤뷰(뷰포트/마스크/Content/아이템), 배치 구역(슬롯)을 모두 배선해서 만든다.
    //  - private [SerializeField] 필드는 SerializedObject 로 안전하게 설정한다.
    //  - 생성물은 "SAMPLE_StickerScroll" 루트 아래에 모이므로 통째로 지우면 원복.
    // --------------------------------------------------
    public static class StickerSampleBuilder
    {
        private const string ROOT_NAME = "SAMPLE_StickerScroll";
        private const string BASE_TEX_DIR = "Assets/StickerModule/Textures/Base";

        // 아이템으로 쓸 샘플 스프라이트(Base 텍스처).
        private static readonly string[] SAMPLE_SPRITES =
        {
            "Circle", "Square", "Capsule", "Triangle",
            "HexagonFlatTop", "HexagonPointTop", "IsometricDiamond", "9Sliced",
        };

        [MenuItem("Tools/Gunter Sticker/Build Sample")]
        public static void BuildSample()
        {
            // 기존 샘플 있으면 제거하고 새로 생성.
            var existing = GameObject.Find(ROOT_NAME);
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject(ROOT_NAME);

            EnsureOrthographicCamera();

            // ---------- 스크롤뷰 ----------
            var scrollGo = new GameObject("ScrollView");
            scrollGo.transform.SetParent(root.transform, false);
            scrollGo.transform.position = new Vector3(0f, -2.5f, 0f);

            float viewportW = 6f;
            float viewportH = 1.6f;

            var scroll = scrollGo.AddComponent<UI_SpriteScrollView>(); // BoxCollider2D 자동 추가

            // Content
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            // 첫 아이템이 뷰포트 왼쪽에서 시작하도록 살짝 왼쪽 아래로.
            contentGo.transform.localPosition = new Vector3(-viewportW * 0.5f + 0.6f, 0f, 0f);

            // Mask (클리핑) - Square 스프라이트를 뷰포트 크기로 스케일.
            var maskGo = new GameObject("Mask");
            maskGo.transform.SetParent(scrollGo.transform, false);
            var mask = maskGo.AddComponent<SpriteMask>();
            var squareSprite = LoadSprite("Square");
            if (squareSprite != null)
            {
                mask.sprite = squareSprite;
                float sw = squareSprite.bounds.size.x;
                float sh = squareSprite.bounds.size.y;
                maskGo.transform.localScale = new Vector3(
                    sw > 0f ? viewportW / sw : 1f,
                    sh > 0f ? viewportH / sh : 1f, 1f);
            }

            // 아이템들
            for (int i = 0; i < SAMPLE_SPRITES.Length; i++)
            {
                var sprite = LoadSprite(SAMPLE_SPRITES[i]);
                var itemGo = new GameObject("Item_" + i);
                itemGo.transform.SetParent(contentGo.transform, false);

                var sr = itemGo.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = 1;
                sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

                var col = itemGo.AddComponent<BoxCollider2D>(); // 히트 판정용
                if (sprite != null) col.size = sprite.bounds.size;

                itemGo.AddComponent<UI_DraggableSticker>();

                // 붙을 때 wrap 연출이 바로 보이도록 미리 굽는다(정점 방식).
                StickerMeshBaker.BakeSprite(itemGo);
            }

            // 스크롤뷰 private 필드 배선.
            var so = new SerializedObject(scroll);
            so.FindProperty("content").objectReferenceValue = contentGo.transform;
            so.FindProperty("targetCamera").objectReferenceValue = Camera.main;
            so.FindProperty("viewportWidth").floatValue = viewportW;
            so.FindProperty("viewportHeight").floatValue = viewportH;
            so.FindProperty("spacing").floatValue = 1.2f;
            so.FindProperty("autoLayout").boolValue = true;
            so.FindProperty("applyMaskInteraction").boolValue = true;
            so.ApplyModifiedProperties();

            // ---------- 배치 구역(슬롯) ----------
            var zoneGo = new GameObject("DropZone");
            zoneGo.transform.SetParent(root.transform, false);
            zoneGo.transform.position = new Vector3(0f, 1.5f, 0f);

            int slotCount = 4;
            float slotGap = 2f;
            float startX = -(slotCount - 1) * slotGap * 0.5f;
            for (int i = 0; i < slotCount; i++)
            {
                var slotGo = new GameObject("Slot_" + i);
                slotGo.transform.SetParent(zoneGo.transform, false);
                slotGo.transform.localPosition = new Vector3(startX + i * slotGap, 0f, 0f);
                slotGo.AddComponent<UI_StickerSlot>();
            }

            // ---------- 자유 배치 구역(면적 판정) ----------
            // 화면 위쪽 한쪽에 사각 구역. 안에 놓으면 그 위치에 그대로 붙는다.
            var freeZoneGo = new GameObject("FreeZone");
            freeZoneGo.transform.SetParent(root.transform, false);
            freeZoneGo.transform.position = new Vector3(0f, 3.2f, 0f);
            freeZoneGo.AddComponent<UI_StickerDropZone>();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[StickerSampleBuilder] 샘플 생성 완료. Play 버튼으로 테스트하세요.\n" +
                      "- 아래 스크롤 목록을 좌우로 끌면 스크롤, 아이템을 위로 끌면 뽑힙니다.\n" +
                      "- 노란 슬롯 근처에 놓으면 그 점에 스냅, 초록 구역(FreeZone) 안에 놓으면 그 위치에 붙습니다.");
        }

        // --------------------------------------------------
        // Helpers
        // --------------------------------------------------
        private static Sprite LoadSprite(string name)
        {
            string path = BASE_TEX_DIR + "/" + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning("[StickerSampleBuilder] 스프라이트를 찾지 못함: " + path);
            return sprite;
        }

        private static void EnsureOrthographicCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.transform.position = new Vector3(0f, 0f, -10f);
            }
            // 스프라이트 작업은 직교(orthographic) 카메라가 자연스럽다.
            cam.orthographic = true;
            cam.orthographicSize = 5f;
        }
    }
}
