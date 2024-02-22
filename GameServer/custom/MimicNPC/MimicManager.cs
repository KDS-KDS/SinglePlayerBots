﻿using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Realm;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DOL.GS.Scripts
{
    #region Battlegrounds

    public static class MimicBattlegrounds
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static MimicBattleground ThidBattleground;

        public static void Initialize()
        {
            ThidBattleground = new MimicBattleground(252,
                                                    new Point3D(37200, 51200, 3950),
                                                    new Point3D(19820, 19305, 4050),
                                                    new Point3D(53300, 26100, 4270),
                                                    30,
                                                    120,
                                                    20,
                                                    24);
        }

        public class MimicBattleground
        {
            public MimicBattleground(ushort region, Point3D albSpawn, Point3D hibSpawn, Point3D midSpawn, int minMimics, int maxMimics, byte minLevel, byte maxLevel)
            {
                m_region = region;
                m_albSpawnPoint = albSpawn;
                m_hibSpawnPoint = hibSpawn;
                m_midSpawnPoint = midSpawn;
                m_minTotalMimics = minMimics;
                m_maxTotalMimics = maxMimics;
                m_minLevel = minLevel;
                m_maxLevel = maxLevel;
            }

            private ECSGameTimer m_masterTimer;
            private ECSGameTimer m_spawnTimer;

            private int m_timerInterval = 600000; // 10 minutes
            private long m_resetMaxTime = 0;

            private List<MimicNPC> m_albMimics = new List<MimicNPC>();
            private List<MimicNPC> m_albStagingList = new List<MimicNPC>();

            private List<MimicNPC> m_hibMimics = new List<MimicNPC>();
            private List<MimicNPC> m_hibStagingList = new List<MimicNPC>();

            private List<MimicNPC> m_midMimics = new List<MimicNPC>();
            private List<MimicNPC> m_midStagingList = new List<MimicNPC>();

            private readonly List<BattleStats> m_battleStats = new List<BattleStats>();

            private Point3D m_albSpawnPoint;
            private Point3D m_hibSpawnPoint;
            private Point3D m_midSpawnPoint;

            private ushort m_region;

            private byte m_minLevel;
            private byte m_maxLevel;

            private int m_minTotalMimics;
            private int m_maxTotalMimics;

            private int m_currentMinTotalMimics;
            private int m_currentMaxTotalMimics;

            private int m_currentMaxAlb;
            private int m_currentMaxHib;
            private int m_currentMaxMid;

            private int m_groupChance = 50;

            public void Start()
            {
                if (m_masterTimer == null)
                {
                    m_masterTimer = new ECSGameTimer(null, new ECSGameTimer.ECSTimerCallback(MasterTimerCallback));
                    m_masterTimer.Start();
                }
            }

            public void Stop()
            {
                if (m_masterTimer != null)
                {
                    m_masterTimer.Stop();
                    m_masterTimer = null;
                }

                if (m_spawnTimer != null)
                {
                    m_spawnTimer.Stop();
                    m_spawnTimer = null;
                }

                ValidateLists();

                m_albStagingList.Clear();
                m_hibStagingList.Clear();
                m_midStagingList.Clear();
            }

            public void Clear()
            {
                Stop();

                if (m_albMimics.Any())
                {
                    foreach (MimicNPC mimic in m_albMimics)
                        mimic.Delete();

                    m_albMimics.Clear();
                }

                if (m_hibMimics.Any())
                {
                    foreach (MimicNPC mimic in m_hibMimics)
                        mimic.Delete();

                    m_hibMimics.Clear();
                }

                if (m_midMimics.Any())
                {
                    foreach (MimicNPC mimic in m_midMimics)
                        mimic.Delete();

                    m_midMimics.Clear();
                }
            }

            private int MasterTimerCallback(ECSGameTimer timer)
            {
                if (GameLoop.GetCurrentTime() > m_resetMaxTime)
                    ResetMaxMimics();

                ValidateLists();
                RefreshLists();
                SpawnLists();

                int totalMimics = m_albMimics.Count + m_hibMimics.Count + m_midMimics.Count;
                log.Info("Alb: " + m_albMimics.Count + "/" + m_currentMaxAlb);
                log.Info("Hib: " + m_hibMimics.Count + "/" + m_currentMaxHib);
                log.Info("Mid: " + m_midMimics.Count + "/" + m_currentMaxMid);
                log.Info("Total Mimics: " + totalMimics + "/" + m_currentMaxTotalMimics);

                return m_timerInterval + Util.Random(-300000, 300000); // 10 minutes + or - 5 minutes
            }

            /// <summary>
            /// Removes any dead or deleted mimics from each realm list.
            /// </summary>
            private void ValidateLists()
            {
                if (m_albMimics.Any())
                {
                    List<MimicNPC> validatedList = new List<MimicNPC>();

                    foreach (MimicNPC mimic in m_albMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            validatedList.Add(mimic);
                    }

                    m_albMimics = validatedList;
                }

                if (m_hibMimics.Any())
                {
                    List<MimicNPC> validatedList = new List<MimicNPC>();

                    foreach (MimicNPC mimic in m_hibMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            validatedList.Add(mimic);
                    }

                    m_hibMimics = validatedList;
                }

                if (m_midMimics.Any())
                {
                    List<MimicNPC> validatedList = new List<MimicNPC>();

                    foreach (MimicNPC mimic in m_midMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            validatedList.Add(mimic);
                    }

                    m_midMimics = validatedList;
                }
            }

            /// <summary>
            /// Adds new mimics to each realm list based on the difference between max and current count
            /// </summary>
            private void RefreshLists()
            {
                if (m_albMimics.Count < m_currentMaxAlb)
                {
                    for (int i = 0; i < m_currentMaxAlb - m_albMimics.Count; i++)
                    {
                        byte level = (byte)Util.Random(m_minLevel, m_maxLevel);
                        MimicNPC mimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Albion), level);
                        m_albMimics.Add(mimic);
                    }
                }

                if (m_hibMimics.Count < m_currentMaxHib)
                {
                    for (int i = 0; i < m_currentMaxHib - m_hibMimics.Count; i++)
                    {
                        byte level = (byte)Util.Random(m_minLevel, m_maxLevel);
                        MimicNPC mimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Hibernia), level);
                        m_hibMimics.Add(mimic);
                    }
                }

                if (m_midMimics.Count < m_currentMaxMid)
                {
                    for (int i = 0; i < m_currentMaxMid - m_midMimics.Count; i++)
                    {
                        byte level = (byte)Util.Random(m_minLevel, m_maxLevel);
                        MimicNPC mimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(eRealm.Midgard), level);
                        m_midMimics.Add(mimic);
                    }
                }
            }

            private void SpawnLists()
            {
                m_albStagingList = new List<MimicNPC>();
                m_hibStagingList = new List<MimicNPC>();
                m_midStagingList = new List<MimicNPC>();

                if (m_albMimics.Any())
                {
                    foreach (MimicNPC mimic in m_albMimics)
                    {
                        if (mimic.ObjectState != GameObject.eObjectState.Active)
                            m_albStagingList.Add(mimic);
                    }
                }

                if (m_hibMimics.Any())
                {
                    foreach (MimicNPC mimic in m_hibMimics)
                    {
                        if (mimic.ObjectState != GameObject.eObjectState.Active)
                            m_hibStagingList.Add(mimic);
                    }
                }

                if (m_midMimics.Any())
                {
                    foreach (MimicNPC mimic in m_midMimics)
                    {
                        if (mimic.ObjectState != GameObject.eObjectState.Active)
                            m_midStagingList.Add(mimic);
                    }
                }

                SetGroupMembers(m_albStagingList);
                SetGroupMembers(m_hibStagingList);
                SetGroupMembers(m_midStagingList);

                m_spawnTimer = new ECSGameTimer(null, new ECSGameTimer.ECSTimerCallback(Spawn), 1000);
            }

            private int Spawn(ECSGameTimer timer)
            {
                bool albDone = false;
                bool hibDone = false;
                bool midDone = false;

                if (m_albStagingList.Any())
                {
                    MimicManager.AddMimicToWorld(m_albStagingList[m_albStagingList.Count - 1], m_albSpawnPoint, m_region);
                    m_albStagingList.RemoveAt(m_albStagingList.Count - 1);
                }
                else
                    albDone = true;

                if (m_hibStagingList.Any())
                {
                    MimicManager.AddMimicToWorld(m_hibStagingList[m_hibStagingList.Count - 1], m_hibSpawnPoint, m_region);
                    m_hibStagingList.RemoveAt(m_hibStagingList.Count - 1);
                }
                else
                    hibDone = true;

                if (m_midStagingList.Any())
                {
                    MimicManager.AddMimicToWorld(m_midStagingList[m_midStagingList.Count - 1], m_midSpawnPoint, m_region);
                    m_midStagingList.RemoveAt(m_midStagingList.Count - 1);
                }
                else
                    midDone = true;

                if (albDone && hibDone && midDone)
                    return 0;
                else
                    return 5000;
            }

            private void SetGroupMembers(List<MimicNPC> list)
            {
                if (list.Count > 1)
                {
                    int groupChance = m_groupChance;
                    int groupLeaderIndex = -1;

                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i + 1 < list.Count)
                        {
                            if (Util.Chance(groupChance) && !(list[i].Group?.GetMembersInTheGroup().Count > 7))
                            {
                                if (groupLeaderIndex == -1)
                                {
                                    list[i].Group = new Group(list[i]);
                                    list[i].Group.AddMember(list[i]);
                                    groupLeaderIndex = i;
                                }

                                list[groupLeaderIndex].Group.AddMember(list[i + 1]);
                                groupChance -= 5;
                            }
                            else
                            {
                                groupLeaderIndex = -1;
                                groupChance = m_groupChance;
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Gets a new total maximum and minimum of mimics for each realm randomly.
            /// </summary>
            private void ResetMaxMimics()
            {
                m_currentMaxTotalMimics = Util.Random(m_minTotalMimics, m_maxTotalMimics);
                m_currentMaxAlb = 0;
                m_currentMaxHib = 0;
                m_currentMaxMid = 0;

                for (int i = 0; i < m_currentMaxTotalMimics; i++)
                {
                    eRealm randomRealm = (eRealm)Util.Random(1, 3);

                    if (randomRealm == eRealm.Albion)
                        m_currentMaxAlb++;
                    else if (randomRealm == eRealm.Hibernia)
                        m_currentMaxHib++;
                    else if (randomRealm == eRealm.Midgard)
                        m_currentMaxMid++;
                }

                m_resetMaxTime = GameLoop.GetCurrentTime() + Util.Random(1800000, 3600000);
            }

            public void UpdateBattleStats(MimicNPC mimic)
            {
                m_battleStats.Add(new BattleStats(mimic.Name, mimic.RaceName, mimic.CharacterClass.Name, mimic.Kills, true));
            }

            public void BattlegroundStats(GamePlayer player)
            {
                List<MimicNPC> currentMimics = GetMasterList();
                List<BattleStats> currentStats = new List<BattleStats>();

                if (currentMimics.Any())
                {
                    foreach (MimicNPC mimic in currentMimics)
                        currentStats.Add(new BattleStats(mimic.Name, mimic.RaceName, mimic.CharacterClass.Name, mimic.Kills, false));
                }

                List<BattleStats> masterStatList = new List<BattleStats>();
                masterStatList.AddRange(currentStats);

                lock (m_battleStats)
                {
                    masterStatList.AddRange(m_battleStats);
                }

                List<BattleStats> sortedList = masterStatList.OrderByDescending(obj => obj.TotalKills).ToList();

                string message = "----------------------------------------\n\n";
                int index = Math.Min(25, sortedList.Count);

                if (sortedList.Any())
                {
                    for (int i = 0; i < index; i++)
                    {
                        string stats = string.Format("{0}. {1} - {2} - {3} - Kills: {4}",
                            i + 1,
                            sortedList[i].Name,
                            sortedList[i].Race,
                            sortedList[i].ClassName,
                            sortedList[i].TotalKills);

                        if (sortedList[i].IsDead)
                            stats += " - DEAD";

                        stats += "\n\n";

                        message += stats;
                    }
                }

                switch (player.Realm)
                {
                    case eRealm.Albion:
                    if (m_albMimics.Any())
                        message += "Alb count: " + m_albMimics.Count;
                    break;

                    case eRealm.Hibernia:
                    if (m_hibMimics.Any())
                        message += "Hib count: " + m_hibMimics.Count;
                    break;

                    case eRealm.Midgard:
                    if (m_midMimics.Any())
                        message += "Mid count: " + m_midMimics.Count;
                    break;
                }

                player.Out.SendMessage(message, PacketHandler.eChatType.CT_System, PacketHandler.eChatLoc.CL_PopupWindow);
            }

            public List<MimicNPC> GetMasterList()
            {
                List<MimicNPC> masterList = new List<MimicNPC>();

                lock (m_albMimics)
                {
                    foreach (MimicNPC mimic in m_albMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            masterList.Add(mimic);
                    }
                }

                lock (m_hibMimics)
                {
                    foreach (MimicNPC mimic in m_hibMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            masterList.Add(mimic);
                    }
                }

                lock (m_midMimics)
                {
                    foreach (MimicNPC mimic in m_midMimics)
                    {
                        if (mimic != null && mimic.ObjectState == GameObject.eObjectState.Active && mimic.ObjectState != GameObject.eObjectState.Deleted)
                            masterList.Add(mimic);
                    }
                }

                return masterList;
            }
        }

        private struct BattleStats
        {
            public string Name;
            public string Race;
            public string ClassName;
            public int TotalKills;
            public bool IsDead;

            public BattleStats(string name, string race, string className, int totalKills, bool dead)
            {
                Name = name;
                Race = race;
                ClassName = className;
                TotalKills = totalKills;
                IsDead = dead;
            }
        }
    }

    #endregion Battlegrounds

    public static class MimicManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static List<MimicNPC> MimicNPCs = new List<MimicNPC>();

        public static bool Initialize()
        {
            // Battlegrounds
            MimicBattlegrounds.Initialize();

            return true;
        }

        public static bool AddMimicToWorld(MimicNPC mimic, Point3D position, ushort region)
        {
            if (mimic != null)
            {
                mimic.X = position.X;
                mimic.Y = position.Y;
                mimic.Z = position.Z;

                mimic.CurrentRegionID = region;

                if (mimic.AddToWorld())
                    return true;
            }

            return false;
        }

        public static MimicNPC GetMimic(eMimicClass mimicClass, byte level, string name = "", eGender gender = eGender.Neutral, bool preventCombat = false)
        {
            if (mimicClass == eMimicClass.None)
                return null;

            MimicNPC mimic = null;

            switch (mimicClass)
            {
                case eMimicClass.Armsman: mimic = new MimicArmsman(level); break;
                case eMimicClass.Cabalist: mimic = new MimicCabalist(level); break;
                case eMimicClass.Cleric: mimic = new MimicCleric(level); break;
                case eMimicClass.Friar: mimic = new MimicFriar(level); break;
                case eMimicClass.Infiltrator: mimic = new MimicInfiltrator(level); break;
                case eMimicClass.Mercenary: mimic = new MimicMercenary(level); break;
                case eMimicClass.Minstrel: mimic = new MimicMinstrel(level); break;
                case eMimicClass.Paladin: mimic = new MimicPaladin(level); break;
                case eMimicClass.Reaver: mimic = new MimicReaver(level); break;
                case eMimicClass.Scout: mimic = new MimicScout(level); break;
                case eMimicClass.Sorcerer: mimic = new MimicSorcerer(level); break;
                case eMimicClass.Theurgist: mimic = new MimicTheurgist(level); break;
                case eMimicClass.Wizard: mimic = new MimicWizard(level); break;

                case eMimicClass.Bard: mimic = new MimicBard(level); break;
                case eMimicClass.Blademaster: mimic = new MimicBlademaster(level); break;
                case eMimicClass.Champion: mimic = new MimicChampion(level); break;
                case eMimicClass.Druid: mimic = new MimicDruid(level); break;
                case eMimicClass.Eldritch: mimic = new MimicEldritch(level); break;
                case eMimicClass.Enchanter: mimic = new MimicEnchanter(level); break;
                case eMimicClass.Hero: mimic = new MimicHero(level); break;
                case eMimicClass.Mentalist: mimic = new MimicMentalist(level); break;
                case eMimicClass.Nightshade: mimic = new MimicNightshade(level); break;
                case eMimicClass.Ranger: mimic = new MimicRanger(level); break;
                case eMimicClass.Valewalker: mimic = new MimicValewalker(level); break;
                case eMimicClass.Warden: mimic = new MimicWarden(level); break;

                case eMimicClass.Berserker: mimic = new MimicBerserker(level); break;
                case eMimicClass.Bonedancer: mimic = new MimicBonedancer(level); break;
                case eMimicClass.Healer: mimic = new MimicHealer(level); break;
                case eMimicClass.Hunter: mimic = new MimicHunter(level); break;
                case eMimicClass.Runemaster: mimic = new MimicRunemaster(level); break;
                case eMimicClass.Savage: mimic = new MimicSavage(level); break;
                case eMimicClass.Shadowblade: mimic = new MimicShadowblade(level); break;
                case eMimicClass.Shaman: mimic = new MimicShaman(level); break;
                case eMimicClass.Skald: mimic = new MimicSkald(level); break;
                case eMimicClass.Spiritmaster: mimic = new MimicSpiritmaster(level); break;
                case eMimicClass.Thane: mimic = new MimicThane(level); break;
                case eMimicClass.Warrior: mimic = new MimicWarrior(level); break;
            }

            if (mimic != null)
            {
                if (name != "")
                    mimic.Name = name;

                if (gender != eGender.Neutral)
                {
                    mimic.Gender = gender;

                    foreach (PlayerRace race in PlayerRace.AllRaces)
                    {
                        if (race.ID == (eRace)mimic.Race)
                        {
                            mimic.Model = (ushort)race.GetModel(gender);
                            break;
                        }
                    }
                }

                if (preventCombat)
                {
                    MimicBrain mimicBrain = mimic.Brain as MimicBrain;

                    if (mimicBrain != null)
                        mimicBrain.PreventCombat = preventCombat;
                }

                return mimic;
            }

            return null;
        }

        public static eMimicClass GetRandomMimicClass(eRealm realm)
        {
            int randomIndex;

            if (realm == eRealm.Albion)
                randomIndex = Util.Random(12);
            else if (realm == eRealm.Hibernia)
                randomIndex = Util.Random(13, 24);
            else if (realm == eRealm.Midgard)
                randomIndex = Util.Random(25, 36);
            else
                randomIndex = Util.Random(36);

            return (eMimicClass)randomIndex;
        }

        public static eMimicClass GetRandomMeleeClass()
        {
            int enumIndexes = Enum.GetValues(typeof(eMimicClass)).Length - 1;

            Console.WriteLine("Indexes: " + enumIndexes);

            List<int> meleeClasses = new List<int>();

            for (int i = 0; i < enumIndexes; i++)
            {
                if (i == 1
                    || i == 10
                    || i == 11
                    || i == 12
                    || i == 17
                    || i == 18
                    || i == 20
                    || i == 26
                    || i == 29
                    || i == 34)
                    continue;
                else
                    meleeClasses.Add(i);
            }

            int randomIndex = Util.Random(meleeClasses.Count - 1);

            return (eMimicClass)meleeClasses[randomIndex];
        }

        #region Spec

        // TODO: Will likley need to be able to tell caster specs apart for AI purposes since they operate so differently. Will bring them into here, or use some sort of enum.

        // Albion
        private static Type[] cabalistSpecs = { typeof(MatterCabalist), typeof(BodyCabalist), typeof(SpiritCabalist) };

        // Hibernia
        private static Type[] eldritchSpecs = { typeof(SunEldritch), typeof(ManaEldritch), typeof(VoidEldritch) };

        private static Type[] enchanterSpecs = { typeof(ManaEnchanter), typeof(LightEnchanter) };
        private static Type[] mentalistSpecs = { typeof(LightMentalist), typeof(ManaMentalist), typeof(MentalismMentalist) };

        // Midgard
        private static Type[] healerSpecs = { typeof(PacHealer), typeof(AugHealer) };

        public static MimicSpec Random(MimicNPC mimicNPC)
        {
            switch (mimicNPC)
            {
                // Albion
                case MimicCabalist: return Activator.CreateInstance(cabalistSpecs[Util.Random(cabalistSpecs.Length - 1)]) as MimicSpec;

                // Hibernia
                case MimicEldritch: return Activator.CreateInstance(eldritchSpecs[Util.Random(eldritchSpecs.Length - 1)]) as MimicSpec;
                case MimicEnchanter: return Activator.CreateInstance(enchanterSpecs[Util.Random(enchanterSpecs.Length - 1)]) as MimicSpec;
                case MimicMentalist: return Activator.CreateInstance(mentalistSpecs[Util.Random(mentalistSpecs.Length - 1)]) as MimicSpec;

                // Midgard
                case MimicHealer: return Activator.CreateInstance(healerSpecs[Util.Random(healerSpecs.Length - 1)]) as MimicSpec;

                default: return null;
            }
        }

        #endregion Spec
    }

    #region Equipment

    public static class MimicEquipment
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static void SetWeaponROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType, eInventorySlot slot, eDamageType damageType)
        {
            DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot, damageType);

            GameInventoryItem item = GameInventoryItem.Create(itemToCreate);
            living.Inventory.AddItem(slot, item);
        }

        public static void SetArmorROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType)
        {
            for (int i = Slot.HELM; i <= Slot.ARMS; i++)
            {
                if (i == Slot.JEWELRY || i == Slot.CLOAK)
                    continue;

                eInventorySlot slot = (eInventorySlot)i;
                DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot);

                GameInventoryItem item = GameInventoryItem.Create(itemToCreate);

                living.Inventory.AddItem(slot, item);
            }
        }

        public static void SetJewelryROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType)
        {
            for (int i = Slot.JEWELRY; i <= Slot.RIGHTRING; i++)
            {
                if (i is Slot.TORSO or Slot.LEGS or Slot.ARMS or Slot.FOREARMS or Slot.SHIELD)
                    continue;

                eInventorySlot slot = (eInventorySlot)i;
                DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot);

                GameInventoryItem item = GameInventoryItem.Create(itemToCreate);

                if (i == Slot.RIGHTRING || i == Slot.LEFTRING)
                    living.Inventory.AddItem(living.Inventory.FindFirstEmptySlot(eInventorySlot.LeftRing, eInventorySlot.RightRing), item);
                else if (i == Slot.LEFTWRIST || i == Slot.RIGHTWRIST)
                    living.Inventory.AddItem(living.Inventory.FindFirstEmptySlot(eInventorySlot.LeftBracer, eInventorySlot.RightBracer), item);
                else
                    living.Inventory.AddItem(slot, item);
            }
        }

        public static void SetInstrumentROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType, eInventorySlot slot, eInstrumentType instrumentType)
        {
            DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot, instrumentType);

            GameInventoryItem item = GameInventoryItem.Create(itemToCreate);
            //if (!living.Inventory.AddItem(slot, item))
            //    log.Info("Could not add " + item.Name + " to slot " + slot);
            //else
            //    log.Info("Added " + item.Name + " to slot " + slot);
        }

        public static void SetMeleeWeapon(IGamePlayer player, eObjectType weapType, eHand hand, eWeaponDamageType damageType = 0)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 4);

            IList<DbItemTemplate> itemList;

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));
            if (itemList.Any())
            {
                List<DbItemTemplate> itemsToKeep = new List<DbItemTemplate>();

                foreach (DbItemTemplate item in itemList)
                {
                    bool shouldAddItem = false;

                    switch (hand)
                    {
                        case eHand.oneHand:
                        shouldAddItem = item.Item_Type == Slot.RIGHTHAND || item.Item_Type == Slot.LEFTHAND;
                        break;

                        case eHand.leftHand:
                        shouldAddItem = item.Item_Type == Slot.LEFTHAND;
                        break;

                        case eHand.twoHand:
                        shouldAddItem = item.Item_Type == Slot.TWOHAND && (damageType == 0 || item.Type_Damage == (int)damageType);
                        break;

                        default:
                        break;
                    }

                    if (shouldAddItem)
                        itemsToKeep.Add(item);
                }

                if (itemsToKeep.Any())
                {
                    DbItemTemplate itemTemplate = itemsToKeep[Util.Random(itemsToKeep.Count - 1)];
                    AddItem(player, itemTemplate, hand);
                }
            }
            else
                log.Info("No melee weapon found for " + player.Name);
        }

        public static void SetRangedWeapon(IGamePlayer player, eObjectType weapType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("Item_Type").IsEqualTo(13).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Any())
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                AddItem(player, itemTemplate);

                return;
            }
            else
                log.Info("No Ranged weapon found for " + player.Name);
        }

        public static void SetShield(IGamePlayer player, int shieldSize)
        {
            if (shieldSize < 1)
                return;

            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)eObjectType.Shield).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("Type_Damage").IsEqualTo(shieldSize).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Any())
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                AddItem(player, itemTemplate);

                return;
            }
            else
                log.Info("No Shield found for " + player.Name);
        }

        public static void SetArmor(IGamePlayer player, eObjectType armorType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)armorType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));

            if (itemList.Any())
            {
                Dictionary<int, List<DbItemTemplate>> armorSlots = new Dictionary<int, List<DbItemTemplate>>();

                foreach (DbItemTemplate template in itemList)
                {
                    if (!armorSlots.TryGetValue(template.Item_Type, out List<DbItemTemplate> slotList))
                    {
                        slotList = new List<DbItemTemplate>();
                        armorSlots[template.Item_Type] = slotList;
                    }

                    slotList.Add(template);
                }

                foreach (var pair in armorSlots)
                {
                    if (pair.Value.Any())
                    {
                        DbItemTemplate itemTemplate = pair.Value[Util.Random(pair.Value.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }
            }
            else
                log.Info("No armor found for " + player.Name);
        }

        public static void SetInstrument(IGamePlayer player, eObjectType weapType, eInventorySlot slot, eInstrumentType instrumentType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("DPS_AF").IsEqualTo((int)instrumentType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Any())
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                DbInventoryItem item = GameInventoryItem.Create(itemTemplate);
                player.Inventory.AddItem(slot, item);

                return;
            }
            else
                log.Info("No instrument found for " + player.Name);
        }

        public static void SetJewelry(IGamePlayer player)
        {
            int min = Math.Max(1, player.Level - 30);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            List<DbItemTemplate> cloakList = new List<DbItemTemplate>();
            List<DbItemTemplate> jewelryList = new List<DbItemTemplate>();
            List<DbItemTemplate> ringList = new List<DbItemTemplate>();
            List<DbItemTemplate> wristList = new List<DbItemTemplate>();
            List<DbItemTemplate> neckList = new List<DbItemTemplate>();
            List<DbItemTemplate> waistList = new List<DbItemTemplate>();

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)eObjectType.Magical).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));
            if (itemList.Any())
            {
                foreach (DbItemTemplate template in itemList)
                {
                    if (template.Item_Type == Slot.CLOAK)
                    {
                        template.Color = Util.Random((Enum.GetValues(typeof(eColor)).Length));
                        cloakList.Add(template);
                    }
                    else if (template.Item_Type == Slot.JEWELRY)
                        jewelryList.Add(template);
                    else if (template.Item_Type == Slot.LEFTRING || template.Item_Type == Slot.RIGHTRING)
                        ringList.Add(template);
                    else if (template.Item_Type == Slot.LEFTWRIST || template.Item_Type == Slot.RIGHTWRIST)
                        wristList.Add(template);
                    else if (template.Item_Type == Slot.NECK)
                        neckList.Add(template);
                    else if (template.Item_Type == Slot.WAIST)
                        waistList.Add(template);
                }

                List<List<DbItemTemplate>> masterList = new List<List<DbItemTemplate>>
                {
                cloakList,
                jewelryList,
                neckList,
                waistList
                };

                foreach (List<DbItemTemplate> list in masterList)
                {
                    if (list.Any())
                    {
                        DbItemTemplate itemTemplate = list[Util.Random(list.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }

                // Add two rings and bracelets
                for (int i = 0; i < 2; i++)
                {
                    if (ringList.Any())
                    {
                        DbItemTemplate itemTemplate = ringList[Util.Random(ringList.Count - 1)];
                        AddItem(player, itemTemplate);
                    }

                    if (wristList.Any())
                    {
                        DbItemTemplate itemTemplate = wristList[Util.Random(wristList.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }

                // Not sure this is needed what were you thinking past self?
                if (player.Inventory.GetItem(eInventorySlot.Cloak) == null)
                {
                    DbItemTemplate cloak = GameServer.Database.FindObjectByKey<DbItemTemplate>("cloak");
                    cloak.Color = Util.Random((Enum.GetValues(typeof(eColor)).Length));
                    AddItem(player, cloak);
                }
            }
            else
                log.Info("No jewelry of any kind found for " + player.Name);
        }

        private static void AddItem(IGamePlayer player, DbItemTemplate itemTemplate, eHand hand = eHand.None)
        {
            if (itemTemplate == null)
                log.Info("itemTemplate in AddItem is null");

            DbInventoryItem item = GameInventoryItem.Create(itemTemplate);

            if (item != null)
            {
                if (item.Item_Type == Slot.LEFTRING || item.Item_Type == Slot.RIGHTRING)
                {
                    player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.LeftRing, eInventorySlot.RightRing), item);
                    return;
                }
                else if (item.Item_Type == Slot.LEFTWRIST || item.Item_Type == Slot.RIGHTWRIST)
                {
                    player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.LeftBracer, eInventorySlot.RightBracer), item);
                    return;
                }
                else if (item.Item_Type == Slot.LEFTHAND && item.Object_Type != (int)eObjectType.Shield && hand == eHand.oneHand)
                {
                    player.Inventory.AddItem(eInventorySlot.RightHandWeapon, item);
                    return;
                }
                else
                {
                    if (item.Object_Type == (int)eObjectType.Shield &&
                        (player.CharacterClass.ID == (int)eCharacterClass.Infiltrator ||
                        player.CharacterClass.ID == (int)eCharacterClass.Mercenary ||
                        player.CharacterClass.ID == (int)eCharacterClass.Nightshade ||
                        player.CharacterClass.ID == (int)eCharacterClass.Ranger ||
                        player.CharacterClass.ID == (int)eCharacterClass.Blademaster ||
                        player.CharacterClass.ID == (int)eCharacterClass.Shadowblade ||
                        player.CharacterClass.ID == (int)eCharacterClass.Berserker ||
                        (player.CharacterClass.ID == (int)eCharacterClass.Savage)))
                    {
                        player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstEmptyBackpack, eInventorySlot.LastEmptyBackpack), item);
                    }
                    else
                        player.Inventory.AddItem((eInventorySlot)item.Item_Type, item);
                }
            }
            else
                log.Info("Item failed to be created for " + player.Name);
        }
    }

    #endregion Equipment

    #region Spec

    public class MimicSpec
    {
        public static string SpecName;
        public eObjectType WeaponTypeOne;
        public eObjectType WeaponTypeTwo;
        public eWeaponDamageType DamageType = 0;

        public bool is2H;

        public List<SpecLine> SpecLines = new List<SpecLine>();

        public MimicSpec()
        { }

        protected void Add(string spec, uint cap, float ratio)
        {
            SpecLines.Add(new SpecLine(spec, cap, ratio));
        }

        protected string ObjToSpec(eObjectType obj)
        {
            string spec = SkillBase.ObjectTypeToSpec(obj);

            return spec;
        }
    }

    public struct SpecLine
    {
        public string Spec;
        public uint SpecCap;
        public float levelRatio;

        public SpecLine(string spec, uint cap, float ratio)
        {
            Spec = spec;
            SpecCap = cap;
            levelRatio = ratio;
        }
    }

    #endregion Spec

    #region LFG

    public static class MimicLFGManager
    {
        public static List<MimicLFGEntry> LFGListAlb = new List<MimicLFGEntry>();
        public static List<MimicLFGEntry> LFGListHib = new List<MimicLFGEntry>();
        public static List<MimicLFGEntry> LFGListMid = new List<MimicLFGEntry>();

        private static long _respawnTimeAlb = 0;
        private static long _respawnTimeHib = 0;
        private static long _respawnTimeMid = 0;

        private static int minRespawnTime = 60000;
        private static int maxRespawnTime = 600000;

        private static int minRemoveTime = 300000;
        private static int maxRemoveTime = 3600000;

        private static int maxMimics = 20;
        private static int chance = 25;

        public static List<MimicLFGEntry> GetLFG(eRealm realm, byte level)
        {
            switch (realm)
            {
                case eRealm.Albion:
                {
                    if (_respawnTimeAlb == 0)
                    {
                        _respawnTimeAlb = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        LFGListAlb = GenerateList(LFGListAlb, realm, level);
                    }

                    lock (LFGListAlb)
                    {
                        LFGListAlb = ValidateList(LFGListAlb);

                        if (GameLoop.GameLoopTime > _respawnTimeAlb)
                        {
                            LFGListAlb = GenerateList(LFGListAlb, realm, level);
                            _respawnTimeAlb = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        }
                    }

                    return LFGListAlb;
                }

                case eRealm.Hibernia:
                {
                    if (_respawnTimeHib == 0)
                    {
                        _respawnTimeHib = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        LFGListHib = GenerateList(LFGListHib, realm, level);
                    }

                    lock (LFGListHib)
                    {
                        LFGListHib = ValidateList(LFGListHib);

                        if (GameLoop.GameLoopTime > _respawnTimeHib)
                        {
                            LFGListHib = GenerateList(LFGListHib, realm, level);
                            _respawnTimeHib = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        }
                    }

                    return LFGListHib;
                }

                case eRealm.Midgard:
                {
                    if (_respawnTimeMid == 0)
                    {
                        _respawnTimeMid = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        LFGListMid = GenerateList(LFGListMid, realm, level);
                    }

                    lock (LFGListMid)
                    {
                        LFGListMid = ValidateList(LFGListMid);

                        if (GameLoop.GameLoopTime > _respawnTimeMid)
                        {
                            LFGListMid = GenerateList(LFGListMid, realm, level);
                            _respawnTimeMid = GameLoop.GameLoopTime + Util.Random(minRespawnTime, maxRespawnTime);
                        }
                    }

                    return LFGListMid;
                }
            }

            return null;
        }

        public static void Remove(eRealm realm, MimicLFGEntry entryToRemove)
        {
            switch (realm)
            {
                case eRealm.Albion:
                if (LFGListAlb.Any())
                {
                    lock (LFGListAlb)
                    {
                        foreach (MimicLFGEntry entry in LFGListAlb)
                        {
                            if (entry == entryToRemove)
                            {
                                entry.RemoveTime = GameLoop.GameLoopTime - 1;
                                break;
                            }
                        }
                    }
                }
                break;

                case eRealm.Hibernia:
                if (LFGListHib.Any())
                {
                    lock (LFGListHib)
                    {
                        foreach (MimicLFGEntry entry in LFGListHib)
                        {
                            if (entry == entryToRemove)
                            {
                                entry.RemoveTime = GameLoop.GameLoopTime;
                                break;
                            }
                        }
                    }
                }
                break;

                case eRealm.Midgard:
                if (LFGListMid.Any())
                {
                    lock (LFGListMid)
                    {
                        foreach (MimicLFGEntry entry in LFGListMid)
                        {
                            if (entry == entryToRemove)
                            {
                                entry.RemoveTime = GameLoop.GameLoopTime;
                                break;
                            }
                        }
                    }
                }
                break;
            }
        }

        private static List<MimicLFGEntry> GenerateList(List<MimicLFGEntry> entries, eRealm realm, byte level)
        {
            if (entries.Count < maxMimics)
            {
                int mimicsToAdd = maxMimics - entries.Count;

                for (int i = 0; i < mimicsToAdd; i++)
                {
                    if (Util.Chance(chance))
                    {
                        int levelMin = Math.Max(1, level - 3);
                        int levelMax = Math.Min(50, level + 3);
                        int levelRand = Util.Random(levelMin, levelMax);
                        long removeTime = GameLoop.GameLoopTime + Util.Random(minRemoveTime, maxRemoveTime);

                        MimicLFGEntry entry = new MimicLFGEntry(MimicManager.GetRandomMimicClass(realm), (byte)levelRand, realm, removeTime);

                        entries.Add(entry);
                    }
                }
            }

            List<MimicLFGEntry> generateList = new List<MimicLFGEntry>();
            generateList.AddRange(entries);

            return generateList;
        }

        private static List<MimicLFGEntry> ValidateList(List<MimicLFGEntry> entries)
        {
            List<MimicLFGEntry> validList = new List<MimicLFGEntry>();

            if (entries.Any())
            {
                foreach (MimicLFGEntry entry in entries)
                {
                    if (GameLoop.GameLoopTime < entry.RemoveTime)
                        validList.Add(entry);
                }
            }

            return validList;
        }

        public class MimicLFGEntry
        {
            public string Name;
            public eGender Gender;
            public eMimicClass MimicClass;
            public byte Level;
            public eRealm Realm;
            public long RemoveTime;
            public bool RefusedGroup;

            public MimicLFGEntry(eMimicClass mimicClass, byte level, eRealm realm, long removeTime)
            {
                Gender = Util.RandomBool() ? eGender.Male : eGender.Female;
                Name = MimicNames.GetName(Gender, realm);
                MimicClass = mimicClass;
                Level = level;
                Realm = realm;
                RemoveTime = removeTime;
            }
        }
    }

    #endregion LFG

    public class SetupMimicsEvent
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        [ScriptLoadedEvent]
        public static void OnScriptsCompiled(DOLEvent e, object sender, EventArgs args)
        {
            if (MimicManager.Initialize())
                log.Info("MimicNPCs Initialized.");
            else
                log.Error("MimicNPCs Failed to Initialize.");
        }
    }

    // Just a quick way to get names...
    public static class MimicNames
    {
        private const string albMaleNames = "Gareth,Lancelot,Cedric,Tristan,Percival,Gawain,Arthur,Merlin,Galahad,Ector,Uther,Mordred,Bors,Lionel,Agravain,Bedivere,Kay,Lamorak,Erec,Gaheris,Pellinore,Loholt,Leodegrance,Aglovale,Tor,Ywain,Uri,Cador,Elayne,Tristram,Cei,Gavain,Kei,Launcelot,Meleri,Isolde,Dindrane,Ragnelle,Lunete,Morgause,Yseult,Bellicent,Brangaine,Blanchefleur,Enid,Vivian,Laudine,Selivant,Lisanor,Ganelon,Cundrie,Guinevere,Norgal,Vivienne,Clarissant,Ettard,Morgaine,Serene,Serien,Selwod,Siraldus,Corbenic,Gurnemanz,Terreban,Malory,Dodinel,Serien,Gurnemanz,Manessen,Herzeleide,Taulat,Serien,Bohort,Ysabele,Karados,Dodinel,Peronell,Dinadan,Segwarides,Lucan,Lamorat,Enide,Parzival,Aelfric,Geraint,Rivalin,Blanchefleur,Gurnemanz,Terreban,Launceor,Clarissant,Herzeleide,Taulat,Zerbino,Serien,Bohort,Ysabele,Dodinel,Peronell,Serenadine,Dinadan,Caradoc,Segwarides,Lucan,Lamorat,Enide,Parzival,Aelfric,Geraint,Rivalin,Blanchefleur,Kaherdin,Gurnemanz,Terreban,Launceor,Clarissant,Patrise,Navarre,Taulat,Iseut,Guivret,Madouc,Ygraine,Tristran,Perceval,Lanzarote,Lamorat,Ysolt,Evaine,Guenever,Elisena,Rowena,Deirdre,Maelis,Clarissant,Palamedes,Yseult,Iseult,Palomides,Brangaine,Laudine,Herlews,Tristram,Alundyne,Blasine,Dinas";
        private const string albFemaleNames = "Guinevere,Isolde,Morgana,Elaine,Vivienne,Nimue,Lynette,Rhiannon,Enid,Iseult,Bellicent,Brangaine,Blanchefleur,Laudine,Selivant,Lisanor,Elidor,Brisen,Linet,Serene,Serien,Selwod,Ysabele,Karados,Peronell,Serenadine,Dinadan,Clarissant,Igraine,Aelfric,Herzeleide,Taulat,Zerbino,Iseut,Guivret,Madouc,Ygraine,Elisena,Rowena,Deirdre,Maelis,Herlews,Alundyne,Blasine,Dinas,Evalach,Rohais,Soredamors,Orguelleuse,Egletine,Fenice,Amide,Lionesse,Eliduc,Silvayne,Amadas,Amadis,Iaonice,Emerause,Ysabeau,Idonia,Alardin,Lessele,Evelake,Herzeleide,Carahes,Elyabel,Igrayne,Laudine,Guenloie,Isolt,Urgan,Yglais,Nimiane,Arabele,Amabel,Clarissant,Patrise,Navarre,Iseut,Guivret,Madouc,Ygraine,Elisena,Rowena,Deirdre,Maelis,Herlews,Alundyne,Blasine,Dinas,Evalach,Rohais,Soredamors,Orguelleuse,Egletine,Fenice,Amide,Lionesse,Eliduc,Silvayne,Amadas,Amadis,Iaonice,Emerause,Ysabeau,Idonia,Alardin,Lessele,Evelake,Herzeleide,Carahes,Elyabel,Igrayne,Laudine,Guenloie,Isolt,Urgan,Yglais,Nimiane,Arabele,Amabel";

        private const string hibMaleNames = "Aonghus,Breandán,Cian,Dallán,Eógan,Fearghal,Gréagóir,Iomhar,Lorcán,Máirtín,Neachtan,Odhrán,Páraic,Ruairí,Seosamh,Toiréasa,Áed,Beircheart,Colm,Domhnall,Éanna,Fergus,Goll,Irial,Liam,MacCon,Naoimhín,Ódhran,Pádraig,Ronán,Seánán,Tadhgán,Úilliam,Ailill,Bran,Cairbre,Daithi,Eoghan,Faolan,Gorm,Iollan,Lughaidh,Manannan,Niall,Oisin,Pádraig,Rónán,Séadna,Tadhg,Ultán,Alastar,Bairre,Caoilte,Dáire,Énna,Fiachra,Gairm,Imleach,Jarlath,Kian,Laoiseach,Malachy,Naoise,Odhrán,Páidín,Roibéard,Seamus,Turlough,Uilleag,Alastriona,Bairrfhionn,Caoimhe,Dymphna,Éabha,Fionnuala,Gráinne,Isolt,Laoise,Máire,Niamh,Oonagh,Pádraigín,Róisín,Saoirse,Teagan,Úna,Aoife,Bríd,Caitríona,Deirdre,Éibhlin,Fia,Gormlaith,Iseult,Jennifer,Kerstin,Léan,Máighréad,Nóirín,Órlaith,Plurabelle,Ríoghnach,Siobhán,Treasa,Úrsula,Aodh,Baird,Caoimhín,Dáire,Éamon,Fearghas,Gartlach,Íomhar,József,Lochlainn,Mánus,Naois,Óisin,Páidín,Roibeárd,Seaán,Tomás,Uilliam,Ailbhe,Bairrionn,Caoilinn,Dairine,Eabhnat,Fearchara,Gormfhlaith,Ite,Juliana,Kaitlín,Laochlann,Nollaig,Órnait,Pála,Roise,Seaghdha,Tomaltach,Uinseann,Ailbín,Bairrionn,Caoimhín,Dairine,Eabhnat,Fearchara,Gormfhlaith,Ite,Juliana,Kaitlín,Laochlann,Nollaig,Órnait,Pála,Roise,Seaghdha,Tomaltach,Uinseann";
        private const string hibFemaleNames = "Aibhlinn,Brighid,Caoilfhionn,Deirdre,Éabha,Fionnuala,Gráinne,Iseult,Jennifer,Kerstin,Léan,Máire,Niamh,Oonagh,Pádraigín,Róisín,Saoirse,Teagan,Úna,Aoife,Aisling,Bláthnat,Clíodhna,Dymphna,Éidín,Fíneachán,Gormfhlaith,Íomhar,Juliana,Kaitlín,Laoise,Máighréad,Nóirín,Órlaith,Plurabelle,Ríoghnach,Siobhán,Treasa,Úrsula,Ailbhe,Bairrfhionn,Caoilinn,Dairine,Éabhnat,Fearchara,Gormlaith,Ite,Laochlann,Máirtín,Nollaig,Órnait,Pála,Roise,Seaghdha,Tomaltach,Uinseann,Ailbín,Ailis,Bláth,Dairín,Éadaoin,Fionn,Grá,Iseabal,Jacinta,Káit,Laoiseach,Máire,Nuala,Órfhlaith,Póilín,Saibh,Téadgh";

        private const string midMaleNames = "Agnar,Bjorn,Dagur,Eirik,Fjolnir,Geir,Haldor,Ivar,Jarl,Kjartan,Leif,Magnus,Njall,Orvar,Ragnald,Sigbjorn,Thrain,Ulf,Vifil,Arni,Bardi,Dain,Einar,Faldan,Grettir,Hogni,Ingvar,Jokul,Koll,Leiknir,Mord,Nikul,Ornolf,Ragnvald,Sigmund,Thorfinn,Ulfar,Vali,Yngvar,Asgeir,Bolli,Darri,Egill,Flosi,Gisli,Hjortur,Ingolf,Jokull,Kolbeinn,Leikur,Mordur,Nils,Orri,Ragnaldur,Sigurdur,Thormundur,Ulfur,Valur,Yngvi,Arnstein,Bardur,David,Egill,Flosi,Gisli,Hjortur,Ingolf,Jokull,Kolbeinn,Leikur,Mordur,Nils,Orri,Ragnaldur,Sigurdur,Thormundur,Ulfur,Valur,Yngvi,Arnstein,Bardur,David,Eik,Fridgeir,Grimur,Hafthor,Ivar,Jorundur,Kari,Ljotur,Mord,Nokkvi,Oddur,Rafn,Steinar,Thorir,Valgard,Yngve,Askur,Baldur,Dagr,Eirikur,Fridleif";
        private const string midFemaleNames = "Aesa,Bjorg,Dalla,Edda,Fjola,Gerd,Halla,Inga,Jora,Kari,Lina,Marna,Njola,Orna,Ragna,Sif,Thora,Ulfhild,Vika,Alva,Bodil,Dagny,Eira,Frida,Gisla,Hildur,Ingibjorg,Jofrid,Kolfinna,Leidr,Mina,Olina,Ragnheid,Sigrid,Thordis,Una,Yrsa,Asgerd,Bergthora,Eilif,Flosa,Gudrid,Hjordis,Ingimund,Jolninna,Lidgerd,Mjoll,Oddny,Ranveig,Sigrun,Thorhalla,Valdis,Alfhild,Bardis,Davida,Eilika,Fridleif,Gudrun,Hjortur,Jokulina,Kolfinna,Leiknir,Mordur,Njall,Orvar,Ragnald,Sigbjorn,Thrain,Ulf,Vifil,Arnstein,Bardur,David,Egill,Fridgeir,Grimur,Hafthor,Ivar,Jorundur,Kari,Ljotur,Mord,Nokkvi,Oddur,Rafn,Steinar,Thorir,Valgard,Yngve,Askur,Baldur,Dagr,Eirikur,Fridleif,Grimur,Halfdan,Ivarr,Kjell,Ljung,Nikul,Ornolf,Ragnvald,Sigurdur,Thormundur,Ulfur,Valur,Yngvi";

        public static string GetName(eGender gender, eRealm realm)
        {
            string[] names = new string[0];

            switch (realm)
            {
                case eRealm.Albion:
                if (gender == eGender.Male)
                    names = albMaleNames.Split(',');
                else
                    names = albFemaleNames.Split(',');
                break;

                case eRealm.Hibernia:
                if (gender == eGender.Male)
                    names = hibMaleNames.Split(',');
                else
                    names = hibFemaleNames.Split(",");
                break;

                case eRealm.Midgard:
                if (gender == eGender.Male)
                    names = midMaleNames.Split(',');
                else
                    names = midFemaleNames.Split(",");
                break;
            }

            int randomIndex = Util.Random(names.Length - 1);

            return names[randomIndex];
        }
    }
}