using System.Collections.Generic;
using UnityEngine;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 스티커가 빨려들어가 붙는 "특정 위치".
    //  - 씬의 구역마다 이 컴포넌트를 두면, 뽑힌 스티커가 놓일 때
    //    가장 가까운(그리고 흡입 거리 안인) 슬롯을 찾아 붙는다.
    //  - 한 슬롯에는 하나의 스티커만 점유(occupy)한다.
    // --------------------------------------------------
    public class UI_StickerSlot : MonoBehaviour
    {
        // --------------------------------------------------
        // Static Registry
        // --------------------------------------------------
        private static readonly List<UI_StickerSlot> _slots = new List<UI_StickerSlot>();

        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [SerializeField] private float snapDistance = 1.0f; // 이 거리 안에서 놓아야 빨려들어감

        // --------------------------------------------------
        // Fields
        // --------------------------------------------------
        private UI_DraggableSticker _occupant = null;

        // --------------------------------------------------
        // Properties
        // --------------------------------------------------
        public float SnapDistance => snapDistance;
        public bool IsOccupied => _occupant != null;

        // --------------------------------------------------
        // Unity
        // --------------------------------------------------
        private void OnEnable()  { if (!_slots.Contains(this)) _slots.Add(this); }
        private void OnDisable() { _slots.Remove(this); }

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------
        public void Occupy(UI_DraggableSticker sticker) { _occupant = sticker; }
        public void Clear() { _occupant = null; }

        // 월드 위치에서 가장 가까운(비어있는) 슬롯을 반환.
        public static UI_StickerSlot FindNearest(Vector2 worldPos, out float distance)
        {
            UI_StickerSlot best = null;
            distance = Mathf.Infinity;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot == null || slot.IsOccupied) continue;

                float d = Vector2.Distance(worldPos, slot.transform.position);
                if (d < distance)
                {
                    distance = d;
                    best = slot;
                }
            }

            return best;
        }

        // --------------------------------------------------
        // Gizmos - 씬 뷰에서 흡입 거리 확인용
        // --------------------------------------------------
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, snapDistance);
        }
    }
}
