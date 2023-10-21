using Psychedelic_Prism.Items;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.Audio;

namespace Psychedelic_Prism.Projectiles
{
	public class PsychedelicPrismMain : ModProjectile
	{
		public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.LastPrism;

		public float currentExpansion = 0;
		public double currentRotation = 0;
		private int Blink = 0;

		// The vanilla Last Prism is an animated item with 5 frames of animation. We copy that here.
		private const int NumAnimationFrames = 5;

		// This controls how many individual beams are fired by the Prism.
		public int NumBeams = 6;
		public int[] BeamIDs = new int[6];

		// This value controls how many frames it takes for the Prism to reach "max charge". 60 frames = 1 second.
		public const float MaxCharge = 240;

		// This value controls how sluggish the Prism turns while being used. Vanilla Last Prism is 0.08f.
		// Higher values make the Prism turn faster.
		private const float AimResponsiveness = 0.12f;
		public Item HeldItem;

		// This property encloses the internal AI variable projectile.ai[0]. It makes the code easier to read.
		public float FrameCounter {
			get => Projectile.ai[0];
			set => Projectile.ai[0] = value;
		}

		public int identity {
			get => Projectile.whoAmI;
			set => Projectile.whoAmI = value;
		}

		public override void SetStaticDefaults() {
			Main.projFrames[Projectile.type] = NumAnimationFrames;

			// Signals to Terraria that this projectile requires a unique identifier beyond its index in the projectile array.
			// This prevents the issue with the vanilla Last Prism where the beams are invisible in multiplayer.
			ProjectileID.Sets.NeedsUUID[Projectile.type] = true;
		}

		public override void SetDefaults() {
			// Use CloneDefaults to clone all basic projectile statistics from the vanilla Last Prism.
			Projectile.CloneDefaults(ProjectileID.LastPrism);
			Projectile.netImportant = true;
			Projectile.maxPenetrate = 2147483647;
			Projectile.friendly = false;
			Projectile.hostile = false;
			Projectile.penetrate = -1;
		}

