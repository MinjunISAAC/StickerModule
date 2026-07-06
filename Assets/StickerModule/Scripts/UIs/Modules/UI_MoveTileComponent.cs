using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gunter.Sticker
{
    public class UI_MoveTileComponent : MonoBehaviour
    {
        // --------------------------------------------------
        // Enums
        // --------------------------------------------------
        public enum EMoveDirection
        {
            LeftTop_To_RightBottom,
            RightTop_To_LeftBottom,
            LeftBottom_To_RightTop,
            RightBottom_To_LeftlTop
        }

        // --------------------------------------------------
        // Components
        // --------------------------------------------------
        [SerializeField] private float moveSpeed = 0.1f;
        [SerializeField] private EMoveDirection direction;

        private Material mat = null;
        private Image targetImg = null;
        // Start is called before the first frame update
        void Start()
        {
            targetImg = GetComponent<Image>();
            if (targetImg != null)
            {
                mat = targetImg.material;
                mat.SetTextureOffset("_MainTex", Vector2.zero);
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (targetImg.materialForRendering != targetImg.material)
            {
                mat = targetImg.materialForRendering;
            }

            float offset = Time.time * moveSpeed;
            Vector2 moveOffset;
            if (direction == EMoveDirection.LeftTop_To_RightBottom) moveOffset = new Vector2(-offset, offset);
            else if (direction == EMoveDirection.RightTop_To_LeftBottom) moveOffset = new Vector2(offset, offset);
            else if (direction == EMoveDirection.LeftBottom_To_RightTop) moveOffset = new Vector2(-offset, -offset);
            else moveOffset = new Vector2(offset, -offset);

            mat.SetTextureOffset("_MainTex", moveOffset);
        }

    }

}