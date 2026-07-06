using System;

namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 스티커가 붙는 순간 재생하는 wrap 연출 공통 인터페이스.
    //  - 연출 동안만 메시 렌더러를 켜고, 끝나면 onComplete 로 알린다.
    //  - 호출부(UI_DraggableSticker)가 완료 시 다시 SpriteRenderer 로 되돌린다.
    //  - 구현: UI_StickerMeshWrap(정점) / UI_StickerBoneWrap(본 스킨)
    // --------------------------------------------------
    public interface IStickerWrap
    {
        // 연출 재생: 자기 렌더러를 켜고, 끝나면 onComplete 호출.
        void PlayWrap(Action onComplete);

        // 렌더러 끄고 평평(초기) 상태로 되돌림.
        void Hide();
    }
}
