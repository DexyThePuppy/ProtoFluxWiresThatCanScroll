using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine.ProtoFlux;
using Elements.Core;
using System;
using System.Collections.Generic;
using Elements.Assets;
using System.Linq;

namespace ProtoWireScroll;
//More info on creating mods can be found https://github.com/resonite-modding-group/ResoniteModLoader/wiki/Creating-Mods
public class ProtoWireScroll : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.1.0";
	public override string Name => "ProtoWireScroll";
	public override string Author => "Dexy, NepuShiro";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/DexyThePuppy/ProtoFluxWiresThatCanScroll";

	// Configuration
	public static ModConfiguration Config;
	public static readonly Dictionary<Slot, Panner2D> pannerCache = new Dictionary<Slot, Panner2D>();
	
	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> ENABLED = new("Enabled", "Should ProtoWireScroll be Enabled?", () => true);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<float2> SCROLL_SPEED = new("scrollSpeed", "Scroll Speed (X,Y)", () => new float2(-0.5f, 0f));
	
	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<float2> SCROLL_REPEAT = new("scrollRepeat", "Scroll Repeat Interval (X,Y)", () => new float2(1f, 1f));
	
	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> PING_PONG = new("pingPong", "Ping Pong Animation", () => false);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<Uri> FAR_TEXTURE = new("farTexture", "Far Texture URL", () => new Uri("resdb:///de2b9dfc4d029bd32ec784078b7511f0a8f18d2690595fc2540729da63a37f0a.webp"));

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<Uri> NEAR_TEXTURE = new("nearTexture", "Near Texture URL", () => new Uri("resdb:///de2b9dfc4d029bd32ec784078b7511f0a8f18d2690595fc2540729da63a37f0a.webp"));	

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<TextureFilterMode> FILTER_MODE = new("filterMode", "Texture Filter Mode", () => TextureFilterMode.Anisotropic);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> MIPMAPS = new("mipMaps", "Generate MipMaps", () => false);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> UNCOMPRESSED = new("uncompressed", "Uncompressed Texture", () => false);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> DIRECT_LOAD = new("directLoad", "Direct Load", () => false);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> FORCE_EXACT_VARIANT = new("forceExactVariant", "Force Exact Variant", () => false);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> CRUNCH_COMPRESSED = new("crunchCompressed", "Use Crunch Compression", () => true);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<TextureWrapMode> WRAP_MODE_U = new("wrapModeU", "Texture Wrap Mode U", () => TextureWrapMode.Repeat);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<TextureWrapMode> WRAP_MODE_V = new("wrapModeV", "Texture Wrap Mode V", () => TextureWrapMode.Repeat);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> KEEP_ORIGINAL_MIPMAPS = new("keepOriginalMipMaps", "Keep Original MipMaps", () => false);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<Filtering> MIPMAP_FILTER = new("mipMapFilter", "MipMap Filter", () => Filtering.Box);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<bool> READABLE = new("readable", "Readable Texture", () => true);

	[AutoRegisterConfigKey]
	public static readonly ModConfigurationKey<int> ANISOTROPIC_LEVEL = new("anisotropicLevel", "Anisotropic Level", () => 8);


	public override void OnEngineInit() {
		Config = GetConfiguration();
		Config.Save(true); // Save default config values

		Harmony harmony = new Harmony("com.Dexy.ProtoWireScroll");
		harmony.PatchAll();
		Msg("🐾 ProtoWireScroll successfully loaded and patched! Woof!");
		
		Config.OnThisConfigurationChanged += (k) => {
			if (k.Key != ENABLED) {
				foreach (var kvp in pannerCache) {
					var panner = kvp.Value;
					if (panner == null) continue;

					panner.Speed = Config.GetValue(SCROLL_SPEED);
					panner.Repeat = Config.GetValue(SCROLL_REPEAT);
					panner.PingPong.Value = Config.GetValue(PING_PONG);

					// Get the FresnelMaterial
					var fresnelMaterial = kvp.Key.GetComponent<FresnelMaterial>();
					if (fresnelMaterial != null) {
						var farTexture = GetOrCreateSharedTexture(fresnelMaterial.Slot, Config.GetValue(FAR_TEXTURE));
						fresnelMaterial.FarTexture.Target = farTexture;
						
						var nearTexture = GetOrCreateSharedTexture(fresnelMaterial.Slot, Config.GetValue(NEAR_TEXTURE));
						fresnelMaterial.NearTexture.Target = nearTexture;
					}
				}
			} 
		};

	}


	[HarmonyPatch(typeof(ProtoFluxWireManager), "OnChanges")]
	class ProtoFluxWireManager_OnChanges_Patch {
		public static void Postfix(ProtoFluxWireManager __instance, SyncRef<MeshRenderer> ____renderer, SyncRef<StripeWireMesh> ____wireMesh) {
			try {
				// Skip if mod is disabled or required components are missing
				if (!Config.GetValue(ENABLED) || 
					__instance == null || 
					!__instance.Enabled || 
					____renderer?.Target == null || 
					____wireMesh?.Target == null ||
					__instance.Slot == null) return;
				
				// === User Permission Check ===
				// Get the AllocUser for wire point
				__instance.Slot.ReferenceID.ExtractIDs(out ulong position, out byte user);
				User wirePointAllocUser = __instance.World.GetUserByAllocationID(user);
				
				// Only process if wire belongs to local user
				if (wirePointAllocUser == null || position < wirePointAllocUser.AllocationIDStart) {
					__instance.ReferenceID.ExtractIDs(out ulong position1, out byte user1);
					User instanceAllocUser = __instance.World.GetUserByAllocationID(user1);
					
					if (instanceAllocUser == null || position1 < instanceAllocUser.AllocationIDStart || instanceAllocUser != __instance.LocalUser) return;
				}
				else if (wirePointAllocUser != __instance.LocalUser) return;
				
				// === Material Setup ===
				// Get or create the shared Fresnel Material
				var fresnelMaterial = GetOrCreateSharedMaterial(__instance.Slot);
				if (fresnelMaterial != null) {
					____renderer.Target.Material.Target = fresnelMaterial;
				}

				// === Animation Setup ===
				// Get or create Panner2D for scrolling effect
				if (!pannerCache.TryGetValue(fresnelMaterial.Slot, out var panner)) {
					panner = fresnelMaterial.Slot.GetComponentOrAttach<Panner2D>();
				
					// Configure panner with user settings
					panner.Speed = Config.GetValue(SCROLL_SPEED);
					panner.Repeat = Config.GetValue(SCROLL_REPEAT);
					panner.PingPong.Value = Config.GetValue(PING_PONG);
					
					pannerCache[fresnelMaterial.Slot] = panner;

					// Set the textures from config
					var farTexture = GetOrCreateSharedTexture(fresnelMaterial.Slot, Config.GetValue(FAR_TEXTURE));
					fresnelMaterial.FarTexture.Target = farTexture;
					
					var nearTexture = GetOrCreateSharedTexture(fresnelMaterial.Slot, Config.GetValue(NEAR_TEXTURE));
					fresnelMaterial.NearTexture.Target = nearTexture;
				}

				// === Texture Offset Setup ===
				// Setup texture offset drivers if they don't exist
				if (!fresnelMaterial.FarTextureOffset.IsLinked) {
					panner.Target = fresnelMaterial.FarTextureOffset;
				}

				if (!fresnelMaterial.NearTextureOffset.IsLinked) {
					ValueDriver<float2> newNearDrive = fresnelMaterial.Slot.GetComponentOrAttach<ValueDriver<float2>>();
					newNearDrive.DriveTarget.Target = fresnelMaterial.NearTextureOffset;
					newNearDrive.ValueSource.Target = panner.Target;
				}
			}
			catch (Exception e) {
				UniLog.Error($"Error in ProtoWireScroll OnChanges patch: {e}");
			}
		}
	}

	// Harmony patch for ProtoFluxWireManager's Setup method to handle wire configuration
	// This patch ensures proper wire orientation and appearance based on wire type
	[HarmonyPatch(typeof(ProtoFluxWireManager), "Setup")]
	class ProtoFluxWireManager_Setup_Patch {        
		// UV scale constants for wire texture direction
		// Default is 1.0 for normal direction, inverted is -1.0 for flipped direction
		private const float DEFAULT_UV_SCALE = 1f;
		private const float INVERTED_UV_SCALE = -1f;

		// Postfix patch that runs after the original Setup method
		// Handles wire mesh configuration based on wire type
		// __instance: The ProtoFluxWireManager instance being patched
		// type: The type of wire being configured (Input/Output/Reference)
		// ____wireMesh: Reference to the wire's mesh component
		public static void Postfix(ProtoFluxWireManager __instance, WireType type, SyncRef<StripeWireMesh> ____wireMesh) {
			try {
				// Skip configuration if basic requirements aren't met
				if (!IsValidSetup(__instance, ____wireMesh)) return;
				
				// Only allow configuration by the wire's owner
				if (!HasPermission(__instance)) return;

				// Apply wire-type specific configuration
				ConfigureWireByType(__instance, ____wireMesh.Target, type);
			}
			catch (Exception e) {
				UniLog.Error($"Error in ProtoFluxWireManager_Setup_Patch: {e.Message}");
			}
		}

		// Validates that all required components are present and the mod is enabled
		// Returns: True if setup is valid, false otherwise
		private static bool IsValidSetup(ProtoFluxWireManager instance, SyncRef<StripeWireMesh> wireMesh) {
			return Config.GetValue(ENABLED) && 
				   instance != null && 
				   wireMesh?.Target != null;
		}

		// Checks if the current user has permission to modify the wire
		// This ensures only the wire's owner can modify its properties
		// Returns: True if user has permission, false otherwise
		private static bool HasPermission(ProtoFluxWireManager instance) {
			// Extract IDs to determine wire ownership
			instance.Slot.ReferenceID.ExtractIDs(out ulong position, out byte user);
			User wirePointAllocUser = instance.World.GetUserByAllocationID(user);
			
			// Handle cases where primary allocation check fails
			if (wirePointAllocUser == null || position < wirePointAllocUser.AllocationIDStart) {
				// Try secondary instance check
				instance.ReferenceID.ExtractIDs(out ulong position1, out byte user1);
				User instanceAllocUser = instance.World.GetUserByAllocationID(user1);
				
				return instanceAllocUser != null && 
					   position1 >= instanceAllocUser.AllocationIDStart && 
					   instanceAllocUser == instance.LocalUser;
			}
			
			return wirePointAllocUser == instance.LocalUser;
		}

		// Configures wire mesh properties based on wire type
		// Each type (Input/Output/Reference) has specific orientation and texture settings
		private static void ConfigureWireByType(ProtoFluxWireManager instance, StripeWireMesh wireMesh, WireType type) {
			switch(type) {
				case WireType.Input:
					ConfigureInputWire(wireMesh);
					break;

				case WireType.Output:
					ConfigureOutputWire(wireMesh);
					break;

				case WireType.Reference:
					ConfigureReferenceWire(wireMesh);
					break;

				default:
					UniLog.Warning($"Unexpected wire type: {type}");
					return;
			}

			// Prevent UV scale from being changed after configuration
			LockUVScale(instance, wireMesh);
		}

		// Configures an input wire with:
		// - Inverted UV scale for correct texture direction
		// - Left-pointing tangent
		// - Input orientation for both ends
		private static void ConfigureInputWire(StripeWireMesh wireMesh) {
			wireMesh.UVScale.Value = new float2(INVERTED_UV_SCALE, ProtoFluxWireManager.WIRE_ATLAS_RATIO);
			wireMesh.Tangent0.Value = float3.Left * ProtoFluxWireManager.TANGENT_MAGNITUDE;
			wireMesh.Orientation0.Value = ProtoFluxWireManager.WIRE_ORIENTATION_INPUT;
			wireMesh.Orientation1.Value = ProtoFluxWireManager.WIRE_ORIENTATION_INPUT;
		}

		// Configures an output wire with:
		// - Default UV scale for normal texture direction
		// - Right-pointing tangent
		// - Output orientation for both ends
		private static void ConfigureOutputWire(StripeWireMesh wireMesh) {
			wireMesh.UVScale.Value = new float2(DEFAULT_UV_SCALE, ProtoFluxWireManager.WIRE_ATLAS_RATIO);
			wireMesh.Tangent0.Value = float3.Right * ProtoFluxWireManager.TANGENT_MAGNITUDE;
			wireMesh.Orientation0.Value = ProtoFluxWireManager.WIRE_ORIENTATION_OUTPUT;
			wireMesh.Orientation1.Value = ProtoFluxWireManager.WIRE_ORIENTATION_OUTPUT;
		}

		// Configures a reference wire with:
		// - Default UV scale for normal texture direction
		// - Downward-pointing tangent
		// - Reference orientation for both ends
		private static void ConfigureReferenceWire(StripeWireMesh wireMesh) {
			wireMesh.UVScale.Value = new float2(DEFAULT_UV_SCALE, ProtoFluxWireManager.WIRE_ATLAS_RATIO);
			wireMesh.Tangent0.Value = float3.Down * ProtoFluxWireManager.TANGENT_MAGNITUDE;
			wireMesh.Orientation0.Value = ProtoFluxWireManager.WIRE_ORIENTATION_REFERENCE;
			wireMesh.Orientation1.Value = ProtoFluxWireManager.WIRE_ORIENTATION_REFERENCE;
		}

		// Locks the UV scale to prevent changes after initial configuration
		// This ensures wire appearance remains consistent
		private static void LockUVScale(ProtoFluxWireManager instance, StripeWireMesh wireMesh) {
			var valueCopy = instance.Slot.GetComponentOrAttach<ValueCopy<float2>>();
			valueCopy.Source.Target = wireMesh.UVScale;
			valueCopy.Target.Target = wireMesh.UVScale;
		}
	}


	/// Creates or retrieves a shared FresnelMaterial for wire rendering
	private static FresnelMaterial GetOrCreateSharedMaterial(Slot slot) {
		// Create material in temporary storage
		FresnelMaterial fresnelMaterial = slot.World.RootSlot
			.FindChildOrAdd("__TEMP", false)
			.FindChildOrAdd($"{slot.LocalUser.UserName}-Scrolling-ProtofluxWire", false)
			.GetComponentOrAttach<FresnelMaterial>();

		// Ensure cleanup when user leaves
		fresnelMaterial.Slot.GetComponentOrAttach<DestroyOnUserLeave>().TargetUser.Target = slot.LocalUser;
		
		// Configure material properties
		fresnelMaterial.NearColor.Value = new colorX(0.8f);
		fresnelMaterial.FarColor.Value = new colorX(1.4f);
		fresnelMaterial.Sidedness.Value = Sidedness.Double;
		fresnelMaterial.UseVertexColors.Value = true;
		fresnelMaterial.BlendMode.Value = BlendMode.Alpha;
		fresnelMaterial.ZWrite.Value = ZWrite.On;
		
		// Setup textures from config
		var farTexture = GetOrCreateSharedTexture(fresnelMaterial.Slot, Config.GetValue(FAR_TEXTURE));
		fresnelMaterial.FarTexture.Target = farTexture;
		
		var nearTexture = GetOrCreateSharedTexture(fresnelMaterial.Slot, Config.GetValue(NEAR_TEXTURE));
		fresnelMaterial.NearTexture.Target = nearTexture;
		
		return fresnelMaterial;
	}

	/// Creates or retrieves a shared texture with specified settings
	private static StaticTexture2D GetOrCreateSharedTexture(Slot slot, Uri uri) {
		// Get or create the texture
		StaticTexture2D texture = slot.GetComponentOrAttach<StaticTexture2D>();
		texture.URL.Value = uri;

		// Configure texture properties from user settings
		texture.FilterMode.Value = Config.GetValue(FILTER_MODE);
		texture.MipMaps.Value = Config.GetValue(MIPMAPS);
		texture.Uncompressed.Value = Config.GetValue(UNCOMPRESSED);
		texture.CrunchCompressed.Value = Config.GetValue(CRUNCH_COMPRESSED);
		texture.DirectLoad.Value = Config.GetValue(DIRECT_LOAD);
		texture.ForceExactVariant.Value = Config.GetValue(FORCE_EXACT_VARIANT);
		texture.AnisotropicLevel.Value = Config.GetValue(ANISOTROPIC_LEVEL);
		texture.WrapModeU.Value = Config.GetValue(WRAP_MODE_U);
		texture.WrapModeV.Value = Config.GetValue(WRAP_MODE_V);
		texture.KeepOriginalMipMaps.Value = Config.GetValue(KEEP_ORIGINAL_MIPMAPS);
		texture.MipMapFilter.Value = Config.GetValue(MIPMAP_FILTER);
		texture.Readable.Value = Config.GetValue(READABLE);
		texture.PowerOfTwoAlignThreshold.Value = 0.05f;  // For proper texture alignment
		
		return texture;
	}
}
