using System.Runtime.CompilerServices;

// 저장 데이터가 깨진 상황(겹침·중복 ID 등)을 테스트에서 재현하려고 MapData의 internal 변경 메서드를 연다.
[assembly: InternalsVisibleTo("MapTool.Core.Tests")]
[assembly: InternalsVisibleTo("MapTool.Editor.Tests")]
