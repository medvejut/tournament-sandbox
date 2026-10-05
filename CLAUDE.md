# Tournament Sandbox

A 1v1 Speed Bingo tournament: a Unity 6 client (`client/`) and a Node/TypeScript server
(`server/`). [README.md](README.md) has how to run it.

The implementations in `client/Assets/Scripts/Net` (`ApiClient`, `RetryPolicy`, `ServerClock`,
`PendingSubmissionStore`) are written by hand: review them and suggest changes, but edit them only
when asked.

## Client code style (C#)

Applies to `client/Assets/Scripts` and `client/Assets/Tests`. Assets outside them come from
packages or the project template.

### Structure and naming

- Explicit access modifiers, `private` included.
- The namespace is the assembly's (`TournamentSandbox.Net`), also in its subfolders: a subfolder
  (`Net/Api`, `UI/Match`) groups files and adds no namespace segment. Tests live in
  `TournamentSandbox.Tests`. No assembly folder is named after a Unity or .NET class that code calls
  through its type name (`Physics`, `Input`, `Animation`, `Math`): inside `TournamentSandbox.*`
  the namespace would shadow it.
- One top-level type per file, named after it. The exception is a file of wire DTOs, which keeps
  the DTOs of one API together with the string constants for their fields.
- A class name says what the class is for. Components carry their feature as a prefix, since Add
  Component shows the class name alone; plain C# classes do not. Suffixes name the role:
  `*View`, `*Presenter`, `*Settings`, `*State`, `*Store`, `*Policy`, `*Service`, `*EntryPoint`,
  `*Request`/`*Response`.
- Constants and `static readonly` fields are PascalCase.
- Serialized fields are `[SerializeField] private camelCase`, with `[SerializeField]` on the same
  line as the field; other private fields are `_camelCase`. No public fields, except in settings
  ScriptableObjects - there fields are `public camelCase` and read directly, and a private field
  with a getter is kept only where the value is derived before use - and in `[Serializable]` DTOs
  for `JsonUtility`, whose public camelCase fields are named exactly like the JSON keys.
- Variables and lambda parameters are named like any variable (`entry`, `keyboard`), never
  shortened to a letter or an abbreviation. Exceptions: loop indices (`i`, `j`), coordinates
  (`x`, `y`), the frame delta `dt`, and names fixed by a wire format.
- Seal classes that are not designed for inheritance.
- Test methods are named `Subject_Condition_Outcome`
  (`TryDaub_BeforeTheCall_ReturnsNotCalledYet`); the condition may be dropped when there is none.

### Components and serialized references

- A component on the same GameObject is fetched in `Awake`, not serialized: a required one with
  `[RequireComponent]` and `GetComponent`, an optional one with `TryGetComponent` into a
  `[CanBeNull]` field. Serialized references are for other objects - children, parents, scene
  objects, assets - and for picking one of several components of the same type.
- A reference the component cannot work without is not checked at runtime: the
  NullReferenceException on first use is the fail-fast. An optional serialized one says in its
  tooltip what happens when it is left empty ("Left empty, ...") and is null-checked.
- Other attributes go on the lines above `[SerializeField]`, in this order: `[Header]` and `[Space]`, each
  on its own line; `[Tooltip]` on its own line; then the rest, where short ones may share a line
  (`[Range(0f, 1f), Min(0f)]`).
- Tooltips on serialized fields only where the name, type and range leave something out - a
  relative or unusual unit, what zero or an empty reference means, what may be assigned, a
  trade-off in tuning. A tooltip that restates the field name is noise in the Inspector.

### Statements

- Braces around every block, including single-statement `if`s and loops. The one exception is a
  guard clause - an `if` whose only statement leaves the method or loop (`return`, `continue`,
  `break`, `throw`): no braces, the statement indented on the next line, so the main flow reads
  down the left edge.
- `var` for local variables. Spell the type out only where the reader cannot tell what a call
  returns and it matters, or where a different type is wanted (`float ratio = 1;`).
