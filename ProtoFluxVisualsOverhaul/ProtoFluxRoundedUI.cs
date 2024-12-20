using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine.ProtoFlux;
using FrooxEngine.UIX;
using Elements.Core;
using System;
using System.Collections.Generic;
using Elements.Assets;
using System.Linq;
using System.Reflection;
using ProtoFlux.Core;

namespace ProtoFluxVisualsOverhaul {
    // Shared permission check method for all patches
    public static class PermissionHelper {
        public static bool HasPermission(ProtoFluxNodeVisual instance) {
            if (instance == null || instance.World == null) return false;

            // Check the visual component's node first
            var node = instance.Node.Target;
            if (node != null) {
                // Primary check - Check the node's slot ownership
                node.Slot.ReferenceID.ExtractIDs(out ulong nodePosition, out byte nodeUser);
                User nodeSlotAllocUser = instance.World.GetUserByAllocationID(nodeUser);

                // Check if the node slot belongs to the local user
                if (nodeSlotAllocUser == null || nodePosition < nodeSlotAllocUser.AllocationIDStart) {
                    // Secondary check - Check the node component ownership
                    node.ReferenceID.ExtractIDs(out ulong nodeCompPosition, out byte nodeCompUser);
                    User nodeComponentAllocUser = instance.World.GetUserByAllocationID(nodeCompUser);

                    // If neither the slot nor component belong to the local user, check group nodes
                    if (nodeComponentAllocUser == null || 
                        nodeCompPosition < nodeComponentAllocUser.AllocationIDStart || 
                        nodeComponentAllocUser != instance.LocalUser) {
                        
                        // Additional check for node group ownership through its nodes
                        var group = node.Group;
                        if (group != null) {
                            // Check if any node in the group belongs to a different user
                            foreach (var groupNode in group.Nodes) {
                                if (groupNode == null) continue;
                                
                                groupNode.ReferenceID.ExtractIDs(out ulong groupNodePosition, out byte groupNodeUser);
                                User groupNodeAllocUser = instance.World.GetUserByAllocationID(groupNodeUser);

                                if (groupNodeAllocUser != null && 
                                    groupNodePosition >= groupNodeAllocUser.AllocationIDStart && 
                                    groupNodeAllocUser != instance.LocalUser) {
                                    return false;
                                }
                            }
                        }
                    }
                }
                else if (nodeSlotAllocUser != instance.LocalUser) {
                    return false;
                }
            }

            // Then check the visual component's own ownership
            instance.ReferenceID.ExtractIDs(out ulong position, out byte user);
            User visualAllocUser = instance.World.GetUserByAllocationID(user);

            if (visualAllocUser == null || position < visualAllocUser.AllocationIDStart) {
                // Check the slot ownership as fallback
                instance.Slot.ReferenceID.ExtractIDs(out ulong slotPosition, out byte slotUser);
                User slotAllocUser = instance.World.GetUserByAllocationID(slotUser);

                return slotAllocUser != null && 
                       slotPosition >= slotAllocUser.AllocationIDStart && 
                       slotAllocUser == instance.LocalUser;
            }

            return visualAllocUser == instance.LocalUser;
        }
    }

    // Patch to add rounded corners to ProtoFlux node visuals
    [HarmonyPatch(typeof(ProtoFluxNodeVisual), "BuildUI")]
    public class ProtoFluxNodeVisual_BuildUI_Patch {
        // ColorMyProtoFlux color settings
        private static readonly colorX NODE_CATEGORY_TEXT_LIGHT_COLOR = new colorX(0.75f);
        private static readonly colorX NODE_CATEGORY_TEXT_DARK_COLOR = new colorX(0.25f);

        // Cache for shared sprite provider
        private static readonly Dictionary<(Slot, bool), SpriteProvider> connectorSpriteCache = new Dictionary<(Slot, bool), SpriteProvider>();

        private static Dictionary<(Slot, bool), SpriteProvider> callConnectorSpriteCache = new Dictionary<(Slot, bool), SpriteProvider>();

