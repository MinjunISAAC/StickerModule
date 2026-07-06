# StickerModule 문서

스크롤뷰에서 스티커를 **뽑아 → 특정 위치에 붙이고 → 붙는 순간 연출(정점 wrap / 본 roll)** 을 재생하는 Unity 미니 모듈.

- Unity: **6000.3.11f1**
- Input System: **com.unity.inputsystem 1.19.0** (신규 Input System 활성)
- 네임스페이스: `Gunter.Sticker` (에디터 툴: `Gunter.Sticker.EditorTools`)
- 스크립트 위치: `Assets/StickerModule/Scripts/`

---

## 1. 전체 흐름

```
[스크롤 목록]  SpriteRenderer, 뷰포트 SpriteMask로 클리핑
     │  가로로 끌면 → 스크롤 (관성/스냅/탄성)
     │  위로 끌면   → 아이템 "뽑기"
     ▼
[뽑기/드래그]  스프라이트가 손가락을 따라다님
     │  · 뽑히는 동안 스크롤의 빈자리는 드래그 거리에 따라 서서히 메꿔짐
     ▼
[놓기] ──┬── 슬롯(점) 근처        → 그 점으로 흡입
         ├── 구역(면적) 안        → 놓은 위치에 붙음
         └── 유효한 곳 아님        → 원래 자리로 가속하며 복귀(빈자리 다시 벌어짐)
     ▼
[붙는 연출]  SpriteRenderer OFF → MeshRenderer ON → wrap 애니 재생
     │        · 정점 방식(UI_StickerMeshWrap) 또는 본 방식(UI_StickerBoneWrap)
     ▼
[최종]  MeshRenderer OFF → 다시 SpriteRenderer ON (가벼운 상태로 안착)
```

핵심 설계: **평소엔 SpriteRenderer로 가볍게, 붙는 연출 순간에만 Mesh로 전환** 후 끝나면 다시 스프라이트로 복귀.

---

## 2. 런타임 컴포넌트

### 2.1 `UI_SpriteScrollView`
SpriteRenderer 기반 가로 스크롤 뷰 (UGUI ScrollView 대체). `[ExecuteAlways]` 로 **에디터에서도 배열**된다.

| 필드 | 기본 | 설명 |
|---|---|---|
| `content` | - | 아이템들의 부모 Transform (이게 좌우로 이동) |
| `targetCamera` | null | 스크린→월드 변환용. 비우면 `Camera.main` |
| `viewportWidth/Height` | 5 / 3 | 뷰포트(보이는 영역) 크기. BoxCollider2D 크기로도 적용 |
| `autoLayout` | true | 시작 시 자식들을 spacing 간격으로 정렬 |
| `spacing` | 1.2 | 아이템 간 간격(stride) |
| `paddingStart / paddingEnd` | 0 / 0 | 앞(왼쪽) / 뒤(오른쪽) 여백 |
| `applyMaskInteraction` | true | 자식 SpriteRenderer의 maskInteraction을 자동으로 `VisibleInsideMask` |
| `snapToItem` | true | 손 놓으면 가장 가까운 아이템 위치로 스냅 |
| `settleSmoothTime` | 0.15 | 정착 부드러움(작을수록 빠름, SmoothDamp) |
| `momentum` | 0.12 | 놓을 때 속도 반영량(플릭 넘김) |
| `elasticLimit` | 1.5 | 경계 밖 최대 드래그(고무줄) |
| `itemLayerMask` | Everything | 뽑기 히트 판정 레이어 |
| `dragRoot` | null | 뽑힌 아이템이 잠시 머무는 부모(null=씬 루트) |
| `decideThreshold` | 0.15 | 스크롤/뽑기 방향 결정 최소 이동 |
| `pullThreshold` | 0.4 | 위로 이만큼 끌면 뽑기 발동 |

동작 포인트:
- **제스처 판별**: 가로 우세 → 스크롤 / 위로 `pullThreshold` 초과 → 그 아이템을 `UI_DraggableSticker.BeginPull`로 위임
- **정착**: 손 놓으면 `SmoothDamp`(임계감쇠)로 목표 위치까지 이동 → 오버슈트/진동 없음. `snapToItem` 시 가장 가까운 아이템 격자에 스냅
- **홈 개념**: 시작 시 content 위치를 `homeX`(오른쪽 경계=왼쪽 정렬)로 기억
- **빈자리 애니**: 뽑히는 아이템이 뷰포트 위로 빠져나간 정도(`gapFill`)에 따라 이웃이 서서히 메꿔짐 / 복귀 시 다시 벌어짐
- 입력/스크롤 로직은 Play일 때만, 편집 모드에선 배열만 갱신

