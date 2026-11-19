# Map Tool (`com.kmj5515.maptool`)

Grid-based level authoring tool for Unity, built for artists. Place, scatter, draw zones and spawns, validate, and export to JSON.

유니티 에디터에서 격자 위에 맵을 만드는 툴입니다. 아티스트가 쓰는 것을 기준으로 만들었습니다.

- **배치**: 오브젝트를 칸 단위로 놓고 돌리고 지웁니다. 놓을 수 없으면 클릭 전에 이유를 보여 줍니다.
- **장식 브러시**: 소품을 흩뿌립니다.
- **존·스폰**: 구역(사운드 존 등)과 플레이어 시작 위치를 그립니다.
- **검증과 빠른 수정**: 겹침, 맵 밖, 빠진 스폰 같은 문제를 찾아 한 번에 고칩니다.
- **JSON 내보내기·가져오기**
- 핵심 로직(`MapTool.Core`)은 유니티에 의존하지 않는 C#이고 EditMode 테스트가 있습니다.

## 요구 사항
- Unity 6 (6000.0) 이상

## 설치
`Packages/manifest.json`의 `dependencies`에 추가합니다.

```json
"com.kmj5515.maptool": "https://github.com/kmj5515/unity-maptool.git#v1.0.0"
```

또는 **Window > Package Manager > + > Install package from git URL...**에 `https://github.com/kmj5515/unity-maptool.git#v1.0.0`을 넣습니다.

`#v1.0.0`을 빼면 `main`의 최신 상태를 받습니다. 버전을 고정해 두는 것을 권장합니다.

## 시작하기
1. **Tools > Map Tool > Map Tool 창**을 엽니다.
2. 연습할 오브젝트가 없으면 **Tools > Map Tool > 샘플 오브젝트 생성**을 누릅니다. 상자·가구·벽 등 10종이 `Assets/MapToolSamples`에 만들어집니다.
3. 창에서 **새 맵 만들기...**를 눌러 시작합니다.

자세한 사용법은 [사용 가이드](Documentation~/MapTool-Guide.md)를 보세요.

## 사운드 존 연동 (선택)
[`com.kmj5515.audio`](https://github.com/kmj5515/unity-wwise-audio)를 함께 설치하면 맵툴의 존이 오디오 존이 됩니다. 존 프로필을 목록에서 고르고, 씬에 실제 `AudioZone`이 만들어집니다.

연동 코드(`MapTool.Audio.Editor`)는 이 패키지에 들어 있지만, 오디오 패키지가 없으면 컴파일에서 빠집니다. 따로 설정할 것은 없습니다.

## 테스트 실행
패키지 테스트는 기본으로 Test Runner에 보이지 않습니다. `manifest.json`에 다음을 추가합니다.

```json
"testables": [ "com.kmj5515.maptool" ]
```

## 어셈블리 구성
| 어셈블리 | 역할 | 참조 |
|---|---|---|
| `MapTool.Core` | 격자·점유·검증·직렬화 (엔진 무관) | 없음 |
| `MapTool.Runtime` | 맵 에셋·오브젝트 정의 | 없음 |
| `MapTool.Editor` | 창·씬뷰 편집·브러시 | Core, Runtime |
| `MapTool.Audio.Editor` | 오디오 존 연동 (조건부) | Core, Editor, `Audio` |

## 라이선스
[MIT](LICENSE.md)