		public override bool PreAI() {
			Player player = Main.player[Projectile.owner];
			// The Prism immediately stops functioning if the player is Cursed (player.noItems) or "Crowd Controlled", e.g. the Frozen debuff.
			// player.channel indicates whether the player is still holding down the mouse button to use the item.
			bool stillInUse = player.statLife > 0 && player.HeldItem != null && player.HeldItem.type == ModContent.ItemType<PsychedelicPrism>() && !player.noItems && !player.CCed && !player.ghost;

			// If the Prism cannot continue to be used, then destroy it immediately.
			if (!stillInUse) {
				Projectile.Kill();
				Projectile.active = false;
				if (HeldItem != null && HeldItem.type == ModContent.ItemType<PsychedelicPrism>()) {
					(HeldItem.ModItem as PsychedelicPrism).State = 0;
				}
				return false;
			}

			Projectile.stepSpeed = 3f;
			Projectile.restrikeDelay = 12792079;
			// Vector2 rrp = projectile.Center; // I tried to set it to the prism position but I don't think it did anything
			// Vector2 rrp = player.RotatedRelativePoint(player.MountedCenter, true); ~ this was the original code (using player position)

			float rotAngle = (float) currentRotation;
			float currDist = 120 * currentExpansion * currentExpansion / 32400;
			Vector2 rrp = player.MountedCenter + new Vector2((float) (currDist * Math.Cos(rotAngle)), (float) (currDist * Math.Sin(rotAngle)));

			// Update the Prism's damage every frame so that it is dynamically affected by Mana Sickness.
			// UpdateDamageForManaSickness(player);

			// Update the frame counter.
			FrameCounter += 1f;

			// Update projectile visuals and sound.
			UpdateAnimation();
			PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
			if (FrameCounter == 240 && (prism.State & 1) != 0) {
				// When all beams are focused, cut out 1/3 of them to save computation
				NumBeams = 4;
				Projectile temp;
				temp = Main.projectile[BeamIDs[4]];
				if (temp != null && temp.active) {
					temp.Kill();
				}
				temp = Main.projectile[BeamIDs[5]];
				if (temp != null && temp.active) {
					temp.Kill();
				}
			}
			Projectile.damage = player.HeldItem.damage;
			if (Projectile.identity == prism.PrismIDs[0]) {
				// if (FrameCounter == 393) {
					// SoundEngine.PlaySound(SoundID.Item66, Projectile.position);
				// }
				if (FrameCounter == 303 && (prism.State & 1) != 0) {
					SoundEngine.PlaySound(SoundID.Item162, Projectile.Center);
				}
				else if (FrameCounter == 1) {
					SoundEngine.PlaySound(SoundID.Item20, Projectile.Center);
				}
				else if (FrameCounter % 28 - 1 == 0 && (prism.State & 2) != 0) {
					SoundEngine.PlaySound(SoundID.Item8, Projectile.Center);
				}
				else if (FrameCounter % 28 - 15 == 0 && (prism.State & 1) != 0) {
					SoundEngine.PlaySound(SoundID.Item15, Projectile.Center);
				}
				for (int i = 0; i < Main.npc.Length; i++) {
					if (Main.npc[i] == null) {
						prism.NPCHealths[i] = -1;
						continue;
					}
					NPC npc = Main.npc[i];
					if (!npc.active || npc.friendly || npc.townNPC) {
						prism.NPCHealths[i] = -1;
						continue;
					}
					if (prism.NPCHealths[i] == -1 || prism.NPCHealths[i] >= npc.life) {
						// Main.NewText((npc.life - 1).ToString(), new Color(0, 0, 255));
						prism.NPCHealths[i] = npc.life - 1;
					}
				}
			}

			// Update the Prism's position in the world and relevant variables of the player holding it.
			UpdatePlayerVisuals(player, rrp);

			// Update the Prism's behavior: project beams on frame 1, consume mana, and despawn if out of mana.
			// Spawn in the Prism's lasers on the first frame if the player is capable of using the item.
			if (FrameCounter == 1f) {
				currentRotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
				HeldItem = player.HeldItem;
			}
			// Main.NewText(FrameCounter);
			if ((prism.State & 1) == 0 || player.HeldItem.type != ModContent.ItemType<PsychedelicPrism>()) {
				DelegateMethods.v3_1 = new Vector3(4, 4, 4);
				Utils.PlotTileLine(Projectile.Center, Projectile.Center + Vector2.Normalize(Projectile.velocity) * 8f, Projectile.width * Projectile.scale, DelegateMethods.CastLight);
				if (FrameCounter == 2f) {
					for (int b = 0; b < NumBeams; ++b) {
						Projectile beam = Main.projectile[BeamIDs[b]];
						if (beam != null && beam.type == ModContent.ProjectileType<PsychedelicPrismBeam>() && beam.active) {
							PsychedelicPrismBeam realBeam = beam.ModProjectile as PsychedelicPrismBeam;
							realBeam.Fading = true;
						}
					}
					for (int b = 0; b < 6; b++) {
						BeamIDs[b] = Main.projectile.Length - 1;
					}
					NumBeams = 0;
				}
			} else {
				NumBeams = 6;
				if (FrameCounter == 2f) {
					FireBeams();
				} else if (FrameCounter > 2f) {
					EnsureBeams();
				}
			}
			if (Blink > 0) {
				Blink--;
			}
			float req = (float) FrameCounter / 240f / 5f;
			if (req > 0.4f) req = 0.4f;
			if ((prism.State & 2) != 0 && Main.rand.NextFloat() < req && !NoSpawnProjectile(256)) {
				Vector2 targetPos = rrp;
				Vector2 targetVel = new(0, 0);
				float targetDist = 2400f;
				float distance;
				bool target = false;
				NPC targetNPC = null;
				double damage = 0;
				float bulletSpeed = 16f;
				if (player.HasMinionAttackTargetNPC) {
					NPC npc = Main.npc[player.MinionAttackTargetNPC];
					if (Collision.CanHitLine(Projectile.position, Projectile.width / 2, Projectile.height / 2, npc.position, npc.width / 2, npc.height / 2)) {
						targetDist = distance = Vector2.Distance(Projectile.Center, targetPos);
						targetPos = npc.Center;
						targetNPC = npc;
						target = true;
						double dmg = (int) (512 * (1 + npc.velocity.Length()) * (npc.life + npc.lifeMax) / Math.Pow(npc.width * npc.height, 1.5) + 1);
						if (npc.boss) dmg /= 8;
						if (damage < dmg) damage = dmg;
					}
				}
				if (!target) {
					for (int k = 0; k < Main.npc.Length; k++) {
						NPC npc = Main.npc[k];
						if (npc.CanBeChasedBy(this, false)) {
							distance = Vector2.Distance(npc.Center, Projectile.Center);
							if ((Main.rand.Next(0, 5) == 0 || !target) && distance < targetDist && Collision.CanHitLine(Projectile.position, Projectile.width, Projectile.height, npc.position, npc.width, npc.height)) {
								targetDist = distance;
								targetPos = npc.Center;
								targetNPC = npc;
								target = true;
								double dmg = (int) (512 * (1 + npc.velocity.Length()) * (npc.life + npc.lifeMax) / Math.Pow(npc.width * npc.height, 1.5) + 1);
								if (npc.boss) dmg /= 8;
								if (damage < dmg) damage = dmg;
							}
						}
					}
				}
				damage *= Projectile.damage;
				if (damage % 1 > Main.rand.NextFloat()) damage += 1;
				int dmg2 = (int) damage;
				if (target && dmg2 > 0) {
					Blink = 16;
					Vector2 A = rrp;
					Vector2 B = targetPos;
					Vector2 BC = targetNPC.velocity;
					Vector2 C;
					if (BC.X == 0 && BC.Y == 0) {
						C = B;
					}
					else {
						Vector2 H = B + targetNPC.velocity * 3600;
						double sinB = (A.X - B.X) * (H.Y - B.Y) - (A.Y - B.Y) * (H.X - B.X);
						sinB /= Math.Sqrt(Math.Pow(B.X - A.X, 2) + Math.Pow(B.Y - A.Y, 2)) * Math.Sqrt(Math.Pow(B.X - H.X, 2) + Math.Pow(B.Y - H.Y, 2));
						double sinA = targetNPC.velocity.Length() / bulletSpeed * sinB;
						double sinC = sinA * Math.Sqrt(1 - sinB * sinB) + sinB * Math.Sqrt(1 - sinA * sinA);
						double lengthBC = sinA / sinC * Vector2.Distance(A, B);
						BC.Normalize();
						BC *= (float) lengthBC / 2;
						C = B + BC;
						if (C.HasNaNs() || !Collision.CanHitLine(Projectile.position, Projectile.width / 2, Projectile.height / 2, C, targetNPC.width / 2, targetNPC.height / 2)) {
							C = B;
						}
					}
					Vector2 polar = C - rrp;
					polar.Normalize();
					polar *= bulletSpeed;
					IEntitySource source = Projectile.GetSource_FromThis();
					int[] choices = {14, 20, 36, 83, 84, 88, 89, 100, 104, 110, 180, 207, 242, 257, 264, 279, 283, 284, 285, 286, 287, 302, 337, 357, 389, 436, 438, 440, 449, 462, 576, 577, 591, 592, 606, 638, 731, 876, 981};
					int pid = choices[Main.rand.Next(0, choices.Length)];
					pid = Projectile.NewProjectile(source, rrp, polar, pid, dmg2, Projectile.knockBack * -2, player.whoAmI);
					Projectile newproj = Main.projectile[pid];
					newproj.active = true;
					newproj.velocity = polar;
					newproj.friendly = true;
					newproj.hostile = false;
					if (Main.rand.Next(0, 3) == 0) {
						SoundStyle[] choices2 = {
							SoundID.Item12,
							SoundID.Item25,
							SoundID.Item75,
							SoundID.Item91,
							SoundID.Item114,
							SoundID.Item115,
							SoundID.Item157,
							SoundID.Item158
						};
						SoundStyle sid = choices2[Main.rand.Next(0, choices2.Length)];
						Vector2 attenuated = new(player.position.X, player.position.Y - 2400);
						SoundEngine.PlaySound(sid, attenuated);
					}
				}
			}

			// Slightly re-aim the Prism every frame so that it gradually sweeps to point towards the mouse.
			UpdateAim(rrp, player.HeldItem.shootSpeed);

			// bool manaIsAvailable = true;
			float manaOverflow = (float) (player.statManaMax * 2 - player.statMana) / 60 / 5;
			if (manaOverflow % 1 > Main.rand.NextFloat()) manaOverflow += 1;
			player.statMana += (int) manaOverflow;

			// This ensures that the Prism never times out while in use.
			Projectile.timeLeft = 2147483647;
			return false;
		}