주요 public API: `Rebuild()`, `DetachItem(item)`, `ReturnItem(item, index)`, `EndLeaving()`, `GetSlotWorldPosition(index)`.

### 2.2 `UI_DraggableSticker`
스크롤에서 뽑혀 나온 스티커. `SpriteRenderer` 필수.

| 필드 | 기본 | 설명 |
|---|---|---|
| `suckDuration` | 0.25 | 슬롯/구역으로 빨려들어가는 시간 |
| `returnDuration` | 0.25 | 빈 곳에 놓았을 때 원위치 복귀 시간 |
| `popScale` | 1.15 | 흡입 중 살짝 커졌다 원복 |
| `draggingOrder` | 100 | 드래그 중 정렬 순서(맨 위) |

동작:
- `BeginPull(scrollView, camera, pressWorld)` — 스크롤뷰가 호출. **처음 누른 지점(pressWorld)** 기준으로 잡아 밀림 없음
- 놓으면 `EndPull`: **슬롯(점) → 구역(면적)** 순으로 판정, 둘 다 아니면 `CoReturn`(가속 복귀)
- 배치 성공 시 `CoPlace` → 흡입 이동 후 `PlaceAsWrappedMesh()`
- `PlaceAsWrappedMesh`: SpriteRenderer OFF → 자식의 `IStickerWrap.PlayWrap(콜백)` → 완료 시 `Hide()` + SpriteRenderer ON
- wrap 컴포넌트는 자식 `WrapMesh`에서 `GetComponentInChildren<IStickerWrap>`로 자동 탐색. **없으면(안 구웠으면) 스프라이트 그대로 배치**

### 2.3 `UI_StickerSlot`
스티커가 **점+반경**으로 붙는 자리.

| 필드 | 기본 | 설명 |
|---|---|---|
| `snapDistance` | 1.0 | 이 거리 안에서 놓아야 흡입 |

`static FindNearest(worldPos, out dist)`로 가장 가까운 빈 슬롯 탐색. 한 슬롯당 하나 점유(`Occupy`/`Clear`). 노란 기즈모.

### 2.4 `UI_StickerDropZone`
스티커를 **면적(사각 구역)** 안 아무 곳에나 자유 배치.

| 필드 | 기본 | 설명 |
|---|---|---|
| `size` | (4,4) | 구역 크기 |
| `clampInside` | true | 경계 밖에 놓으면 안으로 당김 |

`Contains(worldPos)`, `ClampPoint(worldPos)`, `static FindContaining(worldPos)`. 초록 기즈모.

### 2.5 `IStickerWrap` (인터페이스)
붙는 연출 공통 규약.
```csharp
void PlayWrap(System.Action onComplete); // 렌더러 켜고 재생, 끝나면 콜백
void Hide();                             // 렌더러 끄고 평평 상태로
```
구현: `UI_StickerMeshWrap`(정점), `UI_StickerBoneWrap`(본).

### 2.6 `UI_StickerMeshWrap` (정점 방식)
격자 메시 정점을 **원기둥 곡면**에 매핑해 감았다 편다. `MeshFilter` 필수. 붙는 연출이 **곡면(1)→평평(0)** 으로 끝나 스프라이트와 매끄럽게 이어진다.

| 필드 | 기본 | 설명 |
|---|---|---|
| `axis` | Vertical | 감김 방향(Horizontal/Vertical) |
| `wrapAngle` | 2.2 | 감김 강도(라디안). 직교 카메라에선 가장자리 단축으로 표현 |
| `depthScale` | 0.15 | 깊이(Z) 굽힘(Lit/원근용) |
| `popScale` | 0.25 | 붙을 때 살짝 커졌다 원복 |
| `duration` | 0.5 | 연출 시간 |
| `overshoot` | true | 살짝 튕겼다 안정(EaseOutBack) |

### 2.7 `UI_StickerBoneWrap` (본 방식 / 롤)
실제 본 체인(SkinnedMeshRenderer)을 **선 방향으로 돌돌 말았다 편다**. `SkinnedMeshRenderer` 필수.

