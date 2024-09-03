using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Events;
using DOL.GS.Housing;
using DOL.GS.Keeps;
using DOL.GS.Utils;
using DOL.Language;
using log4net;

namespace DOL.GS.PacketHandler.Client.v168
{
	[PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.PlayerInitRequest, "Region Entering Init Request", eClientStatus.PlayerInGame)]
	public class PlayerInitRequestHandler : IPacketHandler
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public void HandlePacket(GameClient client, GSPacketIn packet)
		{
			new PlayerInitRequestAction(client.Player).Start(1);
		}

		/// <summary>
		/// Handles player init requests
		/// </summary>
		protected class PlayerInitRequestAction : ECSGameTimerWrapperBase
		{
			/// <summary>
			/// Constructs a new PlayerInitRequestHandler
			/// </summary>
			/// <param name="actionSource"></param>
			public PlayerInitRequestAction(GamePlayer actionSource) : base(actionSource)
			{
			}

			/// <summary>
			/// Called on every timer tick
			/// </summary>
			protected override int OnTick(ECSGameTimer timer)
			{
				GamePlayer player = (GamePlayer) timer.Owner;

				player.Out.SendUpdatePoints();
				player.TargetObject = null;
				// update the region color scheme which may be wrong due to ALLOW_ALL_REALMS support
				player.Out.SendRegionColorScheme();
				if (player.CurrentRegion != null)
				{
					player.CurrentRegion.Notify(RegionEvent.PlayerEnter, player.CurrentRegion, new RegionPlayerEventArgs(player));
					player.Out.SendPlayerRevive(player);
				}

				player.Out.SendTime();

				bool checkInstanceLogin = false;
				bool updateTempProperties = false;
				// if player is entering the game on this playerinit
				if (!player.EnteredGame)
				{
					updateTempProperties = true;
					player.EnteredGame = true;
					player.Notify(GamePlayerEvent.GameEntered, player);
					//ShowPatchNotes(player);
					//player.EffectList.RestoreAllEffects();
					EffectService.RestoreAllEffects(player);
					checkInstanceLogin = true;
				}
				else
				{
					player.Notify(GamePlayerEvent.RegionChanged, player);
				}
				if (player.TempProperties.GetProperty<bool>(GamePlayer.RELEASING_PROPERTY))
				{
					player.TempProperties.RemoveProperty(GamePlayer.RELEASING_PROPERTY);
					player.Notify(GamePlayerEvent.Revive, player);
					player.Notify(GamePlayerEvent.Released, player);
				}
				
				if (player.Group != null)
				{
					player.Group.UpdateGroupWindow();
					player.Group.UpdateAllToMember(player, true, false);
					player.Group.UpdateMember(player, true, true);
				}
				player.Out.SendPlayerInitFinished(0);
				player.TargetObject = null;
				player.StartHealthRegeneration();
				player.StartPowerRegeneration();
				player.StartEnduranceRegeneration();

				if (player.Guild != null)
				{
					SendGuildMessagesToPlayer(player);
				}
				SendHouseRentRemindersToPlayer(player);
				if (player.Level > 1 && ServerProperties.Properties.MOTD != string.Empty)
				{
					player.Out.SendMessage(ServerProperties.Properties.MOTD, eChatType.CT_System, eChatLoc.CL_SystemWindow);
				}
				else if (player.Level == 1)
				{
					player.Out.SendStarterHelp();
				}
				
				


				if (ServerProperties.Properties.ENABLE_DEBUG)
					player.Out.SendMessage("Server is running in DEBUG mode!", eChatType.CT_System, eChatLoc.CL_SystemWindow);

				// player.Out.SendPlayerFreeLevelUpdate();
				// if (player.FreeLevelState == 2)
				// {
				// 	player.Out.SendDialogBox(eDialogCode.SimpleWarning, 0, 0, 0, 0, eDialogType.Ok, true,
				// 	                         LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerInitRequestHandler.FreeLevel"));
				// }
				// player.Out.SendMasterLevelWindow(0);
				// AssemblyName an = Assembly.GetExecutingAssembly().GetName();
				// player.Out.SendMessage("Dawn of Light " + an.Name + " Version: " + an.Version, eChatType.CT_System,
				//                        eChatLoc.CL_SystemWindow);


				if (ServerProperties.Properties.TELEPORT_LOGIN_NEAR_ENEMY_KEEP)
				{
					CheckIfPlayerLogsNearEnemyKeepAndMoveIfNecessary(player);

					//Check for logging in near own keep/relic keep if its under attack. Prevents people from logging toons in relic keeps.
					CheckIfPlayerLogsNearKeepUnderAttackAndMoveIfNecessary(player);
				}

				if (ServerProperties.Properties.TELEPORT_LOGIN_BG_LEVEL_EXCEEDED)
				{
					CheckBGLevelCapForPlayerAndMoveIfNecessary(player);
				}

				//Check realmtimer and move player to bind if realm timer is not for this realm.
				RealmTimer.CheckRealmTimer(player);

				if (checkInstanceLogin)
				{
					if (WorldMgr.Regions[player.CurrentRegionID] == null || player.CurrentRegion == null || player.CurrentRegion.IsInstance)
					{
						Log.WarnFormat("{0}:{1} logging into instance or CurrentRegion is null, moving to bind!", player.Name, player.Client.Account.Name);
						player.MoveToBind();
					}
				}

				if (player.IsUnderwater)
				{
					player.IsDiving = true;
				}
				player.Client.ClientState = GameClient.eClientState.Playing;

				#region TempPropertiesManager LookUp

				if (updateTempProperties)
				{
					if (ServerProperties.Properties.ACTIVATE_TEMP_PROPERTIES_MANAGER_CHECKUP)
					{
						try
						{
							IList<TempPropertiesManager.TempPropContainer> TempPropContainerList = TempPropertiesManager.TempPropContainerList.Where(item => item.OwnerID == player.DBCharacter.ObjectId).ToList();

							foreach (TempPropertiesManager.TempPropContainer container in TempPropContainerList)
							{
								long longresult = 0;
								if (long.TryParse(container.Value, out longresult))
								{
									player.TempProperties.SetProperty(container.TempPropString, longresult);

									if (ServerProperties.Properties.ACTIVATE_TEMP_PROPERTIES_MANAGER_CHECKUP_DEBUG)
										Log.Debug("Container " + container.TempPropString + " with value " + container.Value + " for player " + player.Name + " was removed from container list, tempproperties added");
								}
									TempPropertiesManager.TempPropContainerList.TryRemove(container);
							}
						}
						catch (Exception e)
						{
							Log.Debug("Error in TempProproperties Manager when searching TempProperties to apply: " + e.ToString());
						}
					}
				}

				#endregion TempPropertiesManager LookUp

				return 0;
			}

			private static void ShowPatchNotes(GamePlayer player)
			{
				if (player.Client.HasSeenPatchNotes)
					return;

				player.Out.SendCustomTextWindow($"Server News {DateTime.Today:d}", GameServer.Instance.PatchNotes);
				player.Client.HasSeenPatchNotes = true;
			}

			private static void CheckBGLevelCapForPlayerAndMoveIfNecessary(GamePlayer player)
			{
				if (player.Client.Account.PrivLevel == 1 && player.CurrentRegion.IsRvR && player.CurrentRegionID != 163)
				{
					ICollection<AbstractGameKeep> list = GameServer.KeepManager.GetKeepsOfRegion(player.CurrentRegionID);


					foreach (AbstractGameKeep k in list)
					{
						if (k.BaseLevel >= 50)
							continue;

						if (player.Level > k.BaseLevel)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerInitRequestHandler.LevelCap"),
							                       eChatType.CT_YouWereHit, eChatLoc.CL_SystemWindow);
							player.MoveTo((ushort) player.BindRegion, player.BindXpos,
							              player.BindYpos, player.BindZpos,
							              (ushort) player.BindHeading);
							break;
						}
					}
				}
			}

			private static void CheckIfPlayerLogsNearEnemyKeepAndMoveIfNecessary(GamePlayer player)
			{
				if (player.CurrentRegion.IsInstance)
				{
					WorldMgr.RvrLinkDeadPlayers.TryRemove(player.InternalID, out _);
					return;
				}

				int gracePeriodInMinutes = 0;
				Int32.TryParse(ServerProperties.Properties.RVR_LINK_DEATH_RELOG_GRACE_PERIOD, out gracePeriodInMinutes);
				AbstractGameKeep keep = GameServer.KeepManager.GetKeepCloseToSpot(player.CurrentRegionID, player, WorldMgr.VISIBILITY_DISTANCE);
				if (keep != null && player.Client.Account.PrivLevel == 1 && GameServer.KeepManager.IsEnemy(keep, player))
				{
					if (WorldMgr.RvrLinkDeadPlayers.TryGetValue(player.InternalID, out DateTime value))
					{
						if (DateTime.Now.Subtract(new TimeSpan(0, gracePeriodInMinutes, 0)) > value)
						{
							SendMessageAndMoveToSafeLocation(player);
						}
					}
					else
					{
						SendMessageAndMoveToSafeLocation(player);
					}
				}			
				var linkDeadPlayerIds = new string[WorldMgr.RvrLinkDeadPlayers.Count];
				WorldMgr.RvrLinkDeadPlayers.Keys.CopyTo(linkDeadPlayerIds, 0);
				foreach (string playerId in linkDeadPlayerIds)
				{
					if (playerId != null && DateTime.Now.Subtract(new TimeSpan(0, gracePeriodInMinutes, 0)) > WorldMgr.RvrLinkDeadPlayers[playerId])
					{
						WorldMgr.RvrLinkDeadPlayers.TryRemove(playerId, out _);
					}
				}
			}

			//Move player to safe spot if they login near keep/relic keep that is under attack
			private static void CheckIfPlayerLogsNearKeepUnderAttackAndMoveIfNecessary(GamePlayer player)
			{
				int gracePeriodInMinutes = 0;
				Int32.TryParse(ServerProperties.Properties.RVR_LINK_DEATH_RELOG_GRACE_PERIOD, out gracePeriodInMinutes);
				AbstractGameKeep keep = GameServer.KeepManager.GetKeepCloseToSpot(player.CurrentRegionID, player, WorldMgr.VISIBILITY_DISTANCE);
				if (keep != null && keep.InCombat && player.Client.Account.PrivLevel == 1 && !GameServer.KeepManager.IsEnemy(keep, player))
				{
					if (WorldMgr.RvrLinkDeadPlayers.TryGetValue(player.InternalID, out DateTime value))
					{
						if (DateTime.Now.Subtract(new TimeSpan(0, gracePeriodInMinutes, 0)) > value)
						{
							SendMessageAndMoveToSafeLocation(player);
						}
					}
					else
					{
						SendMessageAndMoveToSafeLocation(player);
					}
				}
				
			}

			private static void SendMessageAndMoveToSafeLocation(GamePlayer player)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerInitRequestHandler.SaferLocation"),
				                       eChatType.CT_System, eChatLoc.CL_SystemWindow);
				player.MoveTo((ushort) player.BindRegion, player.BindXpos,
				              player.BindYpos, player.BindZpos,
				              (ushort) player.BindHeading);
			}

			private static void SendHouseRentRemindersToPlayer(GamePlayer player)
			{
				House house = HouseMgr.GetHouseByPlayer(player);

				if (house != null)
				{
					TimeSpan due = (house.LastPaid.AddDays(ServerProperties.Properties.RENT_DUE_DAYS).AddHours(1) - DateTime.Now);
					if ((due.Days <= 0 || due.Days < ServerProperties.Properties.RENT_DUE_DAYS) && house.KeptMoney < HouseMgr.GetRentByModel(house.Model))
					{
						//Sending reminder as Text Window as the Help window wasnt properly popping up on the client
						var message = new List<string>();
						message.Add("Rent for personal house " + house.HouseNumber + " due in " + due.Days + " days!");
						player.Out.SendCustomTextWindow("Personal House Rent Reminder", message);
						//player.Out.SendRentReminder(house);
					}
				}

				if (player.Guild != null)
				{
					House ghouse = HouseMgr.GetGuildHouseByPlayer(player);
					if (ghouse != null)
					{
						TimeSpan due = (ghouse.LastPaid.AddDays(ServerProperties.Properties.RENT_DUE_DAYS).AddHours(1) - DateTime.Now);
						if ((due.Days <= 0 || due.Days < ServerProperties.Properties.RENT_DUE_DAYS) && ghouse.KeptMoney < HouseMgr.GetRentByModel(ghouse.Model))
						{
							//Sending reminder as Text Window as the Help window wasnt properly popping up on the client
							var guildmessage = new List<string>();
							guildmessage.Add("Rent for guild house " + ghouse.HouseNumber + " due in " + due.Days + " days!");
							player.Out.SendCustomTextWindow("Guild House Rent Reminder", guildmessage);
							//player.Out.SendRentReminder(ghouse);
						}
					}
				}
			}

			private static void SendGuildMessagesToPlayer(GamePlayer player)
			{
				try
				{
					if (player.GuildRank.GcHear && player.Guild.Motd != string.Empty)
					{
						player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerInitRequestHandler.GuildMessage"),
						                       eChatType.CT_System, eChatLoc.CL_SystemWindow);
						player.Out.SendMessage(player.Guild.Motd, eChatType.CT_System, eChatLoc.CL_SystemWindow);
					}
					if (player.GuildRank.OcHear && player.Guild.Omotd != string.Empty)
					{
						player.Out.SendMessage(
							LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerInitRequestHandler.OfficerMessage", player.Guild.Omotd),
							eChatType.CT_System, eChatLoc.CL_SystemWindow);
					}
					if (player.Guild.alliance != null && player.GuildRank.AcHear && player.Guild.alliance.Dballiance.Motd != string.Empty)
					{
						player.Out.SendMessage(
							LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerInitRequestHandler.AllianceMessage",
							                           player.Guild.alliance.Dballiance.Motd), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					}
				}
				catch (Exception ex)
				{
					Log.Error("SendGuildMessageToPlayer exception, missing guild ranks for guild: " + player.Guild.Name + "?", ex);
					if (player != null)
					{
						player.Out.SendMessage(
							"There was an error sending motd for your guild. Guild ranks may be missing or corrupted.",
							eChatType.CT_Important, eChatLoc.CL_SystemWindow);
					}
				}
			}
		}
	}
}
