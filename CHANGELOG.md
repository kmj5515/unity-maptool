# Changelog

## [1.1.0] - 2026-11-26
### Added
- 빌드 게이트: 빌드 전에 모든 맵을 검증해 오류가 있으면 빌드 실패. 라벨 `MapTool-SkipBuildCheck`로 제외 (`Tools > Map Tool > 모든 맵 검사`).

## [1.0.1] - 2026-11-23
### Changed
- 팔레트 타일이 창 가로 폭을 꽉 채우도록 변경. 최소 72px 기준으로 열 수를 정하고 남는 폭은 타일 크기를 키워 채운다.
  스크롤 뷰 안의 실제 폭을 재서(세로 스크롤바 유무 반영) 타일 위치를 직접 계산한다.

## [1.0.0] - 2026-11-19
### Added
- 첫 공개 버전. HelloWorld 프로젝트의 `Assets/MapTool`을 UPM 패키지로 분리.
- 배치·장식 브러시·존·스폰·검증과 빠른 수정·JSON 내보내기/가져오기.
- 샘플 오브젝트 생성기 (`Tools > Map Tool > 샘플 오브젝트 생성`, `Assets/MapToolSamples`에 생성).
- `com.kmj5515.audio`가 설치돼 있을 때만 컴파일되는 오디오 존 연동 (`MapTool.Audio.Editor`).
