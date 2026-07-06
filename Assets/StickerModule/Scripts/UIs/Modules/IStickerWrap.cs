namespace Gunter.Sticker
{
    // --------------------------------------------------
    // 스티커가 붙는 순간 재생하는 wrap 연출 공통 인터페이스.
    //  - UI_StickerMeshWrap  : 정점 파라메트릭 방식(MeshRenderer)
    //  - UI_StickerBoneWrap  : 실제 본 체인 방식(SkinnedMeshRenderer)
    //  구현체는 자신의 렌더러 표시까지 책임진다.
    // --------------------------------------------------
    public interface IStickerWrap
    {
        void ResetFlat();  // 초기(평평) 상태로 + 렌더러 표시
        void PlayWrap();   // 연출 재생
    }
}
