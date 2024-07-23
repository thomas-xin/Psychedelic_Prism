using Psychedelic_Prism.Projectiles;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using Terraria.ID;
using Terraria.Audio;
using Terraria.ModLoader;

namespace Psychedelic_Prism.Items
{
	/// <summary>
	/// InfoDisplay that is coupled with <seealso cref="ExampleInfoAccessory"/> and <seealso cref="ExampleInfoDisplayPlayer"/> to show
	/// off how to add a new info accessory (such as a Radar, Lifeform Analyzer, etc.)
	/// </summary>
	public class PsychedelicPrism : ModItem
	{
		// You can use a vanilla texture for your item by using the format: "Terraria/Item_<Item ID>".
		// public override string Texture => "Terraria/Item_" + ItemID.LastPrism;
		// public static Color OverrideColor = new Color(191, 127, 255);

		public static int NumPrisms = 5;
		public int[] PrismIDs = new int[5];
		public int[] NPCHealths = new int[2048];
		// public bool PrismReleased = false;
		public int State = 0;

		public override void SetDefaults()
		{
			Item.CloneDefaults(ItemID.LastPrism);
			Item.damage = 1;
			Item.crit = 96;
			Item.mana = 1;
			Item.useTime = 1;
			Item.useAnimation = 1;
			Item.shoot = ModContent.ProjectileType<PsychedelicPrismMain>();
			Item.shootSpeed = 32;
			Item.knockBack = 3.5f;
			Item.value = 1000000;
			Item.rare = ItemRarityID.Purple;
			Item.autoReuse = false;
		}

		public override bool MagicPrefix() {
			return true;
		}

		public override void AddRecipes()
		{
			CreateRecipe()
				.AddIngredient(3541, 5) // Last Prism
				.AddIngredient(3787, 1) // Sky Fracture
				.AddIngredient(2882, 1) // Charged Blaster Cannon
				.AddIngredient(2795, 1) // Laser Machinegun
				.AddIngredient(1260, 1) // Rainbow Gun
				.AddIngredient(495, 1) // Rainbow Rod
				.AddIngredient(4952, 1) // Nightglow
				.AddIngredient(5005, 1) // Terraprisma
				.AddIngredient(5335, 1) // Rod of Harmony
				.AddIngredient(50, 1) // Magic Mirror
				.AddIngredient(5340, 5) // Galaxy Pearl
				.AddIngredient(5339, 100) // Arcane Crystal
				.AddIngredient(3457, 100) // Nebula Fragment
				.AddIngredient(502, 100) // Crystal Shard
				.AddIngredient(181, 100) // Amethyst
				.AddTile(26) // Altar
				.AddTile(125) // Crystal Ball
				.AddTile(356) // Enchanted Sundial
				.AddTile(663) // Enchanted Moondial
				.AddTile(412) // Ancient Manipulator
				.Register();
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

		// Because this weapon fires a holdout projectile, it needs to block usage if its projectile already exists.
		public override bool CanUseItem(Player player) => true;

		public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
		{
			this.FollowShoot(player, 1);
			return false;
		}

		public override bool AltFunctionUse(Player player)//You use this to allow the item to be right clicked
		{
			this.FollowShoot(player, 2);
			return false;
		}

		private void FollowShoot(Player player, int state = 1) {
			State ^= state;
			// Main.NewText(State);
			if (player.ownedProjectileCounts[ModContent.ProjectileType<PsychedelicPrismMain>()] > 0) {
				if (State != 0) {
					if (state == 1) {
						for (int k = 0; k < NumPrisms; k++) {
							Projectile proj = Main.projectile[PrismIDs[k]];
							if (proj == null || !proj.active) {
								continue;
							}
							if (player.whoAmI == proj.owner && proj.type == ModContent.ProjectileType<PsychedelicPrismMain>()) {
								PsychedelicPrismMain currPrism = proj.ModProjectile as PsychedelicPrismMain;
								currPrism.FrameCounter = 1f;
							}
						}
					}
					if (State == 3) {
						SoundEngine.PlaySound(SoundID.Item84, player.position);
					} else {
						SoundEngine.PlaySound(SoundID.Item117, player.position);
					}
					return;
				}
				for (int k = 0; k < NumPrisms; k++) {
					Projectile proj = Main.projectile[PrismIDs[k]];
					if (proj == null || !proj.active) {
						continue;
					}
					if (player.whoAmI == proj.owner && proj.type == ModContent.ProjectileType<PsychedelicPrismMain>()) {
						proj.Kill();
						proj.active = false;
					}
				}
				SoundEngine.PlaySound(SoundID.Item78, player.position);
				player.statMana += 32767;
				return;
			}
			for (int i = 0; i < Main.npc.Length; i++) {
				NPCHealths[i] = -1;
			}
			if (NoSpawnProjectile(5)) {
				ForceSpawnProjectile(5);
			}
			for (int i = 0; i < Main.projectile.Length; i++) {
				Projectile proj = Main.projectile[i];
				if (proj == null || !proj.active) continue;
				if (proj.type == ModContent.ProjectileType<PsychedelicPrismMain>()) {
					proj.Kill();
					proj.active = false;
				} else if (proj.type == ModContent.ProjectileType<PsychedelicPrismBeam>()) {
					PsychedelicPrismBeam beam = proj.ModProjectile as PsychedelicPrismBeam;
					beam.Fading = true;
				}
			}
			var source = new EntitySource_ItemUse_WithAmmo(player, player.HeldItem, 1);
			Vector2 mpos = Main.MouseWorld;
			Vector2 velocity = (mpos - player.MountedCenter);
			bool closeEnough = velocity.Length() < 80f;
			velocity.Normalize();
			velocity *= 16;
			int type = ModContent.ProjectileType<PsychedelicPrismMain>();
			int damage = Item.damage;
			float knockback = Item.knockBack;
			for (int i = 0; i < NumPrisms; i++) {
				Vector2 rotateVec = velocity.RotatedBy(Math.PI * 2 * i / NumPrisms);
				Vector2 perturbedSpeed;
				if (!closeEnough && (State & 1) != 0) {
					perturbedSpeed = velocity.RotatedBy(Math.PI * 2 / 3 * (i - ((float) NumPrisms) / 2) / NumPrisms);
				} else {
					perturbedSpeed = rotateVec;
				}
				PrismIDs[i] = Projectile.NewProjectile(source, player.MountedCenter, rotateVec, type, damage, knockback, player.whoAmI);
				// 433
				Projectile telegraph = Projectile.NewProjectileDirect(source, player.MountedCenter, perturbedSpeed, 433, damage, knockback, player.whoAmI);
				telegraph.friendly = true;
				telegraph.hostile = false;
			}
			Array.Sort(PrismIDs);
			player.statMana = 0;
			return;
		}
	}
}