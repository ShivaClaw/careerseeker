# `BuildSyncBridge` — the composition-root decision

**Decided:** 2026-08-15 (thirty-eighth run, Linux sandbox) · **Agent:** Claude (android + engine-sync track)
**Question asked:** `STATE.md` ordered-intent item 3, carried unchanged for five revisions —
*"`BuildSyncBridge` has still never executed anywhere, and CI cannot execute it either. Do not extract
a further seam without first deciding whether that is worth it — at some point a composition root is
a composition root. Write that decision down either way."*

Every claim below has a re-verification command in the android repo's `AUDIT-REQUEST.md`
(**C-CR-1 … C-CR-11**). Nothing here was compiled: this session has no .NET, and no gate ran.

---

## 0. The decision, first

**`BuildSyncBridge` is declared a composition root. No further seam is to be cut *for the sake of the
argument identities*.** Extraction cannot retire them; it relocates them. The one identity already
retired was retired by the **type system**, not by a seam, and that is the lever the remaining two
worth retiring should use.

**But the method is not thereby declared closed, because the identities are not the whole residue.**
The premise the item inherited — that what remains is "three argument identities" — is **too small by
seven behaviours** (§3). Those seven *are* extractable-to-testable, and none of them is an identity.
So the answer to "is a further seam worth it?" is **no for the composition, yes-in-principle for the
report** — and the item could not have reached that split, because it was reasoning about the wrong
residue.

Neither half is implementable from this session: both change shipping C# and need the full local
`Verify-Alpha.ps1 -IncludePublish -IncludePackage`. What follows is the decision and its evidence, not
a change.

---

## 1. What is actually in the method

`src/Engine/Program.cs:256-323`, byte-identical at the heads of **#46** (`claude/s6-counter-reconciliation`)
and **#47** (`claude/s2-push-disposition`) — #47 does not touch `Program.cs` (**C-CR-1**). Comments
stripped, the body is eighteen statements:

| Lines | Statement | Kind |
| --- | --- | --- |
| 259-260 | `if (!enabled) return null;` | behaviour |
| 262-263 | construct `SyncPairingVault(SyncVaultPath())`, `Load()` | identity + behaviour |
| 264-269 | vault-absent disposition: two operator lines, `return null` | **behaviour** |
| 274 | `new HttpClient { Timeout = 20s }` | **behaviour** (the value) |
| 275 | `new RelayClient(http, paired.RelayUrl, paired.Pairing)` | identity |
| 286 | `relay.PullAsync(paired.RelayToken, "e2p", since: 0)` | **behaviour** (the arguments) |
| 287 | `SyncPublisher.ResumeSeq(paired.LastE2pSeq, relayAnswer)` | call site of a covered rule |
| 288-295 | the reconciliation **report**: two-way branch, two messages | **behaviour** |
| 310-317 | `SyncPushPath.Create(...)` — 7 arguments, 4 named | **the identities** |
| 319 | `Sync: publishing to …` banner | **behaviour** |
| 320-322 | `new EngineSyncBridge(…, inbound: BuildInboundPump(…))` | identity |

**None of it has ever executed.** The method returns null without a DPAPI-backed pairing vault, which
exists only on the owner's Windows machine, so no harness and no CI runner has ever entered it past
line 263. That is the standing condition, not a finding.

---

## 2. Why extraction cannot retire the identities

The four named arguments at `310-317` are the identities: that `push` closes over *this pairing's*
relay token, that `seqStore` receives *the vault*, that `log` reaches the operator, that `startSeq`
receives *§6.1's reconciled value* rather than `paired.LastE2pSeq` or `0`.

A test of an extracted function **supplies its own arguments**. It can prove the function uses what it
was given; it cannot prove the production caller gave it the right thing. So extracting
`BuildSyncBridgeCore(vault, relay, log, resumeSeq, …)` would move the question from *"does
`SyncPushPath.Create` get the right token"* to *"does `BuildSyncBridgeCore` get the right token"* — the
same question, one frame out.

**This is not quite a shell game, and the record proves it.** Extraction *has* reduced the residue, in
two measured steps (**C-CR-6**):

| Commit | What moved out | Residue after |
| --- | --- | --- |
| `dee32f8` | the sink's **decision rule** → `RelaySink.Create` | five delegate bodies |
| `0d369eb` | the **wiring** → `SyncPushPath.Create` | four argument identities |
| *(no commit — a type)* | `SyncPairingVault : IE2pSeqStore` | **three** |

Extraction reduces when it lets the new function **derive** internally what the old call site passed
explicitly. It converges to a floor of **one** — the root's choice of the real vault, which no test can
ever make, because a test that supplied the real DPAPI vault would not be a test. The remaining
distance is 3 → 1, bought with a new public type in `src/Sync`, new harness scaffolding, and one more
frame of indirection on a startup path.

**The decisive step was not an extraction.** 4 → 3 came from `SyncPairingVault` implementing
`IE2pSeqStore`: `seqStore: vault` now compiles *only* because the interface is there, so deleting it is
a build error rather than a silent no-op — recorded as mutation **M8** in the android repo's `LOG.md`
(**C-CR-3**; cited, not re-measured — this session cannot compile). One type retired one identity at
zero structural cost. That is the better lever, and §5 spends it.

