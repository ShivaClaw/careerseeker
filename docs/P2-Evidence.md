# P2 evidence — engine snapshot/delta/heartbeat publisher (offline first)

**Captured:** 2026-07-23. Everything here is "ran it and saw it", per the evidence standard.
This covers the **offline, no-gate** P2 work item (P2-Runbook.md §2.2): the engine-side
`SyncPublisher` and its host wiring. The device-bound pieces (phone pairing UI, desktop
`/pair` page, the live-tick + airplane-mode exit proof) remain for the device-equipped
session and are **not** claimed here.

## 1. What landed

- **`src/Sync/SyncPublisher.cs`** — seals `SyncPayloads` (snapshot/delta/heartbeat) with
  `k_e2p` into v1 wire envelopes, assigns the monotonic e2p `seq` the relay enforces, and
  pushes through an injected sink. Transport/clock/nonce are injectable so the
  sealing+sequencing is verifiable offline. A failed push **burns** the seq (a legitimate
  gap) rather than reusing it. e2p envelopes carry no `sig`, matching the receiver's rule.
- **`src/Engine/EngineSyncBridge.cs`** — projects live `EngineCounters` plus the recent
  application/job summaries the local dashboard already renders into the sync record types
  and drives the publisher: snapshot first, deltas thereafter, plus a counters-only
  heartbeat. It holds no key material. The projection carries only structured fields
  (state/company/title/score/flags), so a raw posting body structurally cannot reach the
  phone (untrusted-text rule).
- **Host wiring** — `EngineHost` gains an optional bridge; with none (the default, sync
  off) the tick is exactly `cycle.TickAsync`. `Program.cs` adds `--sync` (**default OFF**):
  publishing needs a completed pairing, which is device-bound, so `--sync` is honored today
  but no-ops with an explicit note — a clean seam where the `RelayClient`-backed sink gets
  built once the pairing vault exists.

## 2. Offline suite (`scripts/Verify-Alpha.ps1`)

The whole hermetic offline suite, run locally. New coverage: `SyncHarness` 74→88 (publisher
seal/sequence/gap-on-failure), `EngineHarness` 89→99 (the bridge against a real publisher +
fake sink). Pinned total moved 401→425 in lockstep with every count-bearing doc (the drift
trap).

```
=== 88 passed, 0 failed ===          # SyncHarness

=== Offline total: 425 passed, 0 failed ===

CareerSeeker alpha verification complete.
```

`EngineHarness` §"P2 sync bridge" and `SyncHarness` §"SyncPublisher" both include the
load-bearing assertion that a published dashboard payload carries **no** raw posting body.

## 3. Live snapshot/delta round-trip (`tests/SyncLiveSmoke`)

The P2 §2.2 acceptance, live against the deployed relay: the shipping `SyncPublisher` pushes
a snapshot and a delta through `https://relay.careerseeker.app`, and a **simulated phone**
(same `src/Sync` primitives a device would use) pulls both, opens them under `k_e2p`, and
reconstructs the dashboard counters. This extends the P1 live smoke (17 → 22 passes); see
[P1-Evidence.md](P1-Evidence.md) for the pairing/signature/replay coverage it builds on.

```
dotnet run --project tests/SyncLiveSmoke -c Release -- https://relay.careerseeker.app
```

Output, 2026-07-23 (new P2 section shown; full run is 22/22):

```
[ SyncPublisher snapshot + delta round-trip ]
  PASS  publisher pushes a snapshot (seq 2)
  PASS  publisher pushes a delta (seq 3)
  PASS  phone pulls the two new e2p envelopes
  PASS  phone opens snapshot then delta in order
  PASS  phone reconstructs the dashboard counters from the payload
...
=== 22 passed, 0 failed ===
```

Every blob on the wire is ciphertext; the relay stays blind (proven in
[P1-Evidence.md](P1-Evidence.md) §3). The pairing is created and then **unpaired** (Durable
Object purged) at the end of the run.

## 4. What remains for P2 (not claimed here)

- **Android**: Room replica + envelope applier in `:app`, demo-mode fixture, and the five
  read-only Compose screens from fixtures (P2-Runbook.md §2.3–§2.5). Offline, CI-verifiable;
  next up.
- **Device-bound finale**: phone pairing UI (CameraX + ML Kit QR, ECDSA P-256 key in the
  Keystore), desktop `/pair` page, cert pinning, and the exit proof — a demo cycle ticking
  on the phone in near-real time, then an airplane-mode replica read. Needs a handset and
  the three open P2 gates answered (P2-Runbook.md §4).