- LINQ for simple transformations and filters, when the lambda has no branching or is a named
  method (`entries.Where(entry => !entry.IsSettled).ToArray()`). Not on hot paths - per frame -
  where its allocations add up; plain loops there.
- Systems communicate through plain C# events; ScriptableObjects hold data and settings only.

### Async

- Async methods return `UniTask`/`UniTask<T>`, end in `Async`, and take a `CancellationToken` as
  their last parameter. No `async void`; fire-and-forget goes through `.Forget()`.
- Cancellation propagates as `OperationCanceledException`, never wrapped and never logged as an
  error. A MonoBehaviour cancels its work with `GetCancellationTokenOnDestroy()`.

### Comments

- Comments explain non-obvious behaviour that the code cannot show on its own - an API's hidden
  constraint, the reason an order of operations matters. Prefer code that needs none: a named
  method or variable beats a comment restating what the next line does. No XML doc comments
  (`///`): what a member does belongs in its name, and whatever is not obvious goes in a `//`
  comment where it matters. A comment above a class only states a constraint the code cannot
  show, such as a cache never being invalidated from a background thread.
- Before adding a comment, try the code without it: if a reader who knows Unity follows the code
  anyway, leave it out. No comments that weigh the chosen design against alternatives (that goes
  in the commit message), that restate what a type guarantees (a set holds each item once), or
  that explain logic a clearer structure or name would make obvious - restructure instead.
- A comment is one line, two at most, and says how or why, never what: what a class or method is
  for belongs in its name. Needing more is a sign the name or the structure should change. A
  contract with something outside the code (a wire format, a cross-language port) is one line at
  the top of the file.
- Most members need no comment, and most files have none or one or two. No headers on who wrote a
  file, no comments retelling the design or the project's history, no "we'll" or "note that":
  the code should read as written by a developer, not generated.

## Server

- Zero runtime dependencies. Node runs the `.ts` files directly (type stripping), so only
  erasable syntax: no `enum`, no parameter properties, no namespaces; relative imports end in
  `.ts`. `npm test` (node --test), `npm run typecheck` (tsc --noEmit).
- All state is in memory: a restart wipes players and matches.
- Chaos (`/v1/dev/chaos` or `CHAOS_*` env) applies to non-dev `/v1` routes: `fail` answers 503
  before handling, `delay` waits up to `delayMs`, `drop` handles the request and then destroys the
  socket, which is the "submit succeeded, response lost" case.
- `npm run second-player` is a test harness for the second seat; never describe it as an
  opponent for real players. It joins the oldest open room of another player.

## Rules shared by client and server

- Game rules - timings, points, penalties - are the `Rules` class in `Core/Rules.cs` and must
  match `server/src/game.ts`, which replays every match. `spec/vectors.json` pins both sides; the
  Node and EditMode tests assert against it. `npm run vectors` regenerates it after a deliberate
  change.
- Enter is idempotent by the `Idempotency-Key` header: one key per tap on Play, reused by every
  retry. Submit is idempotent by a hash of the moves, so a resubmit must send the saved log as is.
- The server rejects a log whose last move is later than its own time since the match started
  (plus `CLOCK_TOLERANCE_MS`). The client's `t` counts from the enter response plus a countdown,
  so it always stays inside.

## Client architecture

- `GameLifetimeScope` (VContainer) is the only place that knows the container; Game, Net and UI
  take dependencies through constructors. VContainer logs anything an async entry point throws,
  cancellation included, so entry points end quietly on cancellation.
- `OnlineMatchService` is the only user of `IPendingSubmissionStore`: it saves on enter and on
  every move, submits the saved copy and clears it once the server's answer is final.
- Logins go through `OnlineMatchService.EnsureLoggedInAsync`: every login rotates the token, so
  concurrent logins would leave a stale one.
- `GetTimeAsync` makes one attempt, as a backoff would spoil the RTT sample; a sync is retried as
  a whole (`EnsureClockSyncedAsync`).
- Testing `drop` chaos in the Editor needs Error Pause off: UnityWebRequest logs a dropped socket
  as "Curl error 52" and pauses Play mode.