		private bool NoSpawnProjectile(int lim) {
			int count = 0;
			for (int k = 0; k < Main.projectile.Length; k++) {
				if (Main.projectile[k] == null) continue;
				Projectile proj = Main.projectile[k];
				if (!proj.active) continue;
				count++;
				if (count + lim >= Main.projectile.Length) return true;
			}
			return false;
		}

		private void ForceSpawnProjectile(int count) {
			int i = 0;
			int j = 0;
			while (i < count) {
				int x = Main.rand.Next(0, Main.projectile.Length);
				Projectile proj = Main.projectile[x];
				if (proj.type != ModContent.ProjectileType<PsychedelicPrismMain>() && proj.type != ModContent.ProjectileType<PsychedelicPrismBeam>() || j >= Main.projectile.Length) {
					proj.Kill();
					proj.active = false;
					i++;
				}
				j++;
			}
		}

		public override bool? CanDamage() {
			return false;
		}

		// private void UpdateDamageForManaSickness(Player player)
		// {
			// float ownerCurrentMagicDamage = player.allDamage + (player.magicDamage - 1f);
			// Projectile.damage = (int) (player.HeldItem.damage * ownerCurrentMagicDamage);
		// }

		private void UpdateAnimation()
		{
			Projectile.frameCounter++;

			// As the Prism charges up and focuses the beams, its animation plays faster.
			int framesPerAnimationUpdate = FrameCounter >= MaxCharge ? 2 : FrameCounter >= (MaxCharge * 0.66f) ? 3 : 4;

			// If necessary, change which specific frame of the animation is displayed.
			if (Projectile.frameCounter >= framesPerAnimationUpdate) {
				Projectile.frameCounter = 0;
				if (++Projectile.frame >= NumAnimationFrames) {
					Projectile.frame = 0;
				}
			}
		}