| 필드 | 기본 | 설명 |
|---|---|---|
| `autoAxis` | true | 회전축을 선 방향에 **수직(평면 내)** 으로 자동 → 면이 Z앞으로 말림 |
| `bendAxis` | up | autoAxis 끌 때 수동 축 |
| `bendAnglePerBone` | 32 | 본당 굽힘 각도(도). **본수 × 이 값 = 총 말림**(360°≈한 바퀴 코일) |
| `rollBand` | 0.5 | 말림 전이 폭(작을수록 국소적인 "감기는 가장자리") |
| `reverse` | false | 끝→시작 방향으로 말기 |
| `duration` | 0.5 | 연출 시간 |
| `overshoot` | true | 켜면 EaseOutBack(앞에 몰림), 끄면 SmoothStep(고르게) |

동작:
- **롤 프론트**가 시작→끝으로 이동하며 말림이 풀린다(스크롤 언롤)
- 이징 기본 `SmoothStep`으로 duration 전체에 고르게. `overshoot` 켜면 스냅감(단 앞에 몰림)
- 기즈모: 본 체인 표시(**초록=시작, 빨강=끝**, 노란 선)
- 우클릭 컨텍스트 메뉴 `Preview: Rolled/Flat`

### 2.8 `UI_StickerBoneLine` (본 authoring 데이터)
에디터에서 **선을 긋고 그 위에 본을 몇 개 놓을지** 정하는 데이터.

| 필드 | 기본 | 설명 |
|---|---|---|
| `points` | [] | 폴리라인(로컬 공간) |
| `boneCount` | 8 | 선 위 본 개수(많을수록 매끄러움) |

`SampleBonePositions()` — 선 위를 균등 샘플한 본 위치 반환. 커스텀 에디터로 편집(§4.3).

### 2.9 `SPointer` (입력 헬퍼)
레거시 Input / 신규 Input System 양쪽 지원(마우스·터치 공용). `#if` 조건부 컴파일.
`SPointer.Down / Held / Up / Position`.

---

## 3. 에디터 툴 (메뉴)

| 메뉴 | 기능 |
|---|---|
| `Tools > Gunter Sticker > Build Sample` | 스크롤뷰 + 아이템(본 라인+스킨 베이크) + 슬롯 + 구역 예시 씬 자동 생성 |
| `Tools > Gunter Sticker > Bake Mesh From Sprite` | 선택 오브젝트를 **정점 wrap 메시**로 굽기 |
| `Tools > Gunter Sticker > Bake Skinned Mesh (From Bone Line)` | 선택 오브젝트를 **본 스킨 메시(롤)** 로 굽기 |

### 3.1 `StickerMeshBaker`
- 스프라이트의 **월드 크기(`sprite.bounds`)·UV를 그대로 유지**한 격자 메시 생성(분할 `SUBDIVISIONS=12`)
- 메시는 **자식 `WrapMesh`** 에 둔다 (SpriteRenderer와 MeshRenderer는 같은 오브젝트 공존 불가)
- 스킨 베이크: 선 위 본 위치로 **실제 Bone 체인** 생성, 정점을 **인접 본 최대 4개에 smoothstep falloff**로 부드럽게 분배(깨짐 방지), 바인드포즈/SkinnedMeshRenderer 세팅
- 메시 에셋은 **고정 경로에 덮어쓰기**(재베이크 시 파일 안 쌓임): `Meshes/Baked`, 머티리얼 `Materials/Baked`
- public 진입점: `BakeSprite(go)`, `BakeSkinned(go)`(부모에서 SpriteRenderer 탐색), `ClearBake(go)`

### 3.2 `StickerSampleBuilder`
Build Sample이 생성하는 것: 루트 `SAMPLE_StickerScroll` 아래
`ScrollView`(+Content+Mask+Items) / `DropZone`(Slot_0..3) / `FreeZone`(면적 구역). 각 아이템은 `UI_StickerBoneLine`(기본 가로선) + 스킨 베이크 상태.

### 3.3 `StickerBoneLineEditor` (UI_StickerBoneLine 커스텀 에디터)
- **그리기 모드**: 켜고 씬에서 스티커 위 클릭 → 선 점 추가. 점은 구 핸들로 이동
- 선 따라 **본 미리보기**(초록=시작→빨강=끝) + start/end 라벨
- **베이크 상태 표시** + `Bake Skinned` / `Clear Bake(베이크 제거)` 버튼

### 3.4 `StickerBoneWrapEditor` (UI_StickerBoneWrap 커스텀 에디터)
- **에디터 프리뷰**: `Roll` 슬라이더로 스크럽 / `▶ Play Preview`(Duration만큼 실시간 재생) / `Rolled`·`Flat` 버튼
- Play 없이 스키닝·연출·시간 확인 가능

