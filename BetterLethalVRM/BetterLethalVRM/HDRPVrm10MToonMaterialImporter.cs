using System;
using System.Linq;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VRMShaders;
using VRMShaders.VRM10.MToon10.Runtime;
using GltfDeserializer = UniGLTF.Extensions.VRMC_materials_mtoon.GltfDeserializer;

namespace OomJan.BetterLethalVRM;

/// <summary>
/// Convert MToon parameters from glTF specification to Unity implementation.(for URP)
/// </summary>
public static class HDRPVrm10MToonMaterialImporter
{
    public static Shader MToonReplacementShader;

    public static bool TryCreateParam(GltfData Data, int i, out MaterialDescriptor MatDesc)
    {
        var m = Data.GLTF.materials[i];
        if (!GltfDeserializer.TryGet(m.extensions, out var tMToon))
        {
            // Fallback to glTF, when MToon extension does not exist.
            MatDesc = default;
            return false;
        }

        // use material.name, because material name may renamed in GltfParser.
        MatDesc = new MaterialDescriptor(
            m.name,
            MToonReplacementShader,
            null,
            Vrm10MToonTextureImporter.EnumerateAllTextures(Data, m, tMToon)
                .ToDictionary(tuple => tuple.Item1, tuple => tuple.Item2.Item2),
            BuiltInVrm10MToonMaterialImporter.TryGetAllFloats(m, tMToon)
                .ToDictionary(tuple => tuple.key, tuple => tuple.value),
            BuiltInVrm10MToonMaterialImporter.TryGetAllColors(m, tMToon)
                .ToDictionary(tuple => tuple.key, tuple => tuple.value),
            BuiltInVrm10MToonMaterialImporter.TryGetAllFloatArrays(m, tMToon)
                .ToDictionary(tuple => tuple.key, tuple => tuple.value),
            new Action<Material>[]
            {
                material =>
                {
                    // Set hidden properties, keywords from float properties.
                    new MToonValidator(material).Validate();
                }
            });

        return true;
    }
}