		private void UpdatePlayerVisuals(Player player, Vector2 playerHandPos)
		{
			// Place the Prism directly into the player's hand at all times.
			// projectile.Center = playerHandPos;
			// The beams emit from the tip of the Prism, not the side. As such, rotate the sprite by pi/2 (90 degrees).
			Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

			Projectile.spriteDirection = Projectile.direction;

			// The Prism is a holdout projectile, so change the player's variables to reflect that.
			// Constantly resetting player.itemTime and player.itemAnimation prevents the player from switching items or doing anything else.
			// player.ChangeDir(Projectile.direction);
			// player.heldProj = Projectile.whoAmI;
			// player.itemTime = 2;
			// player.itemAnimation = 2;

			// If you do not multiply by projectile.direction, the player's hand will point the wrong direction while facing left.
			// player.itemRotation = (Projectile.velocity * Projectile.direction).ToRotation();

			float rotAngle = (float) currentRotation;
			currentRotation += Math.PI / 60 * currentExpansion / 180 / player.GetAttackSpeed<Terraria.ModLoader.SummonMeleeSpeedDamageClass>();
			if (currentExpansion < 180) {
				currentExpansion += 1;
			}
			float currDist = 120 * currentExpansion * currentExpansion / 32400;
			Vector2 un_offset = new((float) (player.HeldItem.shootSpeed * Math.Cos(Projectile.rotation - MathHelper.PiOver2)), (float) (player.HeldItem.shootSpeed * Math.Sin(Projectile.rotation - MathHelper.PiOver2)));
			Projectile.Center = player.MountedCenter + new Vector2((float) (currDist * Math.Cos(rotAngle)), (float) (currDist * Math.Sin(rotAngle))) - un_offset;
		}

