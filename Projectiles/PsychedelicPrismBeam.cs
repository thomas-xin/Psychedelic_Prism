using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using Terraria;
using Terraria.Enums;
using Terraria.GameContent.Shaders;
using Terraria.Graphics.Effects;
using Terraria.ID;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.Audio;
using Terraria.GameContent.Events;
using Psychedelic_Prism.Items;

namespace Psychedelic_Prism.Projectiles
{
	public class PsychedelicPrismBeam : ModProjectile
	{
		public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.LastPrismLaser;

		// How much more damage the beams do when the Prism is fully charged. Damage smoothly scales up to this multiplier.
		private const float MaxDamageMultiplier = 4f;

		// Beams increase their scale from 0 to this value as the Prism charges up.
		private const float MaxBeamScale = 2f;

		// Beams reduce their spread to zero as the Prism charges up. This controls the maximum spread.
		private const float MaxBeamSpread = 1.3f;

		// The maximum possible range of the beam. Don't set this too high or it will cause significant lag.
		private const float MaxBeamLength = 4096f;

		// The width of the beam in pixels for the purposes of tile collision.
		// This should generally be left at 1, otherwise the beam tends to stop early when touching tiles.
		private const float BeamTileCollisionWidth = 0.1f;

		// The width of the beam in pixels for the purposes of entity hitbox collision.
		// This gets scaled with the beam's scale value, so as the beam visually grows its hitbox gets wider as well.
		private const float BeamHitboxCollisionWidth = 24f;

		// The number of sample points to use when performing a collision hitscan for the beam.
		// More points theoretically leads to a higher quality result, but can cause more lag. 3 tends to be enough.
		private const int NumSamplePoints = 2;
		
		// How quickly the beam adjusts to sudden changes in length.
		// Every frame, the beam replaces this ratio of its current length with its intended length.
		// Generally you shouldn't need to change this.
		// Setting it too low will make the beam lazily pass through walls before being blocked by them.
		private const float BeamLengthChangeFactor = 0.8f;

		// The charge percentage required on the host prism for the beam to begin visual effects (e.g. impact dust).
		private const float VisualEffectThreshold = 0.05f;

		// Each Last Prism beam draws two lasers separately: an inner beam and an outer beam. This controls their opacity.
		private const float OuterBeamOpacityMultiplier = 0.75f;
		private const float InnerBeamOpacityMultiplier = 0.5f;
		private float InnerBeamBrightnessMultiplier = 1f;

		// The maximum brightness of the light emitted by the beams. Brightness scales from 0 to this value as the Prism's charge increases.
		private const float BeamLightBrightness = 32f;

		// These variables control the beam's potential coloration.
		// As a value, hue ranges from 0f to 1f, both of which are pure red. The laser beams vary from 0.57 to 0.75, which winds up being a blue-to-purple gradient.
		// Saturation ranges from 0f to 1f and controls how greyed out the color is. 0 is fully grayscale, 1 is vibrant, intense color.
		// Lightness ranges from 0f to 1f and controls how dark or light the color is. 0 is pitch black. 1 is pure white.
		public float BeamColorHue = 0f;
		private const float BeamHueVariance = 1f;
		private const float BeamColorSaturation = 1f;
		private const float BeamColorLightness = 0.5f;

		private static readonly int[] Buffs = {2, 5, 29, 48, 58, 59, 113, 114, 119, 151, 165, 175, 178, 181, 207, 336};
		private static readonly int[] Debuffs = {20, 24, 31, 36, 39, 44, 67, 68, 69, 70, 144, 153, 169, 183, 189, 195, 196, 203, 204, 323, 324, 337};

		private float Fade = 1f;
		public bool Fading = false;
		private Color LastColor = new(0, 0, 0);
		private float LastScale = 0f;

		private bool Display = false;

		// This property encloses the internal AI variable projectile.ai[0]. It makes the code easier to read.
		public int BeamID {
			get => (int) Projectile.ai[0];
			set => Projectile.ai[0] = value;
		}

		// This property encloses the internal AI variable projectile.ai[1].
		private int HostPrismIndex {
			get => (int) Projectile.ai[1];
			set => Projectile.ai[1] = value;
		}

		// This property encloses the internal AI variable projectile.localAI[1].
		// Normally, localAI is not synced over the network. This beam manually syncs this variable using SendExtraAI and ReceiveExtraAI.
		private float BeamLength {
			get => Projectile.localAI[1];
			set => Projectile.localAI[1] = value;
		}

		public override void SetDefaults() {
			Projectile.width = 18;
			Projectile.height = 18;
			Projectile.DamageType = DamageClass.Magic;
			Projectile.maxPenetrate = 2147483646;
			Projectile.penetrate = -1;
			Projectile.alpha = 255;
			// The beam itself still stops on tiles, but its invisible "source" projectile ignores them.
			// This prevents the beams from vanishing if the player shoves the Prism into a wall.
			Projectile.tileCollide = false;

			// Using local NPC immunity allows each beam to strike independently from one another.
			Projectile.usesLocalNPCImmunity = false;
			Projectile.idStaticNPCHitCooldown = 0;
			Projectile.localNPCHitCooldown = 0;
			Projectile.netImportant = true;
			BeamLength = 16f;
		}

		// public override bool CanBeReflected() {
		// 	return false;
		// }

		// Send beam length over the network to prevent hitbox-affecting and thus cascading desyncs in multiplayer.
		// public override void SendExtraAI(BinaryWriter writer) => writer.Write(BeamLength);
		// public override void ReceiveExtraAI(BinaryReader reader) => BeamLength = reader.ReadSingle();

