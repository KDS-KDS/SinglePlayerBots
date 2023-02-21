using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;
using DOL.Language;

namespace DOL.GS
{
    //this component will hold all data related to casting spells
    public class CastingComponent
    {
        //entity casting the spell
        public GameLiving owner;
        public int EntityManagerId { get; set; } = EntityManager.UNSET_ID;

        public  bool IsCasting
        {
            get { return spellHandler != null && spellHandler.IsCasting; }
        }

        //data for the spell that they are casting
        public ISpellHandler spellHandler;

        //data for the spell they want to queue
        public ISpellHandler queuedSpellHandler;

        //data for instant spells 
        public ISpellHandler instantSpellHandler;

        public CastingComponent(GameLiving owner)
        {
            this.owner = owner;
        }

        public void Tick(long time)
        {
            spellHandler?.Tick(time);
        }

        public bool StartCastSpell(Spell spell, SpellLine line, ISpellCastingAbilityHandler spellCastingAbilityHandler = null, GameLiving target = null)
        {
            if (EntityManagerId == -1)
                EntityManagerId = EntityManager.Add(EntityManager.EntityType.CastingComponent, this);

            //Check for Conditions to Cast
            if (owner is GamePlayer playerOwner)
            {
                if (!CanCastSpell(playerOwner))
                    return false; 

                // Unstealth when we start casting (NS/Ranger/Hunter).
                if (playerOwner.IsStealthed)
                    playerOwner.Stealth(false);
            }

            ISpellHandler m_newSpellHandler = ScriptMgr.CreateSpellHandler(owner, spell, line);

            // 'GameLiving.TargetObject' is used by 'SpellHandler.Tick()' but is likely to change during LoS checks or for queued spells (affects NPCs only).
            // So we pre-initialize 'SpellHandler.Target' with the passed down target, if there's any.
            if (target != null)
                m_newSpellHandler.Target = target;

            // Abilities that cast spells (i.e. Realm Abilities such as Volcanic Pillar) need to set this so the associated ability gets disabled if the cast is successful.
            m_newSpellHandler.Ability = spellCastingAbilityHandler;

            // Performing the first tick here since 'SpellHandler' relies on the owner's target, which may get cleared before 'Tick()' is called by the casting service.
            if (spellHandler != null)
            {
                if (spellHandler.Spell != null && spellHandler.Spell.IsFocus)
                {
                    if (m_newSpellHandler.Spell.IsInstantCast)
                        TickThenReplaceSpellHandler(ref instantSpellHandler, m_newSpellHandler);
                    else
                        TickThenReplaceSpellHandler(ref spellHandler, m_newSpellHandler);
                }
                else if (m_newSpellHandler.Spell.IsInstantCast)
                    TickThenReplaceSpellHandler(ref instantSpellHandler, m_newSpellHandler);
                else
                {
                    if (owner is GamePlayer pl)
                    {
                        if (spell.CastTime > 0 && !(spellHandler is ChamberSpellHandler) && spell.SpellType != (byte)eSpellType.Chamber)
                        {
                            if (spellHandler.Spell.InstrumentRequirement != 0)
                            {
                                if (spell.InstrumentRequirement != 0)
                                    pl.Out.SendMessage(LanguageMgr.GetTranslation(pl.Client.Account.Language, "GamePlayer.CastSpell.AlreadyPlaySong"), eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                                else
                                    pl.Out.SendMessage("You must wait " + (((spellHandler.CastStartTick + spellHandler.Spell.CastTime) - GameLoop.GameLoopTime) / 1000 + 1).ToString() + " seconds to cast a spell!", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                                return false;
                            }
                        }
                        if (pl.SpellQueue)
                        {
                            pl.Out.SendMessage("You are already casting a spell! You prepare this spell as a follow up!", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                            queuedSpellHandler = m_newSpellHandler;
                        }
                        else
                            pl.Out.SendMessage("You are already casting a spell!", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                    }
                    else if (owner is GameNPC npcOwner && npcOwner.Brain is IControlledBrain)
                        queuedSpellHandler = m_newSpellHandler;
                }
            }
            else
            {
                if (m_newSpellHandler.Spell.IsInstantCast)
                    TickThenReplaceSpellHandler(ref instantSpellHandler, m_newSpellHandler);
                else
                    TickThenReplaceSpellHandler(ref spellHandler, m_newSpellHandler);

                //Special CastSpell rules
                if (spellHandler is SummonNecromancerPet necroPetHandler)
                    necroPetHandler.SetConAndHitsBonus();
            }

            return true;
        }

        private bool CanCastSpell(GameLiving living)
        {
            var p = living as GamePlayer;
            /*
            if (spellHandler != null)
            {
                p.Out.SendMessage("You are already casting a spell.", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                return false;
            }*/
            if (p.effectListComponent.ContainsEffectForEffectType(eEffect.Volley))//Volley check, players can't cast spells under volley effect
            {
                p.Out.SendMessage("You can't cast spells while Volley is active!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return false;
            }
            if (p != null && p.IsCrafting)
            {
                p.Out.SendMessage(LanguageMgr.GetTranslation(p.Client.Account.Language, "GamePlayer.Attack.InterruptedCrafting"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                //p.CraftTimer.Stop();
                p.craftComponent.StopCraft();
                p.CraftTimer = null;
                p.Out.SendCloseTimerWindow();
            }

            if (p != null && p.IsSalvagingOrRepairing)
            {
                p.Out.SendMessage(LanguageMgr.GetTranslation(p.Client.Account.Language, "GamePlayer.Attack.InterruptedCrafting"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                p.CraftTimer.Stop();
                p.CraftTimer = null;
                p.Out.SendCloseTimerWindow();
            }

            if (living != null)
            {
                if (living.IsStunned)
                {
                    p?.Out.SendMessage(LanguageMgr.GetTranslation(p.Client.Account.Language, "GamePlayer.CastSpell.CantCastStunned"), eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                    return false;
                }
                if (living.IsMezzed)
                {
                    p?.Out.SendMessage(LanguageMgr.GetTranslation(p.Client.Account.Language, "GamePlayer.CastSpell.CantCastMezzed"), eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
                    return false;
                }

                if (living.IsSilenced)
                {
                    p?.Out.SendMessage(LanguageMgr.GetTranslation(p.Client.Account.Language, "GamePlayer.CastSpell.CantCastFumblingWords"), eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
                    return false;
                }
            }
            return true;
        }

        private static void TickThenReplaceSpellHandler(ref ISpellHandler oldSpellHandler, ISpellHandler newSpellHandler)
        {
            newSpellHandler.Tick(GameLoop.GameLoopTime);
            oldSpellHandler = newSpellHandler;
        }
    }
}