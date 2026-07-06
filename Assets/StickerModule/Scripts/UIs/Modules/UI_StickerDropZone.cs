using System.Collections.Generic;
using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 스티커를 자유 위치로 붙일 수 있는 사각 "구역".
    //  - 슬롯(UI_StickerSlot, 점+반경)과 달리 면적 안이면 어디든 붙는다.
    //  - 놓은 지점이 구역 안인지 판정하고, 밖이면 경계 안으로 밀어넣는다.
    //  - 테스트/자유 배치용.
    // --------------------------------------------------
    public class UI_StickerDropZone : MonoBehaviour
    {
        // --------------------------------------------------
        // Static Registry
        // --------------------------------------------------
        private static readonly List<UI_StickerDropZone> _zones = new List<UI_StickerDropZone>();

        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [SerializeField] private Vector2 size = new Vector2(4f, 4f); // 구역 크기(월드 단위)
        [SerializeField] private bool clampInside = true;           // 경계 밖 드롭 시 안으로 당김

        // --------------------------------------------------
        // Properties
        // --------------------------------------------------
        public Vector2 Size => size;

        // --------------------------------------------------
        // Unity
        // --------------------------------------------------
        private void OnEnable()  { if (!_zones.Contains(this)) _zones.Add(this); }
        private void OnDisable() { _zones.Remove(this); }

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------
        // 월드 좌표가 구역 안에 있는지 판정.
        public bool Contains(Vector2 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f;
        }

        // 월드 좌표를 구역 안으로 클램프한 위치 반환.
        public Vector3 ClampPoint(Vector3 worldPos)
        {
            if (!clampInside) return worldPos;

            Vector3 local = transform.InverseTransformPoint(worldPos);
            local.x = Mathf.Clamp(local.x, -size.x * 0.5f, size.x * 0.5f);
            local.y = Mathf.Clamp(local.y, -size.y * 0.5f, size.y * 0.5f);
            local.z = 0f;
            return transform.TransformPoint(local);
        }

        // 해당 월드 좌표를 포함하는 구역을 찾는다(없으면 null).
        public static UI_StickerDropZone FindContaining(Vector2 worldPos)
        {
            for (int i = 0; i < _zones.Count; i++)
                if (_zones[i] != null && _zones[i].Contains(worldPos)) return _zones[i];
            return null;
        }

        // --------------------------------------------------
        // Gizmos - 씬 뷰에서 구역 확인용
        // --------------------------------------------------
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));
        }
    }
}