---

## 3. The finding: the residue was under-counted, and the qualifier that went missing

`src/Sync/SyncPushPath.cs`'s header states the reduction **correctly and with its scope attached**:

> shrink the unexecuted remainder from *five delegate bodies* … to *four argument identities* **at a
> single call site**

`src/Engine/Program.cs:301-302` restates it **with the qualifier dropped**:

> What stays **here** is only the four argument identities

Read plainly, *"here"* is `BuildSyncBridge`, and the sentence is false: what stays in the method is the
identities **plus the seven behaviours** marked in §1's table. `STATE.md` item 3 then inherited the
unqualified reading and framed the whole seam question around three identities (**C-CR-7**).

The seven are not hypothetical dead weight. Each is unexecuted **and** unasserted — the five operator
strings in the method appear exactly once in `src/` and **zero times in `tests/`** (**C-CR-4**):

- **`286`, the pull's arguments, is the sharpest.** `RelayClient.PullAsync(string bearer, string dir,
  long since, …)` takes an unconstrained `string` for the direction, and the relay computes
  `latest` as `SELECT MAX(seq) … WHERE dir = ?` (`relay/src/channel.ts:206-208`). So `"e2p"` → `"p2e"`
  **compiles, passes every test, and is accepted by the relay** — the engine would reconcile its
  outbound counter against the *inbound* direction's high-water mark (**C-CR-8**). The consequence is
  bounded rather than fatal, and is stated as such: §6 (`Sync-Protocol.md:568-570`) makes gaps
  legitimate and forbids them stalling the stream, so an over-advanced counter costs a spurious
  snapshot request, not a stall (**C-CR-9**).
- **`288-295`, the reconciliation report, is the one with a branch.** Its `else` exists so that an
  operator reading a later replay refusal learns the reconciliation never ran — the comment says
  "Named, not swallowed." Deleting the branch loses that and fails nothing.
- **`287` is the exact shape the record already knows.** `SyncPublisher.ResumeSeq` carries **12**
  harness assertions on its rule and has **one** production call site, which no test reaches
  (**C-CR-5**). The rule was extracted precisely because the composition could not be; the call site
  is the part that stayed behind.

---

## 4. Decision, in three parts

1. **The composition is a composition root. Stop extracting it.** The available reduction is 3 → 1, the
   floor is structural, and the cost is a public type plus scaffolding on a path that still cannot
   execute. Not worth it. *(This retires item 3 as a decision, per its own terms.)*
2. **Retire two of the three identities with types, not seams** — §5. Precedent: M8.
3. **If any further seam is ever cut here, it targets the report and the pull arguments (§3), not the
   composition.** Recorded so a future session does not re-open part 1 having only read the item's
   framing.

---

## 5. What is proposed instead — the type lever

Ordered by value. **All three need the full local gate; none is a cloud-session change.** They alter
shipping signatures on the engine's startup path, and a build this session cannot run is not evidence.

| # | Identity | Proposal | Why |
| --- | --- | --- | --- |
| 1 | `startSeq: resumeSeq` | `SyncPublisher.ResumeSeq` returns a `readonly record struct ResumeSeq(long)`; `SyncPushPath.Create` accepts it | Highest risk of the three. `startSeq`, `paired.LastE2pSeq` and `0` are all `long`, so the wrong one compiles — and a wrong resume value is exactly the 409-on-recovery-snapshot failure §6.1 exists to prevent. A distinct type makes the substitution a build error. |
| 2 | the direction string at `286` | a `Direction` enum or wrapper on `PullAsync`'s `dir` | Closes **C-CR-8** — the swap that compiles, tests clean, and reconciles against the wrong mark. Cheapest of the three: `dir` has few call sites. |
| 3 | `log: Console.WriteLine` | **nothing** | A misrouted log is an observability defect, not a correctness one. Not worth a type. Left conventional, deliberately. |

The fourth, `seqStore: vault`, is already done — that is M8.

---

## 6. What this session did not and could not verify

- **No build, no test run, no gate.** No .NET on this host; `Verify-Alpha.ps1` needs Windows. The M8
  build-error claim (§2) is **cited from the android `LOG.md`, not re-measured**. Every §5 proposal is
  **unverified by construction** and labelled so.
- **`BuildSyncBridge` still has never executed**, here or anywhere. This document does not change that
  and does not claim to. Executing it needs a real pairing vault and a relay — B-2's territory, and
  the owner's machine.
- **This branch is documentation only**: one new file, no C#, no vectors, no `Verify-Alpha.ps1` edit.
  The offline pin stays **793** and no count-reporting doc moves, so the drift trap is not engaged
  (**C-CR-10**). `node docs/sync-vectors/generate.mjs --check` prints `OK: 29 vector files match the
  generator.` — no vector byte moved, so the android vendored pin `679a317` is untouched and **no
  cross-repo drift event is possible from this change** (**C-CR-11**).