---

## 4. 사용 워크플로우

### A) 빠른 확인 (샘플)
1. `Tools > Gunter Sticker > Build Sample`
2. Play → 아이템을 위로 뽑아 슬롯/초록 구역에 놓기 → 붙는 연출 재생

### B) 정점 wrap 방식 (선 그리기 불필요)
1. 스티커(SpriteRenderer) 선택 → `Bake Mesh From Sprite`
2. Play → 붙일 때 곡면 wrap + 팝 연출

### C) 본 롤 방식 (돌돌 말림)
1. 스티커 선택 → `UI_StickerBoneLine` 추가(샘플은 자동)
2. **그리기 모드**로 선 긋기 + `Bone Count` 설정
3. `Bake Skinned` → `WrapMesh` + `Bones` + `UI_StickerBoneWrap` 생성
4. `UI_StickerBoneWrap` 프리뷰 슬라이더로 말림 확인 → Play

**이미지 변경 후 재베이크**: SpriteRenderer의 Sprite 교체 → 같은 Bake 메뉴/버튼 다시 실행(에셋 덮어쓰기).
**새로 그리기**: `Clear Bake` → 선 다시 그리기 → `Bake Skinned`.

---

## 5. 튜닝 가이드

- **스크롤 감**: `settleSmoothTime`(정착 속도), `momentum`(플릭), `snapToItem`, `spacing`, `padding*`
- **뽑기**: `pullThreshold`(뽑히는 민감도), `decideThreshold`
- **배치/복귀**: `suckDuration`, `returnDuration`, `popScale`, 슬롯 `snapDistance`, 구역 `size`
- **정점 wrap**: `wrapAngle`(강도), `popScale`, `duration`, `overshoot`
- **본 롤**: `bendAnglePerBone` × `Bone Count` = 총 말림(돌돌 정도), `rollBand`(감기는 가장자리 폭), `reverse`(방향), `duration`, `overshoot`

---

## 6. 주의사항 / 한계

- **직교(Orthographic) 카메라**: Z(깊이) 굽힘은 화면에 안 보이고 **가장자리 단축(foreshortening)** 으로만 표현됨. 말림의 입체감을 제대로 보려면 **Perspective 카메라**나 카메라를 살짝 기울이기
- **LBS(선형 블렌드 스키닝) 한계**: 본 롤을 과하게(예: 한 바퀴 이상 빡빡하게) 하면 정점이 겹치며 깨질 수 있음. `Bone Count`↑ + `bendAnglePerBone`↓ + `rollBand`↑ 로 완화
- **SpriteRenderer ↔ MeshRenderer 공존 불가**: 그래서 메시는 자식 `WrapMesh`에 둠
- **선(line) ≠ 베이크 결과**: 선을 리셋해도 이미 구운 본은 남음 → `Clear Bake` 사용
- **베이크는 편집 모드에서**(Play 전) 실행. 여러 오브젝트 선택 시 일괄 베이크(본 방식은 각자 선 필요)
- 신규 Input System 사용 중이므로 입력은 `SPointer` 경유(직접 `UnityEngine.Input` 호출 금지)

---

## 7. 파일 목록

```
Assets/StickerModule/Scripts/
├─ UIs/Modules/
│  ├─ UI_SpriteScrollView.cs     가로 스크롤 + 제스처 + 정착/스냅 + 빈자리 애니
│  ├─ UI_DraggableSticker.cs     뽑기/흡입/복귀 + 붙는 연출 트리거
│  ├─ UI_StickerSlot.cs          점+반경 배치 자리
│  ├─ UI_StickerDropZone.cs      면적(사각) 자유 배치 구역
│  ├─ UI_StickerMeshWrap.cs      정점 곡면 wrap (MeshRenderer)
│  ├─ UI_StickerBoneWrap.cs      본 체인 롤 (SkinnedMeshRenderer)
│  ├─ UI_StickerBoneLine.cs      본 라인 authoring 데이터
│  ├─ IStickerWrap.cs            wrap 공통 인터페이스
│  └─ SPointer.cs                레거시/신규 Input 공용 포인터
└─ Editor/
   ├─ StickerMeshBaker.cs        정점/스킨 베이크 + Clear Bake
   ├─ StickerSampleBuilder.cs    예시 씬 생성
   ├─ StickerBoneLineEditor.cs   선 그리기/본 개수/베이크 버튼
   └─ StickerBoneWrapEditor.cs   롤 에디터 프리뷰
```
