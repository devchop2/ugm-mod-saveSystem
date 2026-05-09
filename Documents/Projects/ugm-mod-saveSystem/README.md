# UGM.SaveSystem

Unity용 유저 데이터 저장 모듈. **게임 코드는 손대지 않고**, 부팅 시점에 슬롯만 등록하면 한 파일로 자동 저장됩니다. 내부적으로 [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)를 써서 빠르고 작게 인코딩합니다.

```csharp
using UGM.SaveSystem;

// 게임 클래스 — 평범한 MonoBehaviour. 인터페이스 / 어트리뷰트 없음.
public class Inventory : MonoBehaviour
{
    public Dictionary<int, ItemData> Items = new();
    public int Coins;
}

// 부트 — 한 곳에서 슬롯 등록
SaveManager.Register("Inventory.Items", () => inv.Items, v => inv.Items = v);
SaveManager.Register("Inventory.Coins", () => inv.Coins, v => inv.Coins = v);

await SaveManager.LoadAsync();              // 게임 시작 시 (디폴트 파일)
await SaveManager.SaveAsync();              // 저장이 필요할 때 (디폴트 파일)
await SaveManager.SaveAsync("slot1.dat");   // 사용자 지정 파일명
```

---

## 목차

1. [무엇이 좋은가](#무엇이-좋은가)
2. [설치](#설치)
3. [빠른 시작 (3분)](#빠른-시작-3분)
4. [API 레퍼런스](#api-레퍼런스)
5. [파일 이름 / 저장 위치](#파일-이름--저장-위치)
6. [자동 저장 (AutoSaveScheduler)](#자동-저장-autosavescheduler)
7. [핵심 개념](#핵심-개념)
8. [코덱 / 저장소 교체](#코덱--저장소-교체)
9. [지원되는 데이터 타입](#지원되는-데이터-타입)
10. [IL2CPP / 모바일 빌드 주의](#il2cpp--모바일-빌드-주의)
11. [트러블슈팅](#트러블슈팅)
12. [FAQ](#faq)
13. [프로젝트 구조](#프로젝트-구조)
14. [라이선스](#라이선스)

---

## 무엇이 좋은가

- **사용자 코드 결합도 0.** `IDatabase` 같은 인터페이스 상속 없음. 어트리뷰트 없음. 자동 codegen 없음. (Mono 빌드 한정 `[MessagePackObject]` 도 불필요.)
- **3-스테이지 파이프라인.** 데이터 모음(Aggregator) → 인코딩(Codec) → 저장소(Provider) — 각각 독립 교체 가능. 압축·암호화·클라우드 추가가 한 줄.
- **MessagePack 기본.** JSON 대비 5~8배 빠르고 1/3 크기. 30초 단위 자동 저장에도 메인 스레드 hiccup 없음.
- **백그라운드 인코딩.** 인코딩 + 디스크 쓰기를 워커 스레드로 빼서 30초 자동저장 루프에서도 프레임 드랍 0.
- **dirty 슬롯 캐싱.** 변경 안 된 슬롯은 재인코딩 안 함 — 1MB 변경 / 9MB 미변경 envelope에서 1MB만 재인코딩.
- **단일 파일.** 한 번의 저장 → 한 개의 파일. `save.dat` 하나만 관리.
- **헤더 + CRC.** 매직 바이트 + 코덱 ID + CRC32 트레일러로 손상 / 잘못된 파일 자동 검출.
- **Forward-compat.** 이번 빌드에 등록 안 된 슬롯이 envelope에 있으면 그대로 보존해서 라운드트립 — 모듈/플러그인 시스템에 적합.

---

## 설치

### 옵션 A — 폴더 복사

```bash
git clone https://github.com/<owner>/ugm-mod-saveSystem.git
# 자기 Unity 프로젝트의 Assets/ 안에 UGM/SaveSystem 폴더 복사
```

### 옵션 B — UPM Git URL

`Packages/manifest.json` 에 추가:

```json
"com.chopchopgames.ugm.savesystem": "https://github.com/<owner>/ugm-mod-saveSystem.git?path=Assets/UGM/SaveSystem"
```

### 첫 임포트 시 — 의존성 자동 다운로드

처음 임포트하면 다이얼로그가 한 번 뜹니다.

> **UGM.SaveSystem — Dependencies**
> 다음 의존성이 누락되어 있습니다:
> • MessagePack 2.5.187
> • MessagePack.Annotations 2.5.187
> • Microsoft.Bcl.AsyncInterfaces 8.0.0
> • System.Threading.Tasks.Extensions 4.5.4
>
> 지금 자동으로 다운로드할까요?

승인하면 NuGet에서 받아서 다음 위치에 배치됩니다:

```
Assets/UGM/SaveSystem/Plugins/MessagePack.dll
Assets/UGM/SaveSystem/Plugins/MessagePack.Annotations.dll
Assets/UGM/SaveSystem/Plugins/Microsoft.Bcl.AsyncInterfaces.dll
Assets/UGM/SaveSystem/Plugins/System.Threading.Tasks.Extensions.dll
```

**모든 자동 생성/다운로드 파일은 `Assets/UGM/SaveSystem/` 아래에 모입니다** — 외부 폴더 오염 없음.

---

## 빠른 시작 (3분)

### 1) 게임 클래스 작성 — 평소처럼

```csharp
// Assets/MyGame/Inventory.cs
using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    public Dictionary<int, ItemData> Items = new();
    public int Coins;

    [System.NonSerialized] public float CachedWeight;  // 저장 안 할 캐시
}

public class ItemData
{
    public int Type;
    public int Id;
    public long Count;
}
```

저장 시스템 관련 코드 한 줄도 없음. `Dictionary` 그대로 써도 됩니다.

### 2) 부트 스크립트로 슬롯 등록

```csharp
// Assets/MyGame/SaveBoot.cs
using UGM.SaveSystem;
using UnityEngine;

public class SaveBoot : MonoBehaviour
{
    [SerializeField] Inventory inventory;

    async void Awake()
    {
        SaveManager.Register("Inventory.Items", () => inventory.Items, v => inventory.Items = v ?? new());
        SaveManager.Register("Inventory.Coins", () => inventory.Coins, v => inventory.Coins = v);

        var loaded = await SaveManager.LoadAsync();
        Debug.Log(loaded ? "기존 세이브 로드됨" : "첫 플레이");
    }

    [ContextMenu("Save Now")]
    public async void SaveNow() => await SaveManager.SaveAsync();

    [ContextMenu("Delete Save")]
    public async void DeleteSave() => await SaveManager.DeleteAsync();
}
```

씬에 빈 GameObject를 만들고 `SaveBoot` 컴포넌트를 붙인 뒤 `inventory` 슬롯에 게임 오브젝트를 드래그.

### 3) 자동 저장 (선택)

같은 GameObject에 **`AutoSaveScheduler`** 컴포넌트를 추가:

```
Interval Seconds : 30        ← 30초마다 자동 저장
File Name        : (blank)   ← 디폴트 사용
Save On Pause    : ✓
Save On Quit     : ✓
```

이게 전부. 게임 실행하고 인벤토리 변경 → 30초 후 자동 저장 → 재시작 → 데이터 복원.

### 동작 확인용 샘플

`Assets/Samples/SaveSystem/QuickStart/` 에 더 풍부한 예제(Inventory + PlayerStats + Vector3 RespawnPoint + List<string> Achievements)가 있어. 그대로 가져다 쓰거나 패턴만 참고.

---

## API 레퍼런스

### 진입점: `SaveManager` 정적 클래스

`SaveManager`는 정적 클래스. 인스턴스 만들 필요 없이 정적 메서드로 모든 기능에 접근.

```csharp
using UGM.SaveSystem;

SaveManager.Register("X", g, s);    // 슬롯 등록
await SaveManager.SaveAsync();        // 디폴트 파일에 저장
await SaveManager.LoadAsync();        // 디폴트 파일에서 로드
```

### Save / Load / Delete / Exists

```csharp
Task SaveAsync(string fileName = null);          // null이면 DefaultFileName 사용
Task<bool> LoadAsync(string fileName = null);     // 파일 없으면 false (예외 X)
Task DeleteAsync(string fileName = null);
Task<bool> ExistsAsync(string fileName = null);
```

### 슬롯 등록

```csharp
void Register<T>(string key, Func<T> getter, Action<T> setter);
bool Unregister(string key);
bool IsRegistered(string key);
void MarkDirty(string key);                       // dirty 캐시 무효화 (선택)
```

### 파이프라인 교체

```csharp
SaveManager.Codec      = new MessagePackCodec(...);   // 또는 new JsonCodec()
SaveManager.Provider   = new MyEncryptedProvider();
SaveManager.Aggregator = new MyAttributeAggregator();
SaveManager.DefaultFileName = "mygame.sav";
```

### 진단 프로퍼티

```csharp
int  LastLoadedFormatVersion { get; }
long LastSavedAtUnix         { get; }
bool HasLoaded               { get; }
string DefaultFileName       { get; set; }
```

### 테스트 / 핫리로드

```csharp
SaveManager.Reset();   // 모든 등록·설정 초기화. 프로덕션 코드에선 호출하지 말 것.
```

---

## 파일 이름 / 저장 위치

### 파일 이름 — 호출마다 지정 가능

```csharp
await SaveManager.SaveAsync();              // → save.dat (디폴트)
await SaveManager.SaveAsync("slot1.dat");   // → slot1.dat
await SaveManager.SaveAsync("slot2.dat");   // → slot2.dat (멀티슬롯)
await SaveManager.LoadAsync("slot1.dat");   // 같은 파일 로드
```

빈 문자열 / null이면 `DefaultFileName` ("save.dat") 이 자동 적용.

```csharp
SaveManager.DefaultFileName = "mygame.sav"; // 한 번 바꾸면 이후 호출에 적용
```

### 저장 위치

기본 `LocalFileProvider`는 `Application.persistentDataPath` 에 씁니다.

| 플랫폼 | 경로 |
|---|---|
| Windows | `%APPDATA%\..\<Company>\<Game>\save.dat` |
| macOS | `~/Library/Application Support/<Company>/<Game>/save.dat` |
| iOS | `<App>/Library/Application Support/save.dat` |
| Android | `/data/data/<package>/files/save.dat` |

다른 디렉터리에 쓰려면:

```csharp
SaveManager.Provider = new Providers.LocalFileProvider(baseDirectory: "/custom/path");
```

### 멀티 유저 / 멀티 슬롯

방법 1 — 파일 이름만 바꿔서 호출 (가장 단순):
```csharp
await SaveManager.SaveAsync($"user_{userId}.dat");
await SaveManager.SaveAsync("slot1.dat");
await SaveManager.LoadAsync("slot1.dat");
```

방법 2 — Provider의 baseDirectory를 사용자별로:
```csharp
SaveManager.Provider = new Providers.LocalFileProvider(
    baseDirectory: Path.Combine(Application.persistentDataPath, userId));
```

대부분의 게임은 방법 1만으로 충분 — 슬롯 등록 세트는 공유되고, 파일만 슬롯별로 분리.

---

## 자동 저장 (AutoSaveScheduler)

GameObject에 컴포넌트 하나 붙이면 끝. 인스펙터 옵션:

| 필드 | 의미 |
|---|---|
| Interval Seconds | 자동 저장 주기 (초). 0/음수면 타이머 OFF |
| File Name | 비우면 `SaveManager.DefaultFileName` 사용 |
| Save On Pause | 앱 일시정지 시 (모바일 백그라운드) 한 번 더 저장 |
| Save On Quit | 종료 시 (OnApplicationQuit) 한 번 더 저장 — iOS 강제종료엔 안 잡힘 |

### 이벤트 훅

```csharp
var scheduler = GetComponent<AutoSaveScheduler>();
scheduler.BeforeSave += () => Debug.Log("저장 시작");
scheduler.AfterSave  += () => Debug.Log("저장 완료");
scheduler.SaveFailed += ex => Debug.LogError($"저장 실패: {ex.Message}");
```

### 즉시 저장

```csharp
scheduler.SaveNow();   // 또는 인스펙터 컨텍스트 메뉴 "Save Now"
```

---

## 핵심 개념

### 3-스테이지 파이프라인

```
[게임 객체들]
    │  inventory.Items, stats.Level, ...
    ▼
[Stage 1] ISaveAggregator      (DefaultAggregator)
    │  슬롯 getter() 호출 → 모음
    ▼
[SaveEnvelope]   {Slots: {"Inventory.Items": <bytes>, ...}}
    │
[Stage 2] ISaveCodec           (MessagePackCodec)
    │  envelope → byte[]
    ▼
[byte[]]  [UGMS][ver][codecId][len][body...][crc32]
    │
[Stage 3] IStorageProvider     (LocalFileProvider)
    │  byte[] → 파일
    ▼
[save.dat]
```

세 인터페이스 각각이 독립 교체 가능 — 코덱만 바꿔서 JSON으로 디버그하거나, Provider만 바꿔서 클라우드로 보내거나.

### 슬롯 (Slot)

한 슬롯 = `(key, getter, setter)` 트리플.

- **key**: 저장 파일 안의 고유 ID. **출시 후 절대 바꾸지 말 것** — 옛 세이브가 슬롯을 못 찾음.
- **getter**: 저장 시 호출되어 데이터 캡처. 가벼운 참조 반환만 — 무거운 작업은 절대 X.
- **setter**: 로드 시 디코딩된 값을 받아 게임에 반영. `null` 들어올 가능성 대비.

### 모르는 슬롯 보존 (Forward-compat)

옛 빌드의 세이브에 새 빌드가 등록 안 한 슬롯이 있어도 **그대로 라운드트립**됩니다.

```
빌드 A 저장: {Inventory, Stats, Achievements}
       ↓
빌드 B 로드 (Achievements 슬롯 미등록)
       ↓
빌드 B 저장 → 여전히 {Inventory, Stats, Achievements} 그대로 보존
       ↓
빌드 A 로드 → Achievements 복원됨
```

### dirty 추적 (선택적 최적화)

```csharp
// 인벤토리에 아이템 추가 시
inv.Items[itemId] = item;
SaveManager.MarkDirty("Inventory.Items");
```

다음 SaveAsync에서 dirty 슬롯만 재인코딩, 나머지는 캐시된 byte[] 그대로 사용. 부르지 않아도 안전 (매번 전체 재인코딩, 그냥 약간 느릴 뿐).

---

## 코덱 / 저장소 교체

### 압축 (LZ4)

```csharp
using MessagePack;

var compressed = MessagePackSerializerOptions.Standard
    .WithCompression(MessagePackCompression.Lz4Block);

SaveManager.Codec = new MessagePackCodec(compressed);
```

세이브 크기 추가 30~70% 감소, 인코딩 속도는 거의 동일.

### 암호화 (Provider 데코레이터 패턴)

```csharp
public class EncryptedProvider : IStorageProvider
{
    private readonly IStorageProvider _inner;
    private readonly byte[] _key;

    public EncryptedProvider(IStorageProvider inner, byte[] key)
    {
        _inner = inner; _key = key;
    }

    public Task WriteAsync(string key, byte[] data) =>
        _inner.WriteAsync(key, Encrypt(data, _key));

    public async Task<byte[]> ReadAsync(string key) =>
        Decrypt(await _inner.ReadAsync(key), _key);

    public Task<bool> ExistsAsync(string key) => _inner.ExistsAsync(key);
    public Task DeleteAsync(string key)       => _inner.DeleteAsync(key);

    static byte[] Encrypt(byte[] data, byte[] key) { /* AES 등 */ }
    static byte[] Decrypt(byte[] data, byte[] key) { /* ... */ }
}

// 사용:
SaveManager.Provider = new EncryptedProvider(new Providers.LocalFileProvider(), aesKey);
```

### 클라우드 백엔드

`IStorageProvider` 만 구현하면 어떤 백엔드든 OK.

```csharp
public class FirebaseStorageProvider : IStorageProvider
{
    public async Task WriteAsync(string key, byte[] data)
    {
        var refPath = FirebaseStorage.DefaultInstance.GetReference($"users/{uid}/{key}");
        await refPath.PutBytesAsync(data);
    }

    public async Task<byte[]> ReadAsync(string key)
    {
        var refPath = FirebaseStorage.DefaultInstance.GetReference($"users/{uid}/{key}");
        return await refPath.GetBytesAsync(maxAllowedSize: 5 * 1024 * 1024);
    }

    public async Task<bool> ExistsAsync(string key) { /* metadata 확인 */ }
    public async Task DeleteAsync(string key)       { /* delete 호출 */ }
}

SaveManager.Provider = new FirebaseStorageProvider();
```

---

## 지원되는 데이터 타입

MessagePack-CSharp의 ContractlessStandardResolver + UnityResolver가 처리:

| 카테고리 | 지원 |
|---|---|
| 기본 | `bool`, `byte`/`sbyte`, `short`/`ushort`, `int`/`uint`, `long`/`ulong`, `float`, `double`, `string`, `decimal` |
| 열거형 | 모든 `enum` |
| 컬렉션 | `List<T>`, `T[]`, `Dictionary<K, V>`, `HashSet<T>`, `Queue<T>`, `Stack<T>` 등 BCL 컬렉션 |
| Nullable | `T?` |
| 사용자 클래스 | public 필드/속성 자동 인식 (어트리뷰트 불필요) |
| Unity 타입 | `Vector2`/`Vector3`/`Vector4`, `Quaternion`, `Color`, `Color32` |
| 시간 | `DateTime`, `DateTimeOffset`, `TimeSpan` |
| 중첩 | 위 타입의 임의 조합 |

`Dictionary<int, ItemData>`처럼 키 매칭 룰도 신경 안 써도 됩니다 — MessagePack이 내부적으로 알아서 처리.

### 직렬화 제외하기

MessagePack-CSharp의 `[IgnoreMember]` 또는 `[System.NonSerialized]` 사용:

```csharp
public class Inventory
{
    public Dictionary<int, ItemData> Items = new();   // 저장됨
    [System.NonSerialized] public float CachedWeight; // 저장 안 됨
    [MessagePack.IgnoreMember] public int RuntimeId;  // 저장 안 됨
}
```

### 저장 모양과 게임 클래스 분리 (권장 패턴)

게임 로직 클래스와 저장 데이터를 분리하면 *나중에 게임 클래스를 자유롭게 리팩터링*해도 옛 세이브가 안 깨집니다.

```csharp
// 저장 전용 POCO
public class PlayerStatsSnapshot
{
    public int Level;
    public float Hp;
    public Vector3 RespawnPoint;
}

// 게임 클래스
public class PlayerStats : MonoBehaviour
{
    public int Level = 1;
    public float Hp = 100f;
    public Vector3 RespawnPoint;
    // ... 다른 게임 로직 ...

    public PlayerStatsSnapshot ToSnapshot() => new() { Level = Level, Hp = Hp, RespawnPoint = RespawnPoint };
    public void CopyFrom(PlayerStatsSnapshot s) { Level = s.Level; Hp = s.Hp; RespawnPoint = s.RespawnPoint; }
}

// 등록
SaveManager.Register("Stats", () => stats.ToSnapshot(), s => stats.CopyFrom(s));
```

샘플 `Assets/Samples/SaveSystem/QuickStart/PlayerStats.cs` 에 이 패턴이 들어있습니다.

---

## IL2CPP / 모바일 빌드 주의

기본 `ContractlessStandardResolver`는 런타임 IL emit을 사용합니다. **IL2CPP (모바일/콘솔/WebGL) 빌드**에선 동작하지 않으므로 다음 중 하나가 필요:

### 권장 — mpc 사전 codegen

```bash
dotnet tool install messagepack-csharp --global
mpc -i ./Assets -o ./Assets/UGM/SaveSystem/Generated/MessagePackResolver.cs -n UGM.SaveSystem.Generated
```

빌드 전 한 번만 실행. 코드 변경 후엔 재실행. 자세한 내용은 MessagePack-CSharp 공식 문서.

### 대안 — 어트리뷰트 명시

저장 대상 클래스에 `[MessagePackObject(true)]` 부착 + 빌드 정의에서 StaticCompositeResolver 사용. 어트리뷰트 들어가서 결합도가 살짝 올라가지만 codegen은 불필요.

### 응급 처치 — link.xml

`Assets/link.xml` 에 reflection 보존 설정. 임시 방편이며 신뢰성 낮음.

---

## 트러블슈팅

### `error CS0234: MessagePack does not exist`
의존성 다운로드 실패. `UGM > SaveSystem > Reinstall Dependencies` 실행, 또는 [수동 의존성 설치](#수동-의존성-설치).

### `Save file has wrong magic bytes — not a UGM SaveSystem file`
대상 파일이 UGM 포맷이 아니거나 헤더가 손상됨. 백업 후 삭제 → 다음 실행에서 새로 생성.

### `Save file CRC mismatch`
디스크 손상 / 강제 종료로 파일이 망가졌습니다. 백업이 있으면 복원.

### `was written by codec id 0xXXXX, but current codec is 'messagepack' (0x0001)`
파일이 다른 코덱으로 쓰였거나, 사용자가 코덱을 바꿨다 다시 안 바꿈. 동일 코덱으로 변경 후 재시도.

### `Save slot 'X' is already registered`
같은 키로 중복 Register. 부트 코드가 두 번 실행됐거나 Awake 다중 호출. `IsRegistered("X")` 로 가드 추가.

### 큰 데이터인데 저장이 자주 끊기는 느낌
- 메인스레드에서 슬롯 getter가 무거운 작업하지 않는지 확인 (Profiler).
- `MarkDirty(key)` 로 변경된 슬롯만 재인코딩하게 표시.
- 그래도 부족하면 envelope을 분할(여러 파일)하는 게 나음.

### IL2CPP 빌드에서 `TypeInitializationException`
ContractlessStandardResolver의 IL emit이 막힌 것. [IL2CPP / 모바일 빌드 주의](#il2cpp--모바일-빌드-주의) 참조.

### 로드는 됐는데 데이터가 비어있음
- 부트 코드에서 슬롯 키 오타.
- setter 가 `v ?? new()` 같은 디폴트 처리 안 한 상태에서 null 들어옴 (옛 세이브에 슬롯 없음).
- 옛날 코덱(예: 압축 ON ↔ OFF)으로 쓴 파일 — 코덱 옵션 다시 맞추기.

---

## FAQ

**Q. 왜 FlatBuffers / JSON이 아니고 MessagePack?**
큰 데이터 + 짧은 자동저장 주기에서 *속도·크기·사용 편의*의 균형점이 MessagePack. JSON은 GC 부담 / 큰 파일이 부담스럽고, FlatBuffers는 schema 룰 / codegen 학습이 필요. MessagePack은 둘의 80%씩 가져옴.

**Q. 세이브 파일을 열어보고 싶다.**
MessagePack은 바이너리라 메모장으론 안 보임. 디버그용 `JsonCodec` 을 써서 임시로 JSON 출력으로 바꾸고 확인 → 다시 MessagePack으로 복귀하는 게 가장 편함. (JsonCodec 은 모듈에 포함 X — 필요하면 ISaveCodec 구현해서 추가, 또는 이슈로 요청.)

**Q. 매 프레임 저장해도 되나요?**
디스크 I/O 누적이 부담스러움. 게임 이벤트 단위(스테이지 클리어, 아이템 획득 등) + 30초 자동 저장 정도가 일반적.

**Q. 빌드 시 mpc codegen이 같이 빌드되나요?**
아니요. mpc 는 Editor/CI 도구이며, 출력된 `MessagePackResolver.cs` 만 빌드에 포함됩니다.

**Q. UGM 다른 모듈에서도 슬롯 등록 가능한가요?**
네 — 키에 네임스페이스 prefix를 권장 (예: `UGM.Quest.ActiveQuests`). 충돌 방지.

**Q. CI 환경에서 의존성 다운로드는?**
첫 임포트 시 NuGet 자동 다운로드가 동작합니다. 네트워크 없는 CI라면 의존성 결과 캐싱 (DLL + .version 사이드카) 후 빌드 전에 복사.

**Q. 멀티 SaveManager 인스턴스가 필요한 경우는?**
거의 없음. 멀티 슬롯은 파일명만 다르게 호출하면 충분. 정말로 *완전히 다른 등록 세트*가 필요한 경우엔 ISaveAggregator + ISaveCodec + IStorageProvider 를 직접 조합하면 됨.

---

## 프로젝트 구조

```
Assets/UGM/SaveSystem/
├── Runtime/
│   ├── Core/
│   │   ├── SaveManager.cs              ← 진입점 (정적 클래스, 사용자가 호출하는 모든 API)
│   │   ├── SaveEnvelope.cs             ← 중간 데이터 컨테이너
│   │   ├── ISaveAggregator.cs          ← Stage 1 인터페이스
│   │   ├── ISaveCodec.cs               ← Stage 2 인터페이스
│   │   ├── IStorageProvider.cs         ← Stage 3 인터페이스
│   │   ├── AutoSaveScheduler.cs        ← 인터벌 자동저장 컴포넌트
│   │   └── NoSaveAttribute.cs
│   ├── Aggregator/
│   │   └── DefaultAggregator.cs        ← 슬롯 등록 + dirty 캐싱
│   ├── Codec/
│   │   ├── MessagePackCodec.cs         ← 디폴트 코덱
│   │   └── SaveFileHeader.cs           ← 매직/CRC 헤더 유틸
│   ├── Resolvers/
│   │   └── UnityResolver.cs            ← Vector3 등 Unity 타입
│   ├── Providers/
│   │   └── LocalFileProvider.cs        ← persistentDataPath 저장
│   └── UGM.SaveSystem.asmdef
├── Editor/
│   ├── Dependencies/
│   │   ├── DependencyInstaller.cs      ← 첫 임포트 시 NuGet 다운로드
│   │   └── DependencyManifest.cs
│   └── UGM.SaveSystem.Editor.asmdef
├── Plugins/                             ← MessagePack DLL 자동 다운로드 위치
└── dependencies.json                    ← 의존성 매니페스트

Assets/Samples/SaveSystem/
└── QuickStart/                          ← 그대로 가져다 써도 되는 예제
    ├── Inventory.cs
    ├── PlayerStats.cs
    ├── ItemData.cs
    └── SaveBoot.cs
```

### 수동 의존성 설치

오프라인 환경에서 자동 다운로드가 안 될 때:

1. NuGet에서 직접 다운로드: https://www.nuget.org/packages/MessagePack/ → `.nupkg` 파일 안의 `lib/netstandard2.0/MessagePack.dll`
2. `Assets/UGM/SaveSystem/Plugins/` 에 4개 DLL 모두 배치
3. 각 DLL 옆에 `<filename>.version` 파일 생성, 매니페스트의 version 문자열 그대로 적기 (예: `2.5.187`)

세 번째 단계가 자동 재설치를 막는 표시입니다.

---

## 라이선스

본 모듈: MIT License (자세한 내용은 LICENSE 파일).

서드파티:
- **MessagePack-CSharp** (MIT) — https://github.com/MessagePack-CSharp/MessagePack-CSharp

자동 다운로드되는 바이너리는 각 라이선스를 따릅니다.
