﻿using System;
using System.Reflection;
using log4net;

namespace DOL.GS
{
    public static class ServiceUtils
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static void HandleServiceException<T>(Exception exception, string serviceName, EntityManager.EntityType entityType, T entity, GameObject entityOwner) where T : IManagedEntity
        {
            log.Error($"Critical error encountered in {serviceName}: {exception}");
            EntityManager.Remove(entityType, entity);

            if (entityOwner is GamePlayer player)
            {
                if (player.CharacterClass.ID == (int) eCharacterClass.Necromancer && player.IsShade)
                    player.Shade(false);

                player.Out.SendPlayerQuit(false);
                player.Quit(true);
                CraftingProgressMgr.FlushAndSaveInstance(player);
                player.SaveIntoDatabase();
            }
            else
                entityOwner.RemoveFromWorld();
        }
    }
}
