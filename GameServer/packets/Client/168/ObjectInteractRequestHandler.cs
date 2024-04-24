namespace DOL.GS.PacketHandler.Client.v168
{
	[PacketHandlerAttribute(PacketHandlerType.TCP, eClientPackets.ObjectInteractRequest, "Handles Client Interact Request", eClientStatus.PlayerInGame)]
	public class ObjectInteractRequestHandler : IPacketHandler
	{
		public void HandlePacket(GameClient client, GSPacketIn packet)
		{
			// packet.Skip(10);
			uint playerX = packet.ReadInt();
			uint playerY = packet.ReadInt();
			int sessionId = packet.ReadShort();
			ushort targetOid = packet.ReadShort();

			//TODO: utilize these client-sent coordinates to possibly check for exploits which are spoofing position packets but not spoofing them everywhere
			new InteractActionHandler(client.Player, targetOid).Start(1);
		}

		/// <summary>
		/// Handles player interact actions
		/// </summary>
		protected class InteractActionHandler : ECSGameTimerWrapperBase
		{
			/// <summary>
			/// The interact target OID
			/// </summary>
			protected readonly ushort m_targetOid;

			/// <summary>
			/// Constructs a new InterractActionHandler
			/// </summary>
			/// <param name="actionSource">The action source</param>
			/// <param name="targetOid">The interact target OID</param>
			public InteractActionHandler(GamePlayer actionSource, ushort targetOid) : base(actionSource)
			{
				m_targetOid = targetOid;
			}

			/// <summary>
			/// Called on every timer tick
			/// </summary>
			protected override int OnTick(ECSGameTimer timer)
			{
				GamePlayer player = (GamePlayer) timer.Owner;
				Region region = player.CurrentRegion;

				if (region == null)
					return 0;

				GameObject obj = region.GetObject(m_targetOid);

				if (obj == null || !player.IsWithinRadius(obj, WorldMgr.OBJ_UPDATE_DISTANCE))
					player.Out.SendObjectDelete(m_targetOid);
				else
					obj.Interact(player);

				return 0;
			}
		}
	}
}
