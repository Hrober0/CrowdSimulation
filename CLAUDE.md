# CaveDefender – Project Notes

## Coding Rules

**Do not introduce global state.** No `static` mutable fields, and no class fields used only as a scratch buffer for one method. Return a new list/collection instead – the allocation is cheaper than the hidden coupling.

```csharp
// bad – static buffer shared by every caller, plus a field only Filter() cares about
private static readonly List<(int Index, int Score)> _matches = new();
public static void Filter<T>(IReadOnlyList<T> items, string pattern, List<T> result)

// good – everything the method needs is an argument, the result is returned
public static List<T> Filter<T>(IReadOnlyList<T> items, string pattern)
```

State that must survive between calls belongs to an instance that owns it (a module, a UI menu), never to a `static` field.

**Keep specific logic out of general scripts.** A general script exposes a mechanism, the specific rule that uses it lives in the script it belongs to and is connected through the `EventBus`.

**Do not add new assembly references.** The `.asmdef` graph is the code organization – if something does not compile because of a missing reference, that is a hint the logic sits in the wrong place. Move the logic or add a proxy (see above) instead. If a new reference really is the only option, ask first.

## EventBus (`HCore.EventBus`)

Static singleton in `Assets/Scripts/HCore/Events/EventBus.cs`.

**Two handler types:**
- `IEventHandler` – many-to-many. Register with `EventBus.RegisterHandler<IMyInterface>(this)`, unregister with `EventBus.UnregisterHandler`. Fire with `EventBus.Invoke<IMyInterface>(e => e.Method())`.
- `ISingleEventHandler` – only one handler allowed globally. `RegisterSingleHandler` / `UnregisterSingleHandler`. Same `Invoke` call.

Handlers must be registered in `Initialize`/`Awake` and unregistered in `Deinitialize`/`OnDestroy`.