		private void UpdateAim(Vector2 source, float speed)
		{
			// Get the player's current aiming direction as a normalized vector.
			Player player = Main.player[Projectile.owner];
			Vector2 mpos = Main.MouseWorld;
			if (Vector2.Distance(mpos, player.MountedCenter) < 80) {
				mpos = player.MountedCenter;
			}
			Vector2 aim;
			PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
			if (prism.State == 2) {
				aim = Vector2.Normalize(source - player.MountedCenter);
				if (aim.HasNaNs()) {
					aim = Vector2.Normalize(Projectile.Center - player.MountedCenter);
					if (aim.HasNaNs()) aim = -Vector2.UnitY;
				}
			} else {
				aim = Vector2.Normalize(mpos - source);
				if (aim.HasNaNs()) {
					aim = Vector2.Normalize(mpos - Projectile.Center);
					if (aim.HasNaNs()) aim = -Vector2.UnitY;
				}
			}
			// Change a portion of the Prism's current velocity so that it points to the mouse. This gives smooth movement over time.
			Vector2 orig = Vector2.Normalize(Projectile.velocity);
			aim = Vector2.Normalize(Vector2.Lerp(orig, aim, AimResponsiveness));
			aim *= speed;

			if ((aim - Projectile.velocity).Length() > 0.0625f) {
				Projectile.netUpdate = true;
			}
			Projectile.velocity = aim;
		}

		private void FireBeams() {
			// If for some reason the beam velocity can't be correctly normalized, set it to a default value.
			Vector2 beamVelocity = Vector2.Normalize(Projectile.velocity);
			if (beamVelocity.HasNaNs()) {
				beamVelocity = -Vector2.UnitY;
			}

			// This UUID will be the same between all players in multiplayer, ensuring that the beams are properly anchored on the Prism on everyone's screen.
			int uuid = Projectile.GetByUUID(Projectile.owner, Projectile.whoAmI);

			IEntitySource source = Projectile.GetSource_FromThis();
			int damage = Projectile.damage;
			float knockback = Projectile.knockBack;
			if (NoSpawnProjectile(5)) {
				ForceSpawnProjectile(5);
			}
			for (int b = 0; b < NumBeams; ++b) {
				int x = (int) (b + currentRotation * NumBeams / Math.PI / 2) % NumBeams;
				Projectile beam = Projectile.NewProjectileDirect(source, Projectile.Center, beamVelocity, ModContent.ProjectileType<PsychedelicPrismBeam>(), damage, knockback, Projectile.owner, x, uuid);
				BeamIDs[b] = beam.whoAmI;
				PsychedelicPrismBeam newBeam = beam.ModProjectile as PsychedelicPrismBeam;
				newBeam.BeamColorHue = b / NumBeams / 6;
				newBeam.BeamID = b;
			}
			Array.Sort(BeamIDs);

			// Player player = Main.player[Projectile.owner];
			// if (player.HeldItem.type == ModContent.ItemType<PsychedelicPrism>()) {
			// 	PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
			// 	prism.PrismReleased = true;
			// }
			// After creating the beams, mark the Prism as having an important network event. This will make Terraria sync its data to other players ASAP.
			Projectile.netUpdate = true;
		}