        /// <summary>
        /// Determines if a connector should use the Call sprite based on its type
        /// </summary>
        private static bool ShouldUseCallConnector(ImpulseType? impulseType, bool isOperation = false, bool isAsync = false) {
            // If it's any ImpulseType, use the flow connector
            if (impulseType.HasValue) {
                return true;
            }
            
            // For operations, check if it's a flow connector
            if (isOperation) {
                return true; // Operations use flow connectors
            }
            
            return false;
        }

        /// <summary>
        /// Creates or retrieves a shared sprite provider for the connector image
        /// </summary>
        public static SpriteProvider GetOrCreateSharedConnectorSprite(Slot slot, bool isOutput, ImpulseType? impulseType = null, bool isOperation = false, bool isAsync = false) {
            // Check if this should use the Call connector
            if (ShouldUseCallConnector(impulseType, isOperation, isAsync)) {
                return GetOrCreateSharedCallConnectorSprite(slot, isOutput);
            }
            
            var cacheKey = (slot, isOutput);
            
            // Check cache first
            if (connectorSpriteCache.TryGetValue(cacheKey, out var cachedProvider)) {
                return cachedProvider;
            }

            // Create sprite in temporary storage
            SpriteProvider spriteProvider = slot.World.RootSlot
                .FindChildOrAdd("__TEMP", false)
                .FindChildOrAdd($"{slot.LocalUser.UserName}-Connector-Sprite-{(isOutput ? "Output" : "Input")}", false)
                .GetComponentOrAttach<SpriteProvider>();

            // Ensure cleanup when user leaves
            spriteProvider.Slot.GetComponentOrAttach<DestroyOnUserLeave>().TargetUser.Target = slot.LocalUser;

            // Set up the texture if not already set
            if (spriteProvider.Texture.Target == null) {
                var texture = spriteProvider.Slot.AttachComponent<StaticTexture2D>();
                texture.URL.Value = isOutput ? 
                    ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CONNECTOR_OUTPUT_TEXTURE) : 
                    ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CONNECTOR_INPUT_TEXTURE);
                texture.FilterMode.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FILTER_MODE);
                texture.WrapModeU.Value = TextureWrapMode.Clamp;
                texture.WrapModeV.Value = TextureWrapMode.Clamp;
                texture.MipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAPS);
                texture.MipMapFilter.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAP_FILTER);
                texture.AnisotropicLevel.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ANISOTROPIC_LEVEL);
                texture.KeepOriginalMipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.KEEP_ORIGINAL_MIPMAPS);
                texture.CrunchCompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CRUNCH_COMPRESSED);
                texture.Readable.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.READABLE);
                texture.Uncompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.UNCOMPRESSED);
                texture.DirectLoad.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.DIRECT_LOAD);
                texture.ForceExactVariant.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FORCE_EXACT_VARIANT);
                texture.PreferredFormat.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_FORMAT);
                texture.PreferredProfile.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_PROFILE);
                
                spriteProvider.Texture.Target = texture;
                spriteProvider.Rect.Value = !isOutput ? 
                    new Rect(0f, 0f, 1f, 1f) :    // Inputs (left) normal orientation
                    new Rect(1f, 0f, -1f, 1f);    // Outputs (right) flipped
                spriteProvider.Scale.Value = 1.0f;
                spriteProvider.FixedSize.Value = 16f; // Match the RectTransform width
                spriteProvider.Borders.Value = new float4(0f, 0f, 0.0001f, 0f); // x=0, y=0, z=0.01, w=0
            }

            // Cache the provider
            connectorSpriteCache[cacheKey] = spriteProvider;

            return spriteProvider;
        }

        /// <summary>
        /// Creates or retrieves a shared sprite provider for the Call connector image
        /// </summary>
        public static SpriteProvider GetOrCreateSharedCallConnectorSprite(Slot slot, bool isOutput) {
            var cacheKey = (slot, isOutput);
            
            // Check cache first
            if (callConnectorSpriteCache.TryGetValue(cacheKey, out var cachedProvider)) {
                return cachedProvider;
            }

            // Create sprite in temporary storage
            SpriteProvider spriteProvider = slot.World.RootSlot
                .FindChildOrAdd("__TEMP", false)
                .FindChildOrAdd($"{slot.LocalUser.UserName}-Call-Connector-Sprite-{(isOutput ? "Output" : "Input")}", false)
                .GetComponentOrAttach<SpriteProvider>();

            // Ensure cleanup when user leaves
            spriteProvider.Slot.GetComponentOrAttach<DestroyOnUserLeave>().TargetUser.Target = slot.LocalUser;

            // Set up the texture if not already set
            if (spriteProvider.Texture.Target == null) {
                var texture = spriteProvider.Slot.AttachComponent<StaticTexture2D>();
                texture.URL.Value = isOutput ? 
                    ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CALL_CONNECTOR_OUTPUT_TEXTURE) : 
                    ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CALL_CONNECTOR_INPUT_TEXTURE);
                texture.FilterMode.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FILTER_MODE);
                texture.WrapModeU.Value = TextureWrapMode.Clamp;
                texture.WrapModeV.Value = TextureWrapMode.Clamp;
                texture.MipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAPS);
                texture.MipMapFilter.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAP_FILTER);
                texture.AnisotropicLevel.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ANISOTROPIC_LEVEL);
                texture.KeepOriginalMipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.KEEP_ORIGINAL_MIPMAPS);
                texture.CrunchCompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CRUNCH_COMPRESSED);
                texture.Readable.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.READABLE);
                texture.Uncompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.UNCOMPRESSED);
                texture.DirectLoad.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.DIRECT_LOAD);
                texture.ForceExactVariant.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FORCE_EXACT_VARIANT);
                texture.PreferredFormat.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_FORMAT);
                texture.PreferredProfile.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_PROFILE);
                
                spriteProvider.Texture.Target = texture;
                spriteProvider.Rect.Value = !isOutput ? 
                    new Rect(0f, 0f, 1f, 1f) :    // Inputs (left) normal orientation
                    new Rect(1f, 0f, -1f, 1f);    // Outputs (right) flipped
                spriteProvider.Scale.Value = 1.0f;
                spriteProvider.FixedSize.Value = 16f; // Match the RectTransform width
                spriteProvider.Borders.Value = new float4(0f, 0f, 0.0001f, 0f); // x=0, y=0, z=0.01, w=0
            }

            // Cache the provider
            callConnectorSpriteCache[cacheKey] = spriteProvider;

            return spriteProvider;
        }

        public static void Postfix(ProtoFluxNodeVisual __instance, UIBuilder ui, ProtoFluxNode node) {
            try {
                // Skip if disabled
                if (!ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ENABLED)) return;

                // === User Permission Check ===
                if (!PermissionHelper.HasPermission(__instance)) return;

                // Find all connector images in the hierarchy
                var connectorSlots = ui.Root.GetComponentsInChildren<Image>()
                    .Where(img => img.Slot.Name == "Connector")
                    .ToList();

                foreach (var connectorImage in connectorSlots) {
                    // Determine if this is an output connector based on its RectTransform settings
                    bool isOutput = connectorImage.RectTransform.OffsetMin.Value.x < 0;
                    
                    // Check for ImpulseType by looking for ImpulseProxy or OperationProxy
                    var impulseProxy = connectorImage.Slot.GetComponent<ProtoFluxImpulseProxy>();
                    var operationProxy = connectorImage.Slot.GetComponent<ProtoFluxOperationProxy>();
                    
                    ImpulseType? impulseType = null;
                    bool isOperation = false;
                    bool isAsync = false;
                    
                    if (impulseProxy != null) {
                        impulseType = impulseProxy.ImpulseType.Value;
                    }
                    else if (operationProxy != null) {
                        isOperation = true;
                        isAsync = operationProxy.IsAsync.Value;
                    }
                    
                    // Get or create shared sprite provider with the correct type
                    var spriteProvider = GetOrCreateSharedConnectorSprite(connectorImage.Slot, isOutput, impulseType, isOperation, isAsync);
                    
                    // Apply the sprite provider to the connector image
                    connectorImage.Sprite.Target = spriteProvider;
                    connectorImage.PreserveAspect.Value = true;

                    // Set the correct RectTransform settings based on the original code
                    if (isOutput) {
                        connectorImage.RectTransform.SetFixedHorizontal(-16f, 0.0f, 1f);
                    } else {
                        connectorImage.RectTransform.SetFixedHorizontal(0.0f, 16f, 0.0f);
                    }

                    // Set the wire point anchor
                    var wirePoint = connectorImage.Slot.FindChild("<WIRE_POINT>");
                    if (wirePoint != null) {
                        var rectTransform = wirePoint.GetComponent<RectTransform>();
                        if (rectTransform != null) {
                            rectTransform.AnchorMin.Value = new float2(isOutput ? 1f : 0.0f, 0.5f);
                            rectTransform.AnchorMax.Value = new float2(isOutput ? 1f : 0.0f, 0.5f);
                        }
                    }
                }

                // Get the background image using reflection
                var bgImageRef = (SyncRef<Image>)AccessTools.Field(typeof(ProtoFluxNodeVisual), "_bgImage").GetValue(__instance);
                var bgImage = bgImageRef?.Target;
                if (bgImage != null) {
                    bgImage.Slot.OrderOffset = -2;
                }

                // Find the header panel (it's the first Image with HEADER color)
                var headerPanel = ui.Root.GetComponentsInChildren<Image>()
                    .FirstOrDefault(img => img.Tint.Value == RadiantUI_Constants.HEADER);

                if (headerPanel != null) {
                    // Get the text component that's a sibling
                    var headerText = headerPanel.Slot.Parent.GetComponentInChildren<Text>();
                    if (headerText == null) return;

                    // Create TitleParent with OrderOffset -1
                    var titleParentSlot = ui.Root.AddSlot("TitleParent");
                    titleParentSlot.OrderOffset = -1;

                    // Add RectTransform to parent
                    var rectTransform = titleParentSlot.AttachComponent<RectTransform>();
                    
                    // Add overlapping layout to parent with exact settings
                    var overlappingLayout = titleParentSlot.AttachComponent<OverlappingLayout>();
                    overlappingLayout.PaddingTop.Value = 5.5f;
                    overlappingLayout.PaddingRight.Value = 5.5f;
                    overlappingLayout.PaddingBottom.Value = 2.5f;
                    overlappingLayout.PaddingLeft.Value = 5.5f;
                    overlappingLayout.HorizontalAlign.Value = LayoutHorizontalAlignment.Center;
                    overlappingLayout.VerticalAlign.Value = LayoutVerticalAlignment.Middle;
                    overlappingLayout.ForceExpandWidth.Value = true;
                    overlappingLayout.ForceExpandHeight.Value = true;

                    // Add LayoutElement with exact settings from image
                    var layoutElement = titleParentSlot.AttachComponent<LayoutElement>();
                    layoutElement.MinWidth.Value = -1;
                    layoutElement.PreferredWidth.Value = -1;
                    layoutElement.FlexibleWidth.Value = -1;
                    layoutElement.MinHeight.Value = 24;
                    layoutElement.PreferredHeight.Value = -1;
                    layoutElement.FlexibleHeight.Value = -1;
                    layoutElement.Area.Value = -1;
                    layoutElement.Priority.Value = 1;

                    // Create a copy of the header panel under TitleParent
                    var newHeaderSlot = titleParentSlot.AddSlot("Header");
                    newHeaderSlot.ActiveSelf = true;
                    var image = newHeaderSlot.AttachComponent<Image>();
                    
                    // Get the node's type color for the header
                    var nodeTypeColor = DatatypeColorHelper.GetTypeColor(node.GetType());
                    ProtoFluxVisualsOverhaul.Msg($"🎨 Node type color: R:{nodeTypeColor.r:F2} G:{nodeTypeColor.g:F2} B:{nodeTypeColor.b:F2}");
                    
                    // Apply the color to the header image
                    image.Tint.Value = nodeTypeColor;
                    
                    // Create a copy of the text under the new header
                    var newTextSlot = newHeaderSlot.AddSlot("Text");
                    newTextSlot.ActiveSelf = true;
                    var newText = newTextSlot.AttachComponent<Text>();
                    var textRect = newText.RectTransform;
                    
                    // Set the anchors to stretch horizontally and vertically
                    textRect.AnchorMin.Value = new float2(0.028f, 0.098f);  // x:0.028 y:0.098
                    textRect.AnchorMax.Value = new float2(0.97f, 0.9f);     // x:0.97 y:0.9

                    // Apply text settings
                    newText.Size.Value = 64.00f;
                    newText.HorizontalAlign.Value = TextHorizontalAlignment.Center;
                    newText.VerticalAlign.Value = TextVerticalAlignment.Middle;
                    newText.AlignmentMode.Value = AlignmentMode.Geometric;
                    newText.LineHeight.Value = 0.80f;
                    newText.AutoSizeMin.Value = 8;
                    newText.AutoSizeMax.Value = 64;
                    newText.HorizontalAutoSize.Value = true;
                    newText.VerticalAutoSize.Value = true;
                    newText.ParseRichText.Value = true;
                    
                    // Calculate text color based on header image color for better contrast
                    var headerColor = image.Tint.Value;
                    ProtoFluxVisualsOverhaul.Msg($"🖼️ Header image color: R:{headerColor.r:F2} G:{headerColor.g:F2} B:{headerColor.b:F2}");
                    
                    var brightness = (headerColor.r * 0.299f + headerColor.g * 0.587f + headerColor.b * 0.114f);
                    ProtoFluxVisualsOverhaul.Msg($"✨ Calculated brightness: {brightness:F2}");
                    
                    var textColor = brightness > 0.6f ? colorX.Black : colorX.White;
                    ProtoFluxVisualsOverhaul.Msg($"📝 Setting text color to: {(brightness > 0.6f ? "BLACK" : "WHITE")} based on brightness");
                    
                    // Set text color multiple ways to ensure it takes effect
                    newText.Color.Value = textColor;
                    newText.Color.ForceSet(textColor);
                    newText.Size.Value = 9.00f;
                    newText.AutoSizeMin.Value = 4f;
                    
                    // Convert color to hex based on brightness
                    newText.Content.Value = $"<color={(brightness > 0.6f ? "#000000" : "#FFFFFF")}><b>{headerText.Content.Value}</b></color>";
                    
                    ProtoFluxVisualsOverhaul.Msg($"📝 Text color set to: R:{newText.Color.Value.r:F2} G:{newText.Color.Value.g:F2} B:{newText.Color.Value.b:F2}");
                    
                    // Copy RectTransform settings
                    var newHeaderRect = newHeaderSlot.AttachComponent<RectTransform>();
                    var originalRect = headerPanel.Slot.GetComponent<RectTransform>();
                    if (originalRect != null) {
                        newHeaderRect.AnchorMin.Value = originalRect.AnchorMin.Value;
                        newHeaderRect.AnchorMax.Value = originalRect.AnchorMax.Value;
                        newHeaderRect.OffsetMin.Value = originalRect.OffsetMin.Value;
                        newHeaderRect.OffsetMax.Value = originalRect.OffsetMax.Value;
                    }

                    // Disable the original header and text
                    headerPanel.Slot.ActiveSelf = false;
                    headerText.Slot.ActiveSelf = false;

                    // Apply rounded corners to the new header
                    ApplyRoundedCorners(image);

                    ProtoFluxVisualsOverhaul.Msg("✅ Successfully reorganized title layout!");
                }

                // Find the category text (it's the last Text component with dark gray color)
                var categoryText = ui.Root.GetComponentsInChildren<Text>()
                    .LastOrDefault(text => text.Color.Value == colorX.DarkGray);
                
                if (categoryText != null) {
                    categoryText.VerticalAlign.Value = TextVerticalAlignment.Middle;
                    categoryText.Size.Value = 8.00f;
                    categoryText.AlignmentMode.Value = AlignmentMode.LineBased;
                    categoryText.LineHeight.Value = 0.35f;
                }
            }
            catch (System.Exception e) {
                ProtoFluxVisualsOverhaul.Msg($"❌ Error in ProtoFluxNodeVisual_BuildUI_Patch: {e.Message}");
            }
        }

        private static void ApplyRoundedCorners(Image image) {
            // Skip if already applied
            if (image.Sprite.Target is SpriteProvider) return;

            ProtoFluxVisualsOverhaul.Msg("🐾 Applying rounded corners to header panel...");

            // Create a SpriteProvider for rounded corners
            var spriteProvider = image.Slot.AttachComponent<SpriteProvider>();
            ProtoFluxVisualsOverhaul.Msg("✅ Created SpriteProvider for header");
            
            // Set up the texture
            var texture = spriteProvider.Slot.AttachComponent<StaticTexture2D>();
            texture.URL.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.NODE_BACKGROUND_HEADER_TEXTURE);
            texture.FilterMode.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FILTER_MODE);
            texture.WrapModeU.Value = TextureWrapMode.Clamp;
            texture.WrapModeV.Value = TextureWrapMode.Clamp;
            texture.MipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAPS);
            texture.MipMapFilter.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAP_FILTER);
            texture.AnisotropicLevel.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ANISOTROPIC_LEVEL);
            texture.KeepOriginalMipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.KEEP_ORIGINAL_MIPMAPS);
            texture.CrunchCompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CRUNCH_COMPRESSED);
            texture.Readable.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.READABLE);
            texture.Uncompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.UNCOMPRESSED);
            texture.DirectLoad.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.DIRECT_LOAD);
            texture.ForceExactVariant.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FORCE_EXACT_VARIANT);
            texture.PreferredFormat.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_FORMAT);
            texture.PreferredProfile.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_PROFILE);
            ProtoFluxVisualsOverhaul.Msg("✅ Set up texture for header");
            
            // Configure the sprite provider based on the image settings
            spriteProvider.Texture.Target = texture;
            spriteProvider.Rect.Value = new Elements.Core.Rect(0f, 0f, 1f, 1f);  // x:0 y:0 width:1 height:1
            spriteProvider.Borders.Value = new float4(0.5f, 0.5f, 0.5f, 0.5f); 
            spriteProvider.Scale.Value = 0.03f;  // Scale: 0.05
            spriteProvider.FixedSize.Value = 1.00f;  // FixedSize: 1.00
            ProtoFluxVisualsOverhaul.Msg("✅ Configured header sprite provider settings");

            // Update the image to use the sprite
            image.Sprite.Target = spriteProvider;
            
            // Preserve color and tint settings
            image.PreserveAspect.Value = true;
            ProtoFluxVisualsOverhaul.Msg("✅ Successfully applied rounded corners to header!");
        }
    }

    // Patch to handle initial node creation
    [HarmonyPatch(typeof(ProtoFluxNodeVisual), "GenerateVisual")]
    public class ProtoFluxNodeVisual_GenerateVisual_Patch {
        public static void Postfix(ProtoFluxNodeVisual __instance) {
            try {
                // Skip if disabled
                if (!ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ENABLED)) return;

                // === User Permission Check ===
                if (!PermissionHelper.HasPermission(__instance)) return;

                // Find all connector slots in the hierarchy
                var connectorSlots = __instance.Slot.GetComponentsInChildren<Image>()
                    .Where(img => img.Slot.Name == "Connector")
                    .ToList();

                foreach (var connectorImage in connectorSlots) {
                    // Determine if this is an output connector based on its position
                    bool isOutput = connectorImage.RectTransform.OffsetMin.Value.x < 0;
                    
                    // Check for ImpulseType by looking for ImpulseProxy or OperationProxy
                    var impulseProxy = connectorImage.Slot.GetComponent<ProtoFluxImpulseProxy>();
                    var operationProxy = connectorImage.Slot.GetComponent<ProtoFluxOperationProxy>();
                    
                    ImpulseType? impulseType = null;
                    bool isOperation = false;
                    bool isAsync = false;
                    
                    if (impulseProxy != null) {
                        impulseType = impulseProxy.ImpulseType.Value;
                    }
                    else if (operationProxy != null) {
                        isOperation = true;
                        isAsync = operationProxy.IsAsync.Value;
                    }
                    
                    // Get or create shared sprite provider with the correct type
                    var spriteProvider = ProtoFluxNodeVisual_BuildUI_Patch.GetOrCreateSharedConnectorSprite(
                        connectorImage.Slot, 
                        isOutput, 
                        impulseType, 
                        isOperation, 
                        isAsync
                    );
                    
                    // Apply the sprite provider to the connector image
                    connectorImage.Sprite.Target = spriteProvider;
                    connectorImage.PreserveAspect.Value = true;
                    connectorImage.FlipHorizontally.Value = false; // We handle flipping in the sprite provider
                }
            } catch (Exception e) {
                UniLog.Error($"Error in ProtoFluxNodeVisual_GenerateVisual_Patch: {e}");
            }
        }
    }

    // Patch to handle dynamic connector creation
    [HarmonyPatch(typeof(ProtoFluxNodeVisual), "GenerateInputElement")]
    public class ProtoFluxNodeVisual_GenerateInputElement_Patch {
        public static void Postfix(ProtoFluxNodeVisual __instance) {
            try {
                // Skip if disabled
                if (!ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ENABLED)) return;

                // === User Permission Check ===
                if (!PermissionHelper.HasPermission(__instance)) return;

                // Find the most recently added connector
                var connectorImage = __instance.Slot.GetComponentsInChildren<Image>()
                    .Where(img => img.Slot.Name == "Connector")
                    .LastOrDefault();

                if (connectorImage != null) {
                    // Determine if this is an output connector based on its position
                    bool isOutput = connectorImage.RectTransform.OffsetMin.Value.x < 0;
                    
                    // Check for ImpulseType by looking for ImpulseProxy or OperationProxy
                    var impulseProxy = connectorImage.Slot.GetComponent<ProtoFluxImpulseProxy>();
                    var operationProxy = connectorImage.Slot.GetComponent<ProtoFluxOperationProxy>();
                    
                    ImpulseType? impulseType = null;
                    bool isOperation = false;
                    bool isAsync = false;
                    
                    if (impulseProxy != null) {
                        impulseType = impulseProxy.ImpulseType.Value;
                    }
                    else if (operationProxy != null) {
                        isOperation = true;
                        isAsync = operationProxy.IsAsync.Value;
                    }
                    
                    // Get or create shared sprite provider with the correct type
                    var spriteProvider = ProtoFluxNodeVisual_BuildUI_Patch.GetOrCreateSharedConnectorSprite(
                        connectorImage.Slot, 
                        isOutput, 
                        impulseType, 
                        isOperation, 
                        isAsync
                    );
                    
                    // Apply the sprite provider to the connector image
                    connectorImage.Sprite.Target = spriteProvider;
                    connectorImage.PreserveAspect.Value = true;
                }
            } catch (Exception e) {
                UniLog.Error($"Error in ProtoFluxNodeVisual_GenerateInputElement_Patch: {e}");
            }
        }
    }

    // Keep the OnChanges patch for the background image
    [HarmonyPatch(typeof(ProtoFluxNodeVisual), "OnChanges")]
    public class ProtoFluxNodeVisual_OnChanges_Patch {
        private static readonly FieldInfo bgImageField = AccessTools.Field(typeof(ProtoFluxNodeVisual), "_bgImage");

        public static void Postfix(ProtoFluxNodeVisual __instance) {
            try {
                // Skip if disabled
                if (!ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ENABLED)) return;

                // === User Permission Check ===
                if (!PermissionHelper.HasPermission(__instance)) return;

                // Get the background image component using reflection
                var bgImageRef = (SyncRef<Image>)bgImageField.GetValue(__instance);
                var bgImage = bgImageRef?.Target;
                if (bgImage == null) return;

                // Apply rounded corners to background
                ApplyRoundedCorners(bgImage);
            } catch (Exception e) {
                UniLog.Error($"Error in ProtoFluxNodeVisual_OnChanges_Patch: {e}");
            }
        }

        private static void ApplyRoundedCorners(Image image) {
            // Skip if already applied
            if (image.Sprite.Target is SpriteProvider) return;

            ProtoFluxVisualsOverhaul.Msg("🐾 Applying rounded corners to background...");

            // Create a SpriteProvider for rounded corners
            var spriteProvider = image.Slot.AttachComponent<SpriteProvider>();
            ProtoFluxVisualsOverhaul.Msg("✅ Created SpriteProvider for background");
            
            // Set up the texture
            var texture = spriteProvider.Slot.AttachComponent<StaticTexture2D>();
            texture.URL.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.NODE_BACKGROUND_TEXTURE);
            texture.FilterMode.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FILTER_MODE);
            texture.WrapModeU.Value = TextureWrapMode.Clamp;
            texture.WrapModeV.Value = TextureWrapMode.Clamp;
            texture.MipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAPS);
            texture.MipMapFilter.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.MIPMAP_FILTER);
            texture.AnisotropicLevel.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ANISOTROPIC_LEVEL);
            texture.KeepOriginalMipMaps.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.KEEP_ORIGINAL_MIPMAPS);
            texture.CrunchCompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.CRUNCH_COMPRESSED);
            texture.Readable.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.READABLE);
            texture.Uncompressed.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.UNCOMPRESSED);
            texture.DirectLoad.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.DIRECT_LOAD);
            texture.ForceExactVariant.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.FORCE_EXACT_VARIANT);
            texture.PreferredFormat.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_FORMAT);
            texture.PreferredProfile.Value = ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.PREFERRED_PROFILE);

            ProtoFluxVisualsOverhaul.Msg("✅ Set up texture for background");
            
            // Configure the sprite provider based on the image settings
            spriteProvider.Texture.Target = texture;
            spriteProvider.Rect.Value = new Elements.Core.Rect(0f, 0f, 1f, 1f);  // x:0 y:0 width:1 height:1
            spriteProvider.Borders.Value = new float4(0.5f, 0.5f, 0.5f, 0.5f);  // x:0.5 y:0 z:0 w:0
            spriteProvider.Scale.Value = 0.07f;  // Scale: 0.05
            spriteProvider.FixedSize.Value = 1.00f;  // FixedSize: 1.00
            ProtoFluxVisualsOverhaul.Msg("✅ Configured background sprite provider settings");

            // Update the image to use the sprite
            image.Sprite.Target = spriteProvider;
            
            // Preserve color and tint settings
            image.PreserveAspect.Value = true;
            ProtoFluxVisualsOverhaul.Msg("✅ Successfully applied rounded corners to background!");
        }
    }

    // Patch to sync wire colors with ImpulseType colors
    [HarmonyPatch(typeof(ProtoFluxWireManager), "Setup")]
    [HarmonyPatch(new Type[] { typeof(WireType), typeof(float), typeof(colorX), typeof(int), typeof(bool), typeof(bool) })]
    public class ProtoFluxWireManager_Setup_Patch {
        public static void Postfix(
            ProtoFluxWireManager __instance, 
            WireType type,
            float width,
            colorX startColor,
            int atlasOffset,
            bool collider,
            bool reverseTexture,
            SyncRef<StripeWireMesh> ____wireMesh) 
        {
            try {
                // Skip if disabled
                if (!ProtoFluxVisualsOverhaul.Config.GetValue(ProtoFluxVisualsOverhaul.ENABLED)) return;

                // Get the wire colors based on the atlas offset and type
                colorX color1 = startColor;
                colorX color2 = startColor;

                if (atlasOffset == 4) {
                    // This is a flow wire, check if it's async by looking at the color
                    bool isAsync = startColor.b > startColor.g; // Purple has more blue than green

                    // For flow wires, use the appropriate colors based on sync/async
                    if (type == WireType.Output) {
                        color1 = DatatypeColorHelper.GetOperationColor(isAsync);
                        color2 = startColor; // Keep target color
                    } else {
                        color1 = startColor; // Keep source color
                        color2 = DatatypeColorHelper.GetOperationColor(isAsync);
                    }
                }

                // Apply the colors to create a gradient
                __instance.StartColor.Value = color1;
                __instance.EndColor.Value = color2;

                // Also update the wire mesh colors
                if (____wireMesh?.Target != null) {
                    ____wireMesh.Target.Color0.Value = color1;
                    ____wireMesh.Target.Color1.Value = color2;
                }
            }
            catch (System.Exception e) {
                ProtoFluxVisualsOverhaul.Msg($"❌ Error in ProtoFluxWireManager_Setup_Patch: {e.Message}");
            }
        }
    }
} 