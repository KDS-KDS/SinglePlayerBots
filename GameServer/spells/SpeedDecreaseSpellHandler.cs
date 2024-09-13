using System;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Spell handler for speed decreasing spells
	/// </summary>
	[SpellHandler(eSpellType.SpeedDecrease)]
	public class SpeedDecreaseSpellHandler : UnbreakableSpeedDecreaseSpellHandler
	{
		private bool crit = false;
		public override ECSGameSpellEffect CreateECSEffect(ECSGameEffectInitParams initParams)
		{
			if (crit)
				initParams.Effectiveness *= 2; //critical hit effectiveness needs to be set after duration is calculated to prevent double duration

			return new StatDebuffECSEffect(initParams);
		}

		public override void ApplyEffectOnTarget(GameLiving target)
		{
			// Check for root immunity.
			if (Spell.Value == 99 && (target.effectListComponent.Effects.ContainsKey(eEffect.SnareImmunity) || target.effectListComponent.Effects.ContainsKey(eEffect.SpeedOfSound)))
				//FindStaticEffectOnTarget(target, typeof(MezzRootImmunityEffect)) != null)
			{
				MessageToCaster("Your target is immune!", eChatType.CT_SpellResisted);
				target.StartInterruptTimer(target.SpellInterruptDuration, AttackData.eAttackType.Spell, Caster);
				OnSpellResisted(target);
				return;
			}


			//check for existing effect
			// var debuffs = target.effectListComponent.GetSpellEffects(eEffect.MovementSpeedDebuff);

			// foreach (var debuff in debuffs)
			// {
			// 	if (debuff.SpellHandler.Spell.Value >= Spell.Value)
			// 	{
			// 		// Old Spell is Better than new one
			// 		SendSpellResistAnimation(target);
			// 		this.MessageToCaster(eChatType.CT_SpellResisted, "{0} already has that effect.", target.GetName(0, true));
			// 		MessageToCaster("Wait until it expires. Spell Failed.", eChatType.CT_SpellResisted);
			// 		// Prevent Adding.
			// 		return;
			// 	}
			// }

			int criticalChance = Caster.DebuffCriticalChance;

			if (criticalChance > 0)
			{
				int randNum = Util.CryptoNextInt(0, 100);
				int critCap = Math.Min(50, criticalChance);
				GamePlayer playerCaster = Caster as GamePlayer;

				if (playerCaster?.UseDetailedCombatLog == true && critCap > 0)
					playerCaster.Out.SendMessage($"Debuff crit chance: {critCap} random: {randNum}", eChatType.CT_DamageAdd, eChatLoc.CL_SystemWindow);

				if (critCap > randNum)
				{
					crit = true;
					playerCaster?.Out.SendMessage($"Your snare is doubly effective!", eChatType.CT_YouHit, eChatLoc.CL_SystemWindow);
				}
			}
			
			base.ApplyEffectOnTarget(target);
		}

		/// <summary>
		/// When an applied effect starts
		/// duration spells only
		/// </summary>
		/// <param name="effect"></param>
		public override void OnEffectStart(GameSpellEffect effect)
		{
			// Cannot apply if the effect owner has a charging effect
			if (effect.Owner.EffectList.GetOfType<ChargeEffect>() != null || effect.Owner.effectListComponent.Effects.ContainsKey(eEffect.SpeedOfSound) || effect.Owner.TempProperties.GetProperty<bool>("Charging"))
			{
				MessageToCaster(effect.Owner.Name + " is moving too fast for this spell to have any effect!", eChatType.CT_SpellResisted);
				return;
			}
			base.OnEffectStart(effect);
			GameEventMgr.AddHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(OnAttacked));
			// Cancels mezz on the effect owner, if applied
			//GameSpellEffect mezz = SpellHandler.FindEffectOnTarget(effect.Owner, "Mesmerize");
			ECSGameEffect mezz = EffectListService.GetEffectOnTarget(effect.Owner, eEffect.Mez);
			if (mezz != null)
				EffectService.RequestImmediateCancelEffect(mezz);
				//mezz.Cancel(false);
		}

		/// <summary>
		/// When an applied effect expires.
		/// Duration spells only.
		/// </summary>
		/// <param name="effect">The expired effect</param>
		/// <param name="noMessages">true, when no messages should be sent to player and surrounding</param>
		/// <returns>immunity duration in milliseconds</returns>
		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			GameEventMgr.RemoveHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(OnAttacked));
			return base.OnEffectExpires(effect, noMessages);
		}

		/// <summary>
		/// Handles attack on buff owner
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="arguments"></param>
		protected virtual void OnAttacked(DOLEvent e, object sender, EventArgs arguments)
		{
			AttackedByEnemyEventArgs attackArgs = arguments as AttackedByEnemyEventArgs;
			GameLiving living = sender as GameLiving;
			if (attackArgs == null) return;
			if (living == null) return;

			switch (attackArgs.AttackData.AttackResult)
			{
				case eAttackResult.HitStyle:
				case eAttackResult.HitUnstyled:
					//GameSpellEffect effect = FindEffectOnTarget(living, this);
					ECSGameEffect effect = EffectListService.GetEffectOnTarget(living, eEffect.MovementSpeedDebuff);
					if (effect != null)
						EffectService.RequestImmediateCancelEffect(effect);
						//effect.Cancel(false);
					break;
			}
		}

		// constructor
		public SpeedDecreaseSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line)
		{
		}
	}
}