		public override bool PreAI() {
			if (Fading) {
				if (Fade <= 0f) {
					Projectile.Kill();
					Projectile.active = false;
					return false;
				}
				Projectile.scale = MaxBeamScale * LastScale * Fade;
				// Projectile.Opacity = MathHelper.Lerp(0.05f, 0.6f, LastScale * 1.5f) * (float) Math.Pow(LastScale, 0.5f);
				DelegateMethods.v3_1 = LastColor.ToVector3() * BeamLightBrightness * (16 - InnerBeamBrightnessMultiplier * 15) / 16 * Fade;
				float currLength = BeamLength;
				if (currLength > 1024f) currLength = 1024f;
				Utils.PlotTileLine(Projectile.Center, Projectile.Center + Projectile.velocity * currLength, Projectile.width * Projectile.scale, DelegateMethods.CastLight);
				Fade -= 0.0625f;
				return false;
			}
			Projectile.penetrate = -1;
			Projectile.maxPenetrate = 2147483647;
			Color beamColor = GetOuterBeamColor();
			// If something has gone wrong with either the beam or the host Prism, destroy the beam.
			Projectile hostPrism = Main.projectile[HostPrismIndex];
			if (Projectile.type != ModContent.ProjectileType<PsychedelicPrismBeam>() || !hostPrism.active || hostPrism.type != ModContent.ProjectileType<PsychedelicPrismMain>()) {
				Fading = true;
				return false;
			}
			PsychedelicPrismMain psyPrism = hostPrism.ModProjectile as PsychedelicPrismMain;

			// Grab some variables from the host Prism.
			Vector2 hostPrismDir = Vector2.Normalize(hostPrism.velocity);
			float chargeRatio = MathHelper.Clamp(hostPrism.ai[0] / PsychedelicPrismMain.MaxCharge, 0f, 1f);
			float overCharge = MathHelper.Clamp(hostPrism.ai[0] / PsychedelicPrismMain.MaxCharge - 1.2f, 0f, 0.5f) * 2;
			LastScale = chargeRatio;
			InnerBeamBrightnessMultiplier = 1 - overCharge;

			// Update the beam's damage every frame based on charge and the host Prism's damage.
			Projectile.damage = (int) (hostPrism.damage * GetDamageMultiplier(chargeRatio));

			Projectile.friendly = chargeRatio >= VisualEffectThreshold;

			// This offset is used to make each individual beam orient differently based on its Beam ID.
			float beamIdOffset = BeamID - psyPrism.NumBeams / 2f + 0.5f;
			float beamSpread;
			float spinRate;
			float beamStartSidewaysOffset;
			float beamStartForwardsOffset;

			// Variables scale smoothly while the host Prism is charging up.
			if (chargeRatio < 1f)
			{
				Projectile.scale = MathHelper.Lerp(0f, MaxBeamScale, chargeRatio);
				beamSpread = MathHelper.Lerp(MaxBeamSpread, 0f, chargeRatio);
				beamStartSidewaysOffset = MathHelper.Lerp(20f, 6f, chargeRatio);
				beamStartForwardsOffset = MathHelper.Lerp(-21f, -9f, chargeRatio);

				// For the first 2/3 of charge time, the opacity scales up from 0% to 40%.
				// Spin rate increases slowly during this time.
				if (chargeRatio <= 0.66f)
				{
					float phaseRatio = chargeRatio * 1.5f;
					Projectile.Opacity = MathHelper.Lerp(0.05f, 0.6f, phaseRatio);
					spinRate = MathHelper.Lerp(32f, 20f, phaseRatio);
				}

				// For the last 1/3 of charge time, the opacity scales up from 40% to 100%.
				// Spin rate increases dramatically during this time.
				else
				{
					float phaseRatio = (chargeRatio - 0.66f) * 3f;
					Projectile.Opacity = MathHelper.Lerp(0.6f, 1f, phaseRatio);
					spinRate = MathHelper.Lerp(20f, 8f, phaseRatio);
				}
			}

			// If the host Prism is already at max charge, don't calculate anything. Just use the max values.
			else
			{
				Projectile.scale = MaxBeamScale;
				Projectile.Opacity = 1f;
				beamSpread = 0f;
				spinRate = 8f;
				beamStartSidewaysOffset = 6f;
				beamStartForwardsOffset = -9f;
			}

			// The amount to which the angle changes reduces over time so that the beams look like they are focusing.
			float deviationAngle = (hostPrism.ai[0] / spinRate + beamIdOffset) / psyPrism.NumBeams * MathHelper.TwoPi;

			// This trigonometry calculates where the beam is supposed to be pointing.
			Vector2 unitRot = Vector2.UnitY.RotatedBy(deviationAngle);
			Vector2 yVec = new(4f, beamStartSidewaysOffset);
			float hostPrismAngle = hostPrism.velocity.ToRotation();
			Vector2 beamSpanVector = (unitRot * yVec).RotatedBy(hostPrismAngle);
			float sinusoidYOffset = unitRot.Y * MathHelper.Pi / psyPrism.NumBeams * beamSpread;

			// Calculate the beam's emanating position. Start with the Prism's center.
			Projectile.Center = hostPrism.Center;
			// Add a fixed offset to align with the Prism's sprite sheet.
			Projectile.position += hostPrismDir * 16f + new Vector2(0f, -hostPrism.gfxOffY);
			// Add the forwards offset, measured in pixels.
			Projectile.position += hostPrismDir * beamStartForwardsOffset;
			// Add the sideways offset vector, which is calculated for the current angle of the beam and scales with the beam's sideways offset.
			Projectile.position += beamSpanVector;

			// Set the beam's velocity to point towards its current spread direction and sanity check it. It should have magnitude 1.
			Projectile.velocity = hostPrismDir.RotatedBy(sinusoidYOffset);
			if (Projectile.velocity.HasNaNs() || Projectile.velocity == Vector2.Zero) {
				Projectile.velocity = -Vector2.UnitY;
			}
			Projectile.rotation = Projectile.velocity.ToRotation();

			// Update the beam's length by performing a hitscan collision check.
			float hitscanBeamLength = PerformBeamHitscan(hostPrism, chargeRatio >= 1f);
			BeamLength = MathHelper.Lerp(BeamLength, hitscanBeamLength, BeamLengthChangeFactor);

			// This Vector2 stores the beam's hitbox statistics. X = beam length. Y = beam width.
			Vector2 beamDims = new(Projectile.velocity.Length() * BeamLength, Projectile.width * Projectile.scale);

			// Only produce dust and cause water ripples if the beam is above a certain charge level.
			if (chargeRatio >= VisualEffectThreshold)
			{
				if (BeamLength * 2 <= MaxBeamLength) {
					ProduceBeamDust(beamColor);
				}

				// If the game is rendering (i.e. isn't a dedicated server), make the beam disturb water.
				if (Main.netMode != NetmodeID.Server) {
					ProduceWaterRipples(beamDims);
				}
			}

			DelegateMethods.v3_1 = beamColor.ToVector3() * BeamLightBrightness * chargeRatio * (16 - InnerBeamBrightnessMultiplier * 15) / 16;
			Utils.PlotTileLine(Projectile.Center, Projectile.Center + Projectile.velocity * BeamLength, beamDims.Y, DelegateMethods.CastLight);
			Rectangle projHitbox = new((int) Projectile.Center.X, (int) Projectile.Center.Y, Projectile.width * 5 >> 3, Projectile.height * 5 >> 3);
			Player player = Main.player[Projectile.owner];
			for (int k = 0; k < Main.npc.Length; k++) {
				if (Main.npc[k] == null) continue;
				NPC npc = Main.npc[k];
				if (!npc.active || npc.friendly || ((npc.townNPC || npc.lifeMax <= 3) && npc.damage <= 0)) {
					continue;
				}
				Rectangle targetHitbox = new((int) npc.position.X, (int) npc.position.Y, npc.width, npc.height);
				if ((bool) Colliding2(projHitbox, targetHitbox)) {
					if (InnerBeamBrightnessMultiplier <= 0 || !npc.dontTakeDamage) {
						int dmg = (int) ((Projectile.damage * (1 - InnerBeamBrightnessMultiplier) + 1) / 2);
						if (dmg <= 1) dmg = Main.rand.Next(1, 10);
						int def = npc.defense;
						int origLife = npc.life;
						if (dmg * 49132 <= npc.life) {
							npc.defense = 0;
							if (npc.immortal || npc.dontTakeDamage) {
                                NPC.HitInfo info = new() {
                                    Crit = true,
                                    DamageType = DamageClass.Magic,
                                    Damage = dmg,
                                };
                                npc.StrikeNPC(info);
							}
							else {
								player.ApplyDamageToNPC(npc, dmg, 0f, 0, true);
							}
							npc.defense = def;
							dmg = origLife - npc.life;
						}
						OnHitNPC(npc, dmg, 0f, true);
					}
				}
			}
			if (InnerBeamBrightnessMultiplier < 1) {
				int spawned = 0;
				if (NoSpawnProjectile(512)) spawned = 6;
				for (int k = 0; k < Main.projectile.Length; k++) {
					if (Main.projectile[k] == null || Main.rand.NextBool()) continue;
					Projectile proj = Main.projectile[k];
					if (!proj.active || (proj.friendly && !proj.hostile)) {
						continue;
					}
					if (proj.type == ModContent.ProjectileType<PsychedelicPrismMain>()) {
						continue;
					}
					Rectangle targetHitbox = new((int) proj.position.X, (int) proj.position.Y, proj.width, proj.height);
					if ((bool) Colliding3(projHitbox, targetHitbox)) {
						if (Vector2.Distance(proj.position, player.MountedCenter) > 144) {
							int size = proj.width * proj.height;
							if (size <= 0) size = 1;
							double scale = 32.0 / Math.Pow(size, 0.5);
							proj.damage = (int) (proj.damage * Math.Pow(15.0 / 16, (1 - InnerBeamBrightnessMultiplier) * scale));
							proj.timeLeft = (int) (proj.timeLeft * Math.Pow(15.0 / 16, (1 - InnerBeamBrightnessMultiplier) * scale));
							proj.alpha = (int) (proj.alpha * Math.Pow(15.0 / 16, (1 - InnerBeamBrightnessMultiplier) * scale));
							if (proj.penetrate < 0) proj.penetrate = 1;
							if (size <= 1 || proj.damage <= 0 || proj.timeLeft <= 0) proj.penetrate = 0;
							else if (size > 1) {
								proj.scale *= (float) Math.Pow(31.0 / 32, (1 - InnerBeamBrightnessMultiplier));
							}
						}
						else {
							proj.owner = player.whoAmI;
							proj.friendly = true;
							proj.hostile = false;
							proj.velocity = Vector2.Negate(proj.velocity);
						}
						if (proj.penetrate <= 0) {
							if (spawned < 6) {
								IEntitySource source = Projectile.GetSource_FromThis();
								Vector2 targetpos = (proj.position - Projectile.position).Length() * Projectile.velocity + Projectile.position;
								// Vector2 attenuated = new Vector2(player.position.X, player.position.Y);
								SoundEngine.PlaySound(SoundID.Item33, targetpos);
								for (int i = 0; i < (6 - spawned + 1) / 2; i++) {
									double angle = Math.PI * Main.rand.Next(0, 360) / 180;
									Vector2 polar = new Vector2((float) Math.Cos(angle), (float) Math.Sin(angle)) * 12f;
									proj.SetDefaults(proj.type);
									int[] choices = {9, 16, 79, 92, 297, 462, 464, 538, 617, 634, 635, 709, 725, 728, 917, 931, 950, 955};
									int pid = choices[Main.rand.Next(0, choices.Length)];
									int dmg2 = proj.damage * proj.width * proj.height / 16 + 1;
									if (pid == 79) polar *= 3f;
									else if (pid == 538) polar /= 3f;
									if (pid == 12 || pid == 538) dmg2 *= 3;
									pid = Projectile.NewProjectile(source, targetpos + polar * 3f, polar, pid, dmg2, Projectile.knockBack * -2, player.whoAmI);
									Projectile newproj = Main.projectile[pid];
									newproj.active = true;
									newproj.friendly = true;
									newproj.hostile = false;
									newproj.velocity = polar;
									newproj.penetrate = Main.rand.Next(4, 11);
									if (pid == 538) newproj.timeLeft = 240;
									proj.Kill();
									proj.penetrate = 0;
									spawned++;
								}
							}
						}
						else {
							float knock = 131072f / (65536f + Vector2.DistanceSquared(Projectile.position, proj.position)) - 0.15f;
							Vector2 mpos = Main.MouseWorld;
							Vector2 vel;
							if (Vector2.Distance(mpos, player.MountedCenter) < 80) {
								vel = Vector2.Normalize(proj.position - player.MountedCenter);
							}
							else {
								vel = Projectile.velocity;
							}
							vel *= (1 - InnerBeamBrightnessMultiplier) * (Main.rand.NextFloat() + 1.5f) * knock * 2f;
							proj.velocity *= 0.92f;
							proj.velocity += vel;
							proj.position += vel;
						}
					}
				}
			}
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

		// Uses a simple polynomial (x^2) to get sudden but smooth damage increase near the end of the charge-up period.
		private float GetDamageMultiplier(float chargeRatio) {
			float f = chargeRatio * chargeRatio;
			return MathHelper.Lerp(1f, MaxDamageMultiplier, f);
		}

		private float PerformBeamHitscan(Projectile prism, bool fullCharge) {
			if (InnerBeamBrightnessMultiplier <= 0) return MaxBeamLength;
			Projectile hostPrism = Main.projectile[HostPrismIndex];
			Player player = Main.player[Projectile.owner];
			// Hitscan interpolation starts from the second intersection of the secant formed by the beam through
			// the expanding circle path that the prisms follow. This allows a beam to pass through walls before
			// being fully charged, as long as it doesn't hit the wall until first reaching the edge of the circle.
			Vector2 samplingPoint = Projectile.Center;
			float chord = 0;

			Vector2 diff = player.MountedCenter - hostPrism.position;
			float radius = diff.Length();
			float cosT = (diff.X * Projectile.velocity.X + diff.Y * Projectile.velocity.Y) / radius / Projectile.velocity.Length();
			double theta = Math.Acos(cosT);
			if (theta < 0) theta = -theta;
			double central = Math.PI - theta * 2;
			if (central > 0 && theta > 0) {
				chord = (float) (radius * Math.Sin(central) / Math.Sin(theta));
				if (chord > 0) {
					// Main.NewText(chord.ToString(), new Color(0, 255, 0));
					samplingPoint += Projectile.velocity * chord;
				}
			}

			float chargeRatio = MathHelper.Clamp(hostPrism.ai[0] / PsychedelicPrismMain.MaxCharge, 0f, 1f);
			float shortDist = ((float) Math.Pow(chargeRatio * 2, 2f)) * MaxBeamLength / 2 + 16f;
			if (shortDist > MaxBeamLength) shortDist = MaxBeamLength;
			float[] laserScanResults = new float[NumSamplePoints];
			Collision.LaserScan(samplingPoint, Projectile.velocity, BeamTileCollisionWidth * Projectile.scale, shortDist, laserScanResults);
			float averageLengthSample = 0f;
			for (int i = 0; i < laserScanResults.Length; ++i) {
				averageLengthSample += laserScanResults[i];
			}
			averageLengthSample /= NumSamplePoints;
			averageLengthSample += chord;
			if (averageLengthSample < MaxBeamLength) {
				averageLengthSample += 1;
			}
			if (InnerBeamBrightnessMultiplier < 1) {
				float ratio = (float) Math.Pow(1f - InnerBeamBrightnessMultiplier, 5f);
				return averageLengthSample * (1 - ratio) + MaxBeamLength * ratio;
			}
			return averageLengthSample;
		}
		
		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
			return false;
		}

