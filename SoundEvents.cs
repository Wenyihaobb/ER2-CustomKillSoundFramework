using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CustomKillSoundFramework
{
    /// <summary>
    /// One feedback sound per attack batch.
    ///
    /// A batch is opened ONLY by DamageFeedback.OnSoldierHit where the shooter is
    /// the local player and the target is an enemy soldier. Every other signal
    /// (head hits, kill statistics, soldier deaths) can only MODIFY an open batch;
    /// the sole exception is the standalone crew-kill case, which requires BOTH a
    /// player kill statistic AND a confirmed enemy soldier death.
    /// That is what keeps enemy->player, ally->enemy and enemy->ally silent.
    ///
    /// Within a batch every input is OR-accumulated instead of overwriting the
    /// previous one, so a volley that kills 2 of 10 soldiers is a KILL, never a hit.
    /// Priority: headshot (head + kill) > kill > hit. One batch = one sound.
    ///
    /// All debug output is guarded by "if (Diag.Enabled)" so a disabled switch
    /// costs nothing on the hit / kill / per-frame hot paths.
    /// </summary>
    internal static class SoundEvents
    {
        private const double BatchGap = 0.050;          // events closer than this join the same batch
        private const double SettleDelay = 0.016;       // single hit: resolve after ~1 frame
        private const double VolleySettleDelay = 0.040; // multi-target volley: wait for death rollup
        private const double MaxBatchAge = 0.150;       // hard cap so a hit stream never stalls
        private const double TailSuppress = 0.025;      // trailing frames of one volley stay silent (< BatchGap)
        private const double FeedbackMinGap = 0.025;    // gap after a single-target shot
        private const double VolleyMinGap = 0.075;      // longer gap after a multi-target volley
        private const double HeadWindow = 0.20;         // strong head-signal lifetime
        private const double WeakHeadWindow = 0.05;     // ptr-less helmet statistic
        private const double KillEvidenceWindow = 0.20; // OnPlayerGetKill + enemy death
        private const double EvidenceMinDelay = 0.030;

        private static readonly double TickToSeconds = 1.0 / Stopwatch.Frequency;

        private static bool _batchOpen;
        private static double _batchStart;
        private static double _batchLastEvent;
        private static int _batchHits;
        private static bool _batchAnyKill;
        private static readonly HashSet<long> _batchTargets = new HashSet<long>();
        private static readonly Dictionary<long, double> _headPtrs = new Dictionary<long, double>();
        private static readonly Dictionary<long, double> _deathPtrs = new Dictionary<long, double>();
        private static readonly List<long> _pruneScratch = new List<long>();
        private static double _weakHeadTime = -100.0;

        private static double _playerKillStatTime = -100.0;
        private static double _lastEnemyDeathTime = -100.0;
        private static bool _standalonePending;
        private static double _standaloneTime = -100.0;

        private static double _lastFeedbackTime = -100.0;
        private static bool _lastFeedbackWasVolley;
        private static double _lastBatchResolveTime = -100.0;

        private static double Now
        {
            get { return Stopwatch.GetTimestamp() * TickToSeconds; }
        }

        /// <summary>Clear per-battle state (called when a battle loads).</summary>
        internal static void Reset()
        {
            _batchOpen = false;
            _batchHits = 0;
            _batchAnyKill = false;
            _batchTargets.Clear();
            ClearHead();
            _deathPtrs.Clear();
            _playerKillStatTime = -100.0;
            _lastEnemyDeathTime = -100.0;
            _standalonePending = false;
            _lastFeedbackTime = -100.0;
            _lastFeedbackWasVolley = false;
            _lastBatchResolveTime = -100.0;
            SoundBank.Invalidate(); // re-scan the Sounds folder on the next play
            Diag.Reset();
        }

        // ------------------------------------------------------------------
        // Inputs. OnLocalSoldierHit already passed local-player + enemy checks.
        // ------------------------------------------------------------------

        /// <summary>The local player hit an enemy soldier. Opens / extends a batch.</summary>
        internal static void OnLocalSoldierHit(long targetPtr, bool targetDead)
        {
            double now = Now;

            // Damage that arrives just after a batch was resolved belongs to the
            // same volley (explosion damage is spread over a few frames). Swallow
            // it instead of opening a second batch / playing a second sound.
            if (!_batchOpen && now - _lastFeedbackTime <= TailSuppress)
            {
                if (Diag.Enabled)
                    Diag.Log("HIT ignored: trailing damage of an already-resolved volley (ptr=" +
                             targetPtr + ")");
                return;
            }

            if (_batchOpen && now - _batchLastEvent > BatchGap)
                Resolve(now); // previous batch is done; this hit opens a new one

            if (!_batchOpen)
            {
                _batchOpen = true;
                _batchStart = now;
                _batchHits = 0;
                _batchAnyKill = false;
                _batchTargets.Clear();
            }

            _batchLastEvent = now;
            _batchHits++;
            if (targetPtr != 0L)
                _batchTargets.Add(targetPtr);

            // OR-accumulate: one dead soldier among many is still a kill.
            if (targetDead)
                _batchAnyKill = true;
            double deadAt;
            if (targetPtr != 0L && _deathPtrs.TryGetValue(targetPtr, out deadAt) &&
                now - deadAt <= KillEvidenceWindow)
                _batchAnyKill = true;
            if (now - _playerKillStatTime <= KillEvidenceWindow)
                _batchAnyKill = true;

            if (Diag.Enabled)
                Diag.Log("HIT #" + _batchHits + " ptr=" + targetPtr +
                         " targetDead=" + targetDead + " anyKill=" + _batchAnyKill +
                         " (local player -> enemy soldier)");

            Tick();
        }

        /// <summary>Head hit with a known damaged unit pointer.</summary>
        internal static void OnHeadHit(long unitPtr)
        {
            double now = Now;
            if (unitPtr != 0L)
            {
                _headPtrs[unitPtr] = now;
                Prune(_headPtrs, now, HeadWindow + 0.1);
            }
            if (Diag.Enabled)
                Diag.Log("HEAD accepted ptr=" + unitPtr +
                         " inBatchTarget=" + (_batchOpen && _batchTargets.Contains(unitPtr)));
        }

        /// <summary>Helmet statistic without a unit reference: short weak signal.</summary>
        internal static void OnWeakHeadHit()
        {
            _weakHeadTime = Now;
            if (Diag.Enabled)
                Diag.Log("HEAD accepted (weak helmet statistic, no unit pointer, 50 ms window)");
        }

        /// <summary>
        /// Player kill statistic. Never plays by itself: upgrades an open batch,
        /// or pairs with a confirmed enemy soldier death for the standalone case
        /// (crew killed inside a destroyed vehicle, where OnSoldierHit may not fire).
        /// </summary>
        internal static void OnKillSignal()
        {
            double now = Now;
            _playerKillStatTime = now;

            if (_batchOpen && now - _batchLastEvent <= BatchGap * 2)
            {
                // Normal soldier kill: the batch owns it. No standalone replay.
                _batchLastEvent = now;
                _batchAnyKill = true;
                _standalonePending = false;
                if (Diag.Enabled)
                    Diag.Log("KILL statistic -> upgrades the open batch to a kill");
            }
            else if (!_batchOpen && now - _lastBatchResolveTime > KillEvidenceWindow)
            {
                // No player hit at all nearby -> this is the crew-inside-vehicle
                // case, where OnSoldierHit never fired. Still requires a
                // confirmed enemy soldier death before any sound is played.
                _standalonePending = true;
                _standaloneTime = now;
                if (Diag.Enabled)
                    Diag.Log("KILL statistic -> standalone candidate (needs an enemy soldier death)");
            }
            else if (Diag.Enabled)
            {
                Diag.Log("KILL statistic ignored: a batch just handled this kill");
            }
            Tick();
        }

        /// <summary>A confirmed enemy soldier death (Soldier.Kill / KillSynched).</summary>
        internal static void OnEnemyDeath(long unitPtr)
        {
            double now = Now;
            _deathPtrs[unitPtr] = now;
            _lastEnemyDeathTime = now;
            Prune(_deathPtrs, now, KillEvidenceWindow + 0.1);

            bool matched = _batchOpen && _batchTargets.Contains(unitPtr) &&
                           now - _batchLastEvent <= BatchGap * 2;
            if (matched)
            {
                _batchLastEvent = now; // let the batch wait for the death event
                _batchAnyKill = true;
            }
            if (Diag.Enabled)
                Diag.Log("DEATH evidence ptr=" + unitPtr + " matchedBatch=" + matched);

            Tick();
        }

        private static void Prune(Dictionary<long, double> table, double now, double window)
        {
            if (table.Count <= 32)
                return;
            _pruneScratch.Clear();
            foreach (var kv in table)
                if (now - kv.Value > window)
                    _pruneScratch.Add(kv.Key);
            for (int i = 0; i < _pruneScratch.Count; i++)
                table.Remove(_pruneScratch[i]);
            _pruneScratch.Clear();
        }

        // ------------------------------------------------------------------
        // Decision
        // ------------------------------------------------------------------

        private static bool BatchHasHead(double now)
        {
            if (_batchTargets.Count == 0)
                return false;
            // A head signal counts only if it points at a soldier the local
            // player actually hit in this batch.
            foreach (var kv in _headPtrs)
                if (now - kv.Value <= HeadWindow && _batchTargets.Contains(kv.Key))
                    return true;
            // The unit-less helmet statistic arrives slightly BEFORE OnSoldierHit
            // in the game log, so allow a short lead-in, but never let a stale
            // helmet hit from an unrelated fight upgrade this batch.
            return _weakHeadTime >= _batchStart - WeakHeadWindow &&
                   now - _weakHeadTime <= WeakHeadWindow;
        }

        private static void ClearHead()
        {
            _headPtrs.Clear();
            _weakHeadTime = -100.0;
        }

        private static void Resolve(double now)
        {
            if (!_batchOpen)
                return;

            bool head = BatchHasHead(now);
            bool kill = _batchAnyKill;
            int hits = _batchHits;

            _batchOpen = false;
            _batchLastEvent = -100.0;
            _lastBatchResolveTime = now;
            _batchHits = 0;
            _batchAnyKill = false;
            _batchTargets.Clear();
            ClearHead();
            _standalonePending = false; // already handled by this batch
            _playerKillStatTime = -100.0;

            // Hard guarantee: a volley that was split across frames cannot
            // produce a second sound. Single shots keep a much shorter gap so
            // fast-firing weapons are not swallowed.
            double gap = _lastFeedbackWasVolley ? VolleyMinGap : FeedbackMinGap;
            if (hits > 1) gap = Math.Max(gap, VolleyMinGap);
            if (now - _lastFeedbackTime <= gap)
            {
                if (Diag.Enabled)
                    Diag.Log("DECISION suppressed: same volley as the previous feedback " +
                             "(hits=" + hits + ", wouldBe=" +
                             (head && kill ? "headshot" : kill ? "kill" : "hit") + ")");
                return;
            }

            if (Diag.Enabled)
                Diag.Log("LATENCY hit->decision " +
                         ((now - _batchStart) * 1000.0).ToString("0.0") + " ms");

            Play(head && kill ? "headshot" : kill ? "kill" : "hit", hits, kill, head);
        }

        private static void Play(string tag, int hits, bool kill, bool head)
        {
            _lastFeedbackTime = Now;
            _lastFeedbackWasVolley = hits > 1;
            if (Diag.Enabled)
                Diag.Log("DECISION -> " + tag.ToUpperInvariant() +
                         " (batchHits=" + hits + ", anyKill=" + kill +
                         ", anyHead=" + head + ")");
            if (tag == "headshot") SoundBank.PlayHeadshot();
            else if (tag == "kill") SoundBank.PlayKill();
            else SoundBank.PlayHit();
        }

        internal static void Tick()
        {
            // Tick runs once per frame (twice, as a safety net) and on every
            // input event. Most of the time there is nothing pending, so bail
            // out before even reading the high-resolution timer.
            if (!_batchOpen && !_standalonePending)
                return;

            double now = Now;

            if (_batchOpen)
            {
                double settle = _batchHits > 1 ? VolleySettleDelay : SettleDelay;
                if (now - _batchLastEvent >= settle || now - _batchStart >= MaxBatchAge)
                    Resolve(now);
            }

            // Standalone kill: player kill statistic AND a confirmed enemy soldier
            // death, with no open batch. Covers vehicle crew; excludes allies and
            // vehicle destruction where everybody bailed out.
            if (_standalonePending && !_batchOpen)
            {
                bool evidence = now - _playerKillStatTime <= KillEvidenceWindow &&
                                now - _lastEnemyDeathTime <= KillEvidenceWindow;
                double gap = _lastFeedbackWasVolley ? VolleyMinGap : FeedbackMinGap;
                if (evidence && now - _standaloneTime >= EvidenceMinDelay &&
                    now - _lastFeedbackTime > gap &&
                    now - _lastBatchResolveTime > VolleyMinGap)
                {
                    _standalonePending = false;
                    _playerKillStatTime = -100.0;
                    ClearHead();
                    if (Diag.Enabled)
                        Diag.Log("standalone kill candidate CONFIRMED (crew died with no soldier hit)");
                    Play("kill", 0, true, false);
                }
                else if (now - _standaloneTime > KillEvidenceWindow + 0.05)
                {
                    _standalonePending = false;
                    if (Diag.Enabled)
                        Diag.Log("standalone kill candidate expired (no enemy death evidence)");
                }
            }
        }
    }
}
