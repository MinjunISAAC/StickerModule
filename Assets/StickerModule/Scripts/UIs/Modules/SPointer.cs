using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 레거시 Input Manager / 새 Input System 양쪽을 지원하는 포인터 입력 헬퍼.
    //  - 마우스/터치 공용 (Input System 의 Pointer.current 사용).
    //  - Player Settings 의 Active Input Handling 설정에 따라 자동 분기.
    // --------------------------------------------------
    public static class SPointer
    {
        // 이번 프레임에 눌림 시작.
        public static bool Down
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButtonDown(0);
#elif ENABLE_INPUT_SYSTEM
                return Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
#else
                return false;
#endif
            }
        }

        // 눌린 상태 유지 중.
        public static bool Held
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButton(0);
#elif ENABLE_INPUT_SYSTEM
                return Pointer.current != null && Pointer.current.press.isPressed;
#else
                return false;
#endif
            }
        }

        // 이번 프레임에 떼어짐.
        public static bool Up
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButtonUp(0);
#elif ENABLE_INPUT_SYSTEM
                return Pointer.current != null && Pointer.current.press.wasReleasedThisFrame;
#else
                return false;
#endif
            }
        }

        // 스크린 좌표(픽셀).
        public static Vector3 Position
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
                return Pointer.current != null ? (Vector3)Pointer.current.position.ReadValue() : Vector3.zero;
#else
                return Vector3.zero;
#endif
            }
        }
    }
}
