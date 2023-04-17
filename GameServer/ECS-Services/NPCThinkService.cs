﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DOL.AI;
using DOL.AI.Brain;
using ECS.Debug;
using log4net;

namespace DOL.GS
{
    public static class NPCThinkService
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private const string SERVICE_NAME = "NPCThinkService";

        private static int _nonNullBrainCount;
        private static int _nullBrainCount;

        public static int DebugTickCount { get; set; } // Will print active brain count/array size info for debug purposes if superior to 0.
        private static bool Debug => DebugTickCount > 0;

        public static void Tick(long tick)
        {
            GameLoop.CurrentServiceTick = SERVICE_NAME;
            Diagnostics.StartPerfCounter(SERVICE_NAME);

            if (Debug)
            {
                _nonNullBrainCount = 0;
                _nullBrainCount = 0;
            }

            List<ABrain> list = EntityManager.GetAll<ABrain>(EntityManager.EntityType.Brain);

            Parallel.For(0, EntityManager.GetLastNonNullIndex(EntityManager.EntityType.Brain) + 1, i =>
            {
                ABrain brain = list[i];

                try
                {
                    if (brain == null)
                    {
                        if (Debug)
                            Interlocked.Increment(ref _nullBrainCount);

                        return;
                    }

                    if (Debug)
                        Interlocked.Increment(ref _nonNullBrainCount);

                    if (brain.LastThinkTick + brain.ThinkInterval < tick)
                    {
                        if (!brain.IsActive)
                        {
                            brain.Stop();
                            return;
                        }

                        long startTick = GameTimer.GetTickCount();
                        brain.Think();
                        long stopTick = GameTimer.GetTickCount();

                        if ((stopTick - startTick) > 25 && brain != null)
                            log.Warn($"Long NPCThink for {brain.Body?.Name}({brain.Body?.ObjectID}) Interval: {brain.ThinkInterval} BrainType: {brain.GetType()} Time: {stopTick - startTick}ms");

                        brain.LastThinkTick = tick;

                        // Offset LastThinkTick for non-controlled mobs so that 'Think' ticks are not all "grouped" in one server tick.
                        if (brain is not ControlledNpcBrain)
                            brain.LastThinkTick += Util.Random(-2, 2) * GameLoop.TICK_RATE;
                    }

                    if (brain.Body.NeedsBroadcastUpdate)
                    {
                        brain.Body.BroadcastUpdate();
                        brain.Body.NeedsBroadcastUpdate = false;
                    }
                }
                catch (Exception e)
                {
                    log.Error($"Critical error encountered in NPC Think: {e}");
                }
            });

            // Output debug info.
            if (Debug)
            {
                log.Debug($"==== Non-Null NPCs in EntityManager Array: {_nonNullBrainCount} | Null NPCs: {_nullBrainCount} | Total Size: {list.Count}====");
                log.Debug("---------------------------------------------------------------------------");
                DebugTickCount--;
            }

            Diagnostics.StopPerfCounter(SERVICE_NAME);
        }
    }
}