		private void EnsureBeams() {
			// currentRotation = projectile.velocity.ToRotation() + MathHelper.PiOver2;
			Vector2 beamVelocity = Vector2.Normalize(Projectile.velocity);
			if (beamVelocity.HasNaNs()) {
				beamVelocity = -Vector2.UnitY;
			}
			int uuid = Projectile.GetByUUID(Projectile.owner, Projectile.whoAmI);
			int damage = Projectile.damage;
			float knockback = Projectile.knockBack;
			for (int b = 0; b < NumBeams; ++b) {
				Projectile beam = Main.projectile[BeamIDs[b]];
				if (beam.type == ModContent.ProjectileType<PsychedelicPrismBeam>()) {
					PsychedelicPrismBeam realBeam = beam.ModProjectile as PsychedelicPrismBeam;
					if (realBeam.Fading) beam.active = false;
				}
				if (beam == null || beam.type != ModContent.ProjectileType<PsychedelicPrismBeam>() || !beam.active) {
					if (NoSpawnProjectile(1)) {
						ForceSpawnProjectile(1);
					}
					IEntitySource source = Projectile.GetSource_FromThis();
					int x = (int) (b + currentRotation * NumBeams / Math.PI / 2) % NumBeams;
					beam = Projectile.NewProjectileDirect(source, Projectile.Center, beamVelocity, ModContent.ProjectileType<PsychedelicPrismBeam>(), damage, knockback, Projectile.owner, x, uuid);
					BeamIDs[b] = beam.whoAmI;
					PsychedelicPrismBeam newBeam = beam.ModProjectile as PsychedelicPrismBeam;
					newBeam.BeamColorHue = b / NumBeams / 6;
					newBeam.BeamID = b;
				}
			}
			Array.Sort(BeamIDs);
			Projectile.netUpdate = true;
		}

		public override bool PreDraw(ref Color lightColor) {
			Player player = Main.player[Projectile.owner];
			PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
			if (prism.State == 2) {
				PrePreDraw();
			}
			return false;
		}

		// Because the Prism is a holdout projectile and stays glued to its user, it needs custom drawcode.
		public void PrePreDraw()
		{
			SpriteEffects effects = Projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			Texture2D texture = (Texture2D) ModContent.Request<Texture2D>(Texture);
			// Texture2D texture = Main.projectileTexture[Projectile.type];
			int frameHeight = texture.Height / Main.projFrames[Projectile.type];
			int spriteSheetOffset = frameHeight * Projectile.frame;
			Vector2 sheetInsertPosition = (Projectile.Center + Vector2.UnitY * Projectile.gfxOffY - Main.screenPosition).Floor();

			// The Prism is always at full brightness, regardless of the surrounding light. This is equivalent to it being its own glowmask.
			// It is drawn in a non-white color to distinguish it from the vanilla Last Prism.
			Color drawColor;
			Player player = Main.player[Projectile.owner];
			if (((player.HeldItem.ModItem as PsychedelicPrism).State & 2) != 0) {
				double res = (Projectile.ai[0] - MaxCharge * 1.2) / MaxCharge / 0.7;
				if (res < 0) res = 0;
				double ratio = 1 - res;
				int R = (int) (175 * ratio + 16);
				int G = (int) (127 * ratio);
				int B = (int) (223 * ratio + 32);
				R += (255 - R) * Blink / 16;
				G += (255 - G) * Blink / 16;
				B += (255 - B) * Blink / 16;
				drawColor = new Color(R, G, B);
			} else {
				drawColor = new Color(255, 255, 255);
			}
			// Main.NewText(drawColor);
			Main.EntitySpriteDraw(texture, sheetInsertPosition, new Rectangle?(new Rectangle(0, spriteSheetOffset, texture.Width, frameHeight)), drawColor, Projectile.rotation, new Vector2(texture.Width / 2f, frameHeight / 2f), Projectile.scale, effects, 0);
		}
	}
}