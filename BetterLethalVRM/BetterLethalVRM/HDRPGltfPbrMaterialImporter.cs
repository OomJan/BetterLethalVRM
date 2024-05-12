using System;
using System.Collections.Generic;
using UniGLTF;
using UnityEngine;
using VRMShaders;
using ColorSpace = VRMShaders.ColorSpace;

namespace OomJan.BetterLethalVRM;

/// <summary>
/// glTF PBR to URP Lit.
/// see: https://github.com/Unity-Technologies/Graphics/blob/v7.5.3/com.unity.render-pipelines.universal/Editor/UniversalRenderPipelineMaterialUpgrader.cs#L354-L379
/// </summary>
public static class HDRPGltfPbrMaterialImporter
{
    public const string ShaderName = "HDRP/Lit";

    private static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
    private static readonly int Cutoff = Shader.PropertyToID("_Cutoff");

    public static Shader Shader => Shader.Find(ShaderName);

    public static bool TryCreateParam(GltfData Data, int i, out MaterialDescriptor MatDesc)
    {
        if (i < 0 || i >= Data.GLTF.materials.Count)
        {
            MatDesc = default;
            return false;
        }

        var tTextureSlots = new Dictionary<string, TextureDescriptor>();
        var tFloatValues = new Dictionary<string, float>();
        var tColours = new Dictionary<string, Color>();
        var tVectors = new Dictionary<string, Vector4>();
        var tActions = new List<Action<Material>>();
        var tSrc = Data.GLTF.materials[i];

        TextureDescriptor? tStandardTexDesc = default;
        if (tSrc.pbrMetallicRoughness != null || tSrc.occlusionTexture != null)
        {
            if (tSrc.pbrMetallicRoughness.metallicRoughnessTexture != null || tSrc.occlusionTexture != null)
                if (GltfPbrTextureImporter.TryStandardTexture(Data, tSrc, out var key, out var desc))
                {
                    if (string.IsNullOrEmpty(desc.UnityObjectName)) throw new ArgumentNullException();
                    tStandardTexDesc = desc;
                }

            if (tSrc.pbrMetallicRoughness.baseColorFactor != null &&
                tSrc.pbrMetallicRoughness.baseColorFactor.Length == 4)
                // from _Color !
                tColours.Add("_BaseColor",
                    tSrc.pbrMetallicRoughness.baseColorFactor.ToColor4(ColorSpace.Linear, ColorSpace.sRGB)
                );

            if (tSrc.pbrMetallicRoughness.baseColorTexture != null &&
                tSrc.pbrMetallicRoughness.baseColorTexture.index != -1)
                if (GltfPbrTextureImporter.TryBaseColorTexture(Data, tSrc, out var key, out var desc))
                    // from _MainTex !
                    tTextureSlots.Add("_BaseMap", desc);

            if (tSrc.pbrMetallicRoughness.metallicRoughnessTexture != null &&
                tSrc.pbrMetallicRoughness.metallicRoughnessTexture.index != -1 && tStandardTexDesc.HasValue)
            {
                tActions.Add(material => material.EnableKeyword("_METALLICSPECGLOSSMAP"));
                tTextureSlots.Add("_MetallicGlossMap", tStandardTexDesc.Value);
                // Set 1.0f as hard-coded. See: https://github.com/dwango/UniVRM/issues/212.
                tFloatValues.Add("_Metallic", 1.0f);
                tFloatValues.Add("_GlossMapScale", 1.0f);
                // default value is 0.5 !
                tFloatValues.Add("_Smoothness", 1.0f);
            }
            else
            {
                tFloatValues.Add("_Metallic", tSrc.pbrMetallicRoughness.metallicFactor);
                // from _Glossiness !
                tFloatValues.Add("_Smoothness", 1.0f - tSrc.pbrMetallicRoughness.roughnessFactor);
            }
        }

        if (tSrc.normalTexture != null && tSrc.normalTexture.index != -1)
        {
            tActions.Add(material => material.EnableKeyword("_NORMALMAP"));
            if (GltfPbrTextureImporter.TryNormalTexture(Data, tSrc, out var key, out var desc))
            {
                tTextureSlots.Add("_BumpMap", desc);
                tFloatValues.Add("_BumpScale", tSrc.normalTexture.scale);
            }
        }

        if (tSrc.occlusionTexture != null && tSrc.occlusionTexture.index != -1 && tStandardTexDesc.HasValue)
        {
            tTextureSlots.Add("_OcclusionMap", tStandardTexDesc.Value);
            tFloatValues.Add("_OcclusionStrength", tSrc.occlusionTexture.strength);
        }

        if (tSrc.emissiveFactor != null
            || (tSrc.emissiveTexture != null && tSrc.emissiveTexture.index != -1))
        {
            tActions.Add(material =>
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            });

            var emissiveFactor = GltfMaterialImportUtils.ImportLinearEmissiveFactor(Data, tSrc);
            if (emissiveFactor.HasValue) tColours.Add("_EmissionColor", emissiveFactor.Value);

            if (tSrc.emissiveTexture != null && tSrc.emissiveTexture.index != -1)
                if (GltfPbrTextureImporter.TryEmissiveTexture(Data, tSrc, out var key, out var desc))
                    tTextureSlots.Add("_EmissionMap", desc);
        }

        tActions.Add(material =>
        {
            var blendMode = BlendMode.Opaque;
            // https://forum.unity.com/threads/standard-material-shader-ignoring-setfloat-property-_mode.344557/#post-2229980
            switch (tSrc.alphaMode)
            {
                case "BLEND":
                    blendMode = BlendMode.Fade;
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt(SrcBlend, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt(DstBlend, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt(ZWrite, 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    break;

                case "MASK":
                    blendMode = BlendMode.Cutout;
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.SetInt(SrcBlend, (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt(DstBlend, (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt(ZWrite, 1);
                    material.SetFloat(Cutoff, tSrc.alphaCutoff);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 2450;

                    break;

                default: // OPAQUE
                    blendMode = BlendMode.Opaque;
                    material.SetOverrideTag("RenderType", "");
                    material.SetInt(SrcBlend, (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt(DstBlend, (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt(ZWrite, 1);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = -1;
                    break;
            }

            material.SetFloat("_Mode", (float)blendMode);
        });

        MatDesc = new MaterialDescriptor(
            GltfMaterialImportUtils.ImportMaterialName(i, tSrc),
            Shader,
            null,
            tTextureSlots,
            tFloatValues,
            tColours,
            tVectors,
            tActions);
        return true;
    }

    private enum BlendMode
    {
        Opaque,
        Cutout,
        Fade,
        Transparent
    }
}