		// Determines whether the specified target hitbox is intersecting with the beam.
		private bool? Colliding2(Rectangle projHitbox, Rectangle targetHitbox) {
			// If the target is touching the beam's hitbox (which is a small rectangle vaguely overlapping the host Prism), that's good enough.
			if (projHitbox.Intersects(targetHitbox)) {
				return true;
			}

			// Otherwise, perform an AABB line collision check to check the whole beam.
			float _ = float.NaN;
			Vector2 beamEndPos = Projectile.Center + Projectile.velocity * BeamLength;
			return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center, beamEndPos, BeamHitboxCollisionWidth * Projectile.scale, ref _);
		}

		private bool? Colliding3(Rectangle projHitbox, Rectangle targetHitbox) {
			// If the target is touching the beam's hitbox (which is a small rectangle vaguely overlapping the host Prism), that's good enough.
			if (projHitbox.Intersects(targetHitbox)) {
				return true;
			}

			// Otherwise, perform an AABB line collision check to check the whole beam.
			float _ = float.NaN;
			Vector2 beamEndPos = Projectile.Center + Projectile.velocity * BeamLength;
			return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size() + new Vector2(16f, 16f), Projectile.Center, beamEndPos, BeamHitboxCollisionWidth * Projectile.scale * 1.6f, ref _);
		}

		public override bool PreDraw(ref Color lightColor) {
			// If the beam doesn't have a defined direction, don't draw anything.
			if (Projectile.velocity == Vector2.Zero) {
				return false;
			}

			Texture2D texture = (Texture2D) ModContent.Request<Texture2D>(Texture);
			// Texture2D texture = Main.projectileTexture[Projectile.type];
			Vector2 centerFloored = Projectile.Center.Floor() + Projectile.velocity * Projectile.scale * 10.5f;
			Vector2 drawScale = new(Projectile.scale);

			// Reduce the beam length proportional to its square area to reduce block penetration.
			float visualBeamLength = BeamLength - 9.5f * Projectile.scale * Projectile.scale;

			DelegateMethods.f_1 = 1f; // f_1 is an unnamed decompiled variable whose function is unknown. Leave it at 1.
			Vector2 startPosition = centerFloored - Main.screenPosition;
			Vector2 endPosition = startPosition + Projectile.velocity * visualBeamLength;

			Player player = null;
			if (!Fading) {
				player = Main.player[Projectile.owner];
				if (player.HeldItem.type == ModContent.ItemType<PsychedelicPrism>()) {
					PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
					PsychedelicPrismMain hostPrism = Main.projectile[HostPrismIndex].ModProjectile as PsychedelicPrismMain;
					if (hostPrism.identity == prism.PrismIDs[0]) {
						if (Projectile.whoAmI == hostPrism.BeamIDs[0]) {
							for (int i = 0; i < PsychedelicPrism.NumPrisms; i++) {
								if (Main.projectile[prism.PrismIDs[i]].ModProjectile != null) {
									PsychedelicPrismMain currPrism = Main.projectile[prism.PrismIDs[i]].ModProjectile as PsychedelicPrismMain;
									currPrism.PrePreDraw();
								}
							}
						}
					}
				}
			}

			// Draw the outer beam.
			DrawBeam(texture, startPosition, endPosition, drawScale, LastColor * OuterBeamOpacityMultiplier * Projectile.Opacity);

			if (Fading) {
				PostPreDraw(texture);
				return false;
			}

			// Only draw the inner beams after every outer beam being fired by the player is drawn
			if (player.HeldItem.type == ModContent.ItemType<PsychedelicPrism>()) {
				PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
				for (int i = 0; i < PsychedelicPrism.NumPrisms; i++) {
					Projectile beamWrapper = Main.projectile[prism.PrismIDs[i]];
					if (beamWrapper != null && beamWrapper.active && beamWrapper.type == ModContent.ProjectileType<PsychedelicPrismMain>()) {
						PsychedelicPrismMain currPrism = Main.projectile[prism.PrismIDs[i]].ModProjectile as PsychedelicPrismMain;
						for (int j = 0; j < currPrism.NumBeams; j++) {
							if (currPrism.BeamIDs[j] > Projectile.whoAmI) return false;
						}
					}
				}
				for (int i = 0; i < PsychedelicPrism.NumPrisms; i++) {
					Projectile beamWrapper = Main.projectile[prism.PrismIDs[i]];
					if (beamWrapper != null && beamWrapper.active && beamWrapper.ModProjectile != null) {
						PsychedelicPrismMain currPrism = Main.projectile[prism.PrismIDs[i]].ModProjectile as PsychedelicPrismMain;
						for (int j = 0; j < currPrism.NumBeams; j++) {
							Projectile beam = Main.projectile[currPrism.BeamIDs[j]];
							if (beam != null && beam.active && beam.type == ModContent.ProjectileType<PsychedelicPrismBeam>()) {
								PsychedelicPrismBeam realBeam = beam.ModProjectile as PsychedelicPrismBeam;
								realBeam.PostPreDraw(texture);
							}
						}
					}
				}
			}

			// Returning false prevents Terraria from trying to draw the projectile itself.
			return false;
		}

		public void PostPreDraw(Texture2D texture) {
			// If the beam doesn't have a defined direction, don't draw anything.
			if (Projectile.velocity == Vector2.Zero) {
				return;
			}

			Vector2 centerFloored = Projectile.Center.Floor() + Projectile.velocity * Projectile.scale * 10.5f;
			Vector2 drawScale = new(Projectile.scale);

			// Reduce the beam length proportional to its square area to reduce block penetration.
			float visualBeamLength = BeamLength - 9.5f * Projectile.scale * Projectile.scale;

			DelegateMethods.f_1 = 1f; // f_1 is an unnamed decompiled variable whose function is unknown. Leave it at 1.
			Vector2 startPosition = centerFloored - Main.screenPosition;
			Vector2 endPosition = startPosition + Projectile.velocity * visualBeamLength;

			// Draw the inner beam, which is half size.
			drawScale *= 0.125f * (InnerBeamBrightnessMultiplier + 3);
			DrawBeam(texture, startPosition, endPosition, drawScale, GetInnerBeamColor() * InnerBeamOpacityMultiplier * Projectile.Opacity);
		}

		private void DrawBeam(Texture2D texture, Vector2 startPosition, Vector2 endPosition, Vector2 drawScale, Color beamColor) {
			Utils.LaserLineFraming lineFraming = new(DelegateMethods.RainbowLaserDraw);

			// c_1 is an unnamed decompiled variable which is the render color of the beam drawn by DelegateMethods.RainbowLaserDraw.
			DelegateMethods.c_1 = beamColor;
			Utils.DrawLaser(Main.spriteBatch, texture, startPosition, endPosition, drawScale, lineFraming);
		}

		private Color GetOuterBeamColor() {
			Projectile hostPrism = Main.projectile[HostPrismIndex];
			if (hostPrism == null || Fading) return LastColor;
			PsychedelicPrismMain psyPrism = hostPrism.ModProjectile as PsychedelicPrismMain;
			// This hue calculation produces a unique color for each beam based on its Beam ID.
			float hue = ((((float) BeamID) / psyPrism.NumBeams) % BeamHueVariance + BeamColorHue) % 1;

			float chargeRatio2 = MathHelper.Clamp(hostPrism.ai[0] / PsychedelicPrismMain.MaxCharge, 0f, 1.3f);
			float ratio = (float) Math.Pow(chargeRatio2 / 1.3f, 4f) * 0.9f;
			hue = hue * (1f - ratio) + 0.75f * ratio;

			// Main.hslToRgb converts Hue, Saturation, Lightness into a Color for general purpose use.
			Color c = Main.hslToRgb(hue, BeamColorSaturation, BeamColorLightness);

			// Manually reduce the opacity of the color so beams can overlap without completely overwriting each other.
			c.A = 48;
			LastColor = c;
			return c;
		}

		// Inner beams are always pure white so that they act as a "blindingly bright" center to each laser.
		private Color GetInnerBeamColor() => new(InnerBeamBrightnessMultiplier * 1.5f, InnerBeamBrightnessMultiplier, InnerBeamBrightnessMultiplier * 2);

		private void ProduceBeamDust(Color beamColor) {
			// Create one dust per frame a small distance from where the beam ends.
			const int type = 15;
			Vector2 endPosition = Projectile.Center + Projectile.velocity * (BeamLength - 14.5f * Projectile.scale);

			// Main.rand.NextBool is used to give a 50/50 chance for the angle to point to the left or right.
			// This gives the dust a 50/50 chance to fly off on either side of the beam.
			float angle = Projectile.rotation + (Main.rand.NextBool() ? 1f : -1f) * MathHelper.PiOver2;
			float startDistance = Main.rand.NextFloat(1f, 1.8f);
			float scale = Main.rand.NextFloat(0.7f, 1.1f);
			Vector2 velocity = angle.ToRotationVector2() * startDistance;
			Dust dust = Dust.NewDustDirect(endPosition, 0, 0, type, velocity.X, velocity.Y, 0, beamColor, scale);
			dust.color = beamColor;
			dust.noGravity = true;

			// If the beam is currently large, make the dust faster and larger to match.
			if (Projectile.scale > 0.5f) {
				dust.velocity *= Projectile.scale * 2;
				dust.scale *= Projectile.scale * 2;
			}
		}

		private void ProduceWaterRipples(Vector2 beamDims) {
			WaterShaderData shaderData = (WaterShaderData)Filters.Scene["WaterDistortion"].GetShader();

			// A universal time-based sinusoid which updates extremely rapidly. GlobalTime is 0 to 3600, measured in seconds.
			float waveSine = 0.1f * (float)Math.Sin(Projectile.ai[0] / 3);
			Vector2 ripplePos = Projectile.position + new Vector2(beamDims.X * 0.5f, 0f).RotatedBy(Projectile.rotation);

			// WaveData is encoded as a Color. Not really sure why.
			Color waveData = new Color(0.5f, 0.1f * Math.Sign(waveSine) + 0.5f, 0f, 1f) * Math.Abs(waveSine);
			shaderData.QueueRipple(ripplePos, waveData, beamDims, RippleShape.Square, Projectile.rotation);
		}

		// Automatically iterates through every tile the laser is overlapping to cut grass at all those locations.
		public override void CutTiles() {
			Projectile hostPrism = Main.projectile[HostPrismIndex];
			if (hostPrism == null || Fading) return;
			float chargeRatio = MathHelper.Clamp(hostPrism.ai[0] / PsychedelicPrismMain.MaxCharge, 0f, 1f);
			if (chargeRatio >= 1) {
				PsychedelicPrismMain psyPrism = hostPrism.ModProjectile as PsychedelicPrismMain;
				if (Projectile.whoAmI != psyPrism.BeamIDs[psyPrism.NumBeams - 1]) {
					return;
				}
			}
			// tilecut_0 is an unnamed decompiled variable which tells CutTiles how the tiles are being cut (in this case, via a projectile).
			DelegateMethods.tilecut_0 = TileCuttingContext.AttackProjectile;
			Vector2 beamStartPos = Projectile.Center;
			Vector2 beamEndPos = beamStartPos + Projectile.velocity * BeamLength;

			// PlotTileLine is a function which performs the specified action to all tiles along a drawn line, with a specified width.
			// In this case, it is cutting all tiles which can be destroyed by projectiles, for example grass or pots.
			Utils.PlotTileLine(beamStartPos, beamEndPos, Projectile.width * Projectile.scale, DelegateMethods.CutTiles);
		}

		public void OnHitNPC(NPC target, int damage, float knockback, bool crit) {
			Player player = Main.player[Projectile.owner];
			double dealt = damage;
			double r1 = 524287.0 / 524288;
			double r2 = 1;
			Projectile hostPrism = Main.projectile[HostPrismIndex];
			float chargeRatio = MathHelper.Clamp(hostPrism.ai[0] / PsychedelicPrismMain.MaxCharge, 0f, 1f);
			double dmg = 0;
			float DM = GetDamageMultiplier(chargeRatio);
			if (!target.boss) DM *= 4f;
			DM *= (2f - 1f / target.takenDamageMultiplier);
			// Main.NewText(target.realLife.ToString(), new Color(255, 0, 0));
			// Main.NewText(target.releaseOwner.ToString(), new Color(0, 255, 0));
			if (target.realLife != -1) {
				if (target.releaseOwner == 255) {
					short owned = 0;
					for (int i = 0; i < Main.npc.Length; i++) {
						NPC npc = Main.npc[i];
						if (npc != null && npc.active && npc.realLife == target.realLife) {
							owned++;
						}
					}
					if (owned == 255) owned++;
					for (int i = 0; i < Main.npc.Length; i++) {
						NPC npc = Main.npc[i];
						if (npc != null && npc.active && npc.realLife == target.realLife) {
							npc.releaseOwner = owned;
						}
					}
					// Main.NewText(target.releaseOwner.ToString(), new Color(255, 0, 255));
				}
				DM *= (float) Math.Pow(target.releaseOwner, -0.8);
			}
			if (InnerBeamBrightnessMultiplier < 1) {
				target.lavaImmune = false;
				int dealt2 = 0;
				if (player.HeldItem.type == ModContent.ItemType<PsychedelicPrism>()) {
					PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
					int nextLife = prism.NPCHealths[target.whoAmI];
					if (nextLife < target.life) {
						dealt2 += target.life - nextLife;
						target.life = nextLife;
					}
				}
				r1 = 262143.0 / 262144;
				r2 = 2097151.0 / 2097152;
				double lifeR = (target.life * Math.Pow(r1, (1 - InnerBeamBrightnessMultiplier) * DM));
				if (lifeR % 1 > Main.rand.NextFloat()) lifeR += 1;
				int life = (int) lifeR;
				if (life <= 0) life = 1;
				dealt2 += target.life - life;
				if (dealt2 > 0) {
					target.HitEffect(0, dealt2);
					dealt += dealt2;
				}
				target.life = life;
				if (target.realLife > target.life) target.realLife = target.life;
				if (target.life <= 2) {
					target.life = 0;
					dmg = target.lifeMax;
				}
			}
			if (dmg <= 0) {
				dmg = (target.life * (1 - Math.Pow(r1, DM)));
				dmg += (target.lifeMax * (1 - Math.Pow(r2, DM)));
			}
			dmg *= player.GetDamage(DamageClass.Magic).Multiplicative;
			if (player.luck > 0) {
				dmg *= (1.0 + 2.0 * player.luck * Main.rand.NextFloat());
			}
			dmg *= 1.0f + player.GetCritChance(DamageClass.Magic);
			// dmg *= (1.0 + 2.0 * mcrit / 100 * Main.rand.NextFloat());
			if (dmg % 1 > Main.rand.NextFloat()) {
				dmg += 1;
			}
			dmg = (int) dmg;
			if (dmg < 1) return;
			if (dmg > 2147483647) dmg = 2147483647;
			int def = target.defense;
			target.defense = 0;
			if (InnerBeamBrightnessMultiplier < 1) {
				target.knockBackResist = InnerBeamBrightnessMultiplier / 2 + 0.5f;
			}
			int dir = 1;
			if (target.position.X < player.position.X) dir = -1;
			float kbr = target.knockBackResist;
			Rectangle rect = target.getRect();
			float dist = Vector2.Distance(Projectile.position, target.position) - (rect.Width + rect.Height) / 4f;
			float dist2 = dist * dist + 32768f;
			if (dist2 < 0) dist2 = 0;
			float knock = 131072f / (dist2 + 32768f) - 0.25f;
			if (target.immortal || target.dontTakeDamage || target.noTileCollide || dmg == 2147483647) {
                NPC.HitInfo info = new() {
                    Crit = true,
                    DamageType = DamageClass.Magic,
                    Damage = (int) dmg,
                    Knockback = 0.4f * knock,
                    HitDirection = dir,
                };
                dealt += target.StrikeNPC(info);
			}
			else {
				player.ApplyDamageToNPC(target, (int) dmg, 0.5f * knock, dir, Main.rand.NextBool());
			}
			if (InnerBeamBrightnessMultiplier <= 0) {
				if (dmg < 2147483647) dmg /= 2;
                NPC.HitInfo info = new() {
                    Crit = true,
                    DamageType = DamageClass.Magic,
                    Damage = (int) dmg,
                };
                int d2 = target.StrikeNPC(info);
					dealt += d2;
					if (d2 <= 2) {
						d2 = (int) (dmg - d2);
						target.life -= d2;
						target.HitEffect(0, d2);
						info.Damage = 1;
						target.StrikeNPC(info);
					}
			}
			else {
				target.defense = def;
				target.knockBackResist = kbr;
			}
			if (InnerBeamBrightnessMultiplier < 1) {
				target.dontTakeDamage = false;
				Vector2 mpos = Main.MouseWorld;
				Vector2 vel;
				if (Vector2.Distance(mpos, player.MountedCenter) < 80) {
					vel = Vector2.Normalize(target.position - player.MountedCenter);
				}
				else {
					vel = Projectile.velocity;
				}
				vel *= (1 - InnerBeamBrightnessMultiplier) * (Main.rand.NextFloat() + 1.5f) * knock * 2f;
				target.velocity += vel;
				if (target.noTileCollide) {
					target.position += vel;
				}
			}
			target.value += 12.5f;
			if (target.life <= 0 && !target.celled) {
				target.celled = true;
				target.value *= 4f;
				if (target.type == 548 || target.type == 549) {
					if (DD2Event.Ongoing) {
						NPC.waveNumber = 7;
						NPC.waveKills = 1073741823f;
						DD2Event.TimeLeftBetweenWaves = 1;
						DD2Event.ReportEventProgress();
						DD2Event.StartVictoryScene();
					}
				}
				if (!NoSpawnProjectile(512)) {
					IEntitySource source = Projectile.GetSource_FromThis();
					Vector2 targetpos = (target.position - Projectile.position).Length() * Projectile.velocity + Projectile.position;
					// Vector2 attenuated = new Vector2(player.position.X, player.position.Y);
					SoundEngine.PlaySound(SoundID.Item33, targetpos);
					for (int i = 0; i < 8; i++) {
						double angle = Math.PI * Main.rand.Next(0, 360) / 180;
						Vector2 polar = new Vector2((float) Math.Cos(angle), (float) Math.Sin(angle)) * 12f;
						int[] choices = {9, 16, 79, 92, 297, 462, 464, 538, 617, 634, 635, 709, 725, 728, 917, 931, 950, 955};
						int pid = choices[Main.rand.Next(0, choices.Length)];
						int dmg2 = target.lifeMax / 16 + target.defense / 2 + 1;
						if (pid == 79) polar *= 3f;
						else if (pid == 538) polar /= 3f;
						if (pid == 12 || pid == 538) dmg2 *= 3;
						pid = Projectile.NewProjectile(source, targetpos + polar * 3f, polar, pid, dmg2, Projectile.knockBack * -2, player.whoAmI);
						Projectile newproj = Main.projectile[pid];
						newproj.friendly = true;
						newproj.hostile = false;
						newproj.velocity = polar;
						newproj.penetrate = Main.rand.Next(3, 9);
						if (pid == 538) newproj.timeLeft = 240;
					}
				}
				if (target.active) {
					target.immortal = false;
					target.life = 0;
					dealt = target.lifeMax;
                    // target.lifeMax = 1;
                    // target.aiStyle = 2;
                    NPC.HitInfo info = new() {
                        Crit = true,
                        DamageType = DamageClass.Magic,
                        Damage = (int) dealt,
                    };
                    target.StrikeNPC(info);
					target.NPCLoot();
					target.damage = 0;
					if (player.HeldItem.type == ModContent.ItemType<PsychedelicPrism>()) {
						PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
						if (prism.NPCHealths[target.whoAmI] < 0) {
							target.active = false;
						}
					}
				}
				for (int i = 0; i < Buffs.Length; i++) {
					player.AddBuff(Buffs[i], Main.rand.Next(60, 1800), false);
				}
			}
			else {
				for (int i = 0; i < Debuffs.Length; i++) {
					target.AddBuff(Debuffs[i], 3600, false);
				}
			}
			if (player.HeldItem.type == ModContent.ItemType<PsychedelicPrism>()) {
				PsychedelicPrism prism = player.HeldItem.ModItem as PsychedelicPrism;
				prism.NPCHealths[target.whoAmI] = target.life - 1;
			}
			if (dealt < 0) dealt = 2147483647;
			if (Main.netMode != 0) {
				NetMessage.SendData(28, -1, -1, null, target.whoAmI, (float) dealt, 1f, 0f, 0, 0, 0);
			}
			player.addDPS((int) dealt);
			if (player.dpsDamage < 0) player.dpsDamage = 2147483647;
			target.netUpdate = true;
		}

		public override void OnHitPlayer(Player target, Player.HurtInfo info) {
			int damage = info.Damage;
			// bool crit = false;
			Player player = Main.player[Projectile.owner];
			if (damage <= 0) {
				target.KillMe(null, 1, 1, true);
				return;
			}
			if (InnerBeamBrightnessMultiplier < 1) {
				Projectile hostPrism = Main.projectile[HostPrismIndex];
				float chargeRatio = MathHelper.Clamp(hostPrism.ai[0] / PsychedelicPrismMain.MaxCharge, 0f, 1f);
				int life = (int) (target.statLife * Math.Pow(2730.0 / 2731, (1 - InnerBeamBrightnessMultiplier) * GetDamageMultiplier(chargeRatio)));
				target.statLife = life;
				int dmg = (int) (target.statLife * (1 - Math.Pow(511.0 / 512, GetDamageMultiplier(chargeRatio))));
				if (dmg <= 0) dmg = 1;
				target.Hurt(null, dmg, 0, true);
			}
			if (target.statLife <= 0) {
				IEntitySource source = Projectile.GetSource_FromThis();
				Vector2 targetpos = (target.position - Projectile.position).Length() * Projectile.velocity + Projectile.position;
				// Vector2 attenuated = new Vector2(player.position.X, player.position.Y);
				SoundEngine.PlaySound(SoundID.Item33, targetpos);
				for (int i = 0; i < 8; i++) {
					double angle = Math.PI * Main.rand.Next(0, 360) / 180;
					Vector2 polar = new Vector2((float) Math.Cos(angle), (float) Math.Sin(angle)) * 12f;
					int[] choices = {9, 16, 79, 92, 297, 462, 464, 538, 617, 634, 635, 709, 725, 728, 917, 931, 950, 955};
					int pid = choices[Main.rand.Next(0, choices.Length)];
					int dmg2 = target.statLifeMax * 2 + target.statDefense / 2 + 1;
					if (pid == 79) polar *= 3f;
					else if (pid == 538) polar /= 3f;
					if (pid == 12 || pid == 538) dmg2 *= 3;
					pid = Projectile.NewProjectile(source, targetpos + polar * 3f, polar, pid, dmg2, Projectile.knockBack * -2, player.whoAmI);
					Projectile newproj = Main.projectile[pid];
					newproj.active = true;
					newproj.friendly = true;
					newproj.hostile = false;
					newproj.velocity = polar;
					newproj.penetrate = Main.rand.Next(3, 9);
					if (pid == 538) newproj.timeLeft = 240;
				}
			}
			else {
				for (int i = 0; i < Debuffs.Length; i++) {
					target.AddBuff(Debuffs[i], 3600, false);
				}
			}
		}
	}
}