using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using GameNetcodeStuff;
using OomJan;
using UniGLTF;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UniVRM10;

namespace OomJan.BetterLethalVRM;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class BetterLethalVRMManager : BaseUnityPlugin
{
    private const string MODEL_PATH = "VRMs";
    private readonly SortedDictionary<ulong, BetterLethalVRMInstance> Instances = new();
    private readonly SortedDictionary<ulong, PlayerControllerB> PlayersBySteamID = new();
    private Material HDRP_BaseMaterial;
    private GameObject PlayerPrefab;
    private float PlayerPrefabHeight;

    private PlayerControllerB[] PlayerControllers;

    private bool RequirePlayerUpdate;

    public void Awake()
    {
        AssetBundle tAssetBundle = null;

        try
        {
            var tURI = new UriBuilder(Assembly.GetExecutingAssembly().CodeBase);
            var tPath = Uri.UnescapeDataString(tURI.Path);
            var tBundlePath = Path.Combine(new FileInfo(tPath).Directory.FullName, "bundle.asset");

            if (File.Exists(tBundlePath))
                tAssetBundle = AssetBundle.LoadFromFile(tBundlePath);
        }
        catch (Exception e)
        {
            tAssetBundle = null;
        }

        if (tAssetBundle == null)
        {
            enabled = false;
            Debug.LogError( "BetterLethalVRM failed to load it's asset bundle, this mod will not function");
            return;
        }

        // The MToon replacement shader has all the same texture properties as the regular MToon shader, but they are unused.
        HDRPVrm10MToonMaterialImporter.MToonReplacementShader = (Shader)tAssetBundle.LoadAsset("MToonParameterShader");
        if (HDRPVrm10MToonMaterialImporter.MToonReplacementShader == null)
        {
            enabled = false;
            Debug.LogError("BetterLethalVRM failed to load the MToon replacement shader, this mod will not function");
            return;
        }

        // Trigger to check for new player controllers on scene load
        SceneManager.sceneLoaded += (_, _) => SceneLoad();

        // Keep this object alive forever, through scene unloads and more
        gameObject.hideFlags = HideFlags.HideAndDontSave;

        // This mod requires this path to be accessible
        if (!Directory.Exists(MODEL_PATH)) Directory.CreateDirectory(MODEL_PATH);
        if (!Directory.Exists(MODEL_PATH))
        {
            enabled = false;
            Debug.LogError("BetterLethalVRM failed to create directory for models, this mod will not function");
        }
    }

    private void LateUpdate()
    {
        FindUpdatedIDs();
        FindMaskEnemies();
        AnimateBonePairs();

        if (RequirePlayerUpdate)
        {
            FindPlayerControllers();
            RequirePlayerUpdate = false;
        }
    }

    private void SceneLoad()
    {
        // When we are not in a lobby we dont need any VRM so we clean up everything
        if (GameNetworkManager.Instance?.currentLobby == null)
        {
            // Cleaning up
            foreach (var InstancesValue in Instances.Values)
            {

                if (InstancesValue.Vrm10Instance != null && InstancesValue.Vrm10Instance.gameObject != null)
                    Destroy(InstancesValue.Vrm10Instance.gameObject);
            }

            PlayersBySteamID.Clear();
            Instances.Clear();
        }

        FindPlayerControllers();
        PreparePrefabs();

        RequirePlayerUpdate = true;
    }

    private void PreparePrefabs()
    {
        // Prepare the T-pose player prefab, this will live until the application quits
        if (PlayerPrefab == null)
        {
            var basePlayer = PlayerControllers.FirstOrDefault(x => x.name == "Player");
            if (basePlayer != null)
            {
                basePlayer.gameObject.SetActive(false);
                PlayerPrefab = Instantiate(basePlayer.gameObject);
                PlayerPrefab.name = "VRM T Pose Match";
                PlayerPrefab.hideFlags = HideFlags.HideAndDontSave;

                var HDRP_CutoffMaterial = GameObject.Find("CatwalkShip").GetComponent<MeshRenderer>().material;
                HDRP_BaseMaterial = new Material(HDRP_CutoffMaterial);
                HDRP_BaseMaterial.SetFloat("_Smoothness", 0f);
                HDRP_BaseMaterial.SetFloat("_Metallic", 0f);
                HDRP_BaseMaterial.SetTextureScale("_BaseColorMap", Vector2.one);

                basePlayer.gameObject.SetActive(true);
                PlayerPrefab.GetComponentInChildren<Animator>().enabled = false;
                PlayerPrefab.transform.FindDescendant("arm.L_upper").localRotation = Quaternion.Euler(0, 90, -10);
                PlayerPrefab.transform.FindDescendant("arm.R_upper").localRotation = Quaternion.Euler(-10, 0, 0);
                PlayerPrefab.transform.FindDescendant("thigh.L").localRotation = Quaternion.Euler(20, 180, 180);
                PlayerPrefab.transform.FindDescendant("thigh.R").localRotation = Quaternion.Euler(20, 180, 180);
                PlayerPrefab.transform.FindDescendant("hand.R").localRotation = Quaternion.Euler(0, 90, 0);
                PlayerPrefab.transform.FindDescendant("hand.L").localRotation = Quaternion.Euler(0, 270, 0);

                var footL = PlayerPrefab.transform.FindDescendant("foot.L");
                var footR = PlayerPrefab.transform.FindDescendant("foot.R");
                var head = PlayerPrefab.transform.FindDescendant("spine.004_end");

                var p1 = new Vector3(0, ((footL.position + footR.position) / 2).y, 0);
                var p2 = new Vector3(0, head.position.y, 0);
                PlayerPrefabHeight = Vector3.Distance(p1, p2);

                PlayerPrefab.transform.rotation = Quaternion.identity;
                Debug.Log(
                    $"BetterLethalVRM base prefab set to {PlayerPrefab.name}, has a height of {PlayerPrefabHeight:0.###}");
            }
        }
    }

    private void FindPlayerControllers()
    {
        PlayerControllers = FindObjectsOfType<PlayerControllerB>(true);
        Debug.Log($"BetterLethalVRM found {PlayerControllers.Length} player controllers");
    }

    private void FindUpdatedIDs()
    {
        foreach (var tPlayer in PlayerControllers)
        {
            if (tPlayer.playerSteamId == 0) continue;

            // Remove instances and dict entries for disconnected PlayerControllers
            if (PlayersBySteamID.ContainsKey(tPlayer.playerSteamId) &&
                (tPlayer.disconnectedMidGame || (tPlayer.NetworkObject?.OwnerClientId == 0 && tPlayer.name != "Player")))
            {
                if (Instances.TryGetValue(tPlayer.playerSteamId, out var p2)) Destroy(p2.Vrm10Instance.gameObject);

                PlayersBySteamID.Remove(tPlayer.playerSteamId);
                Instances.Remove(tPlayer.playerSteamId);

                continue;
            }

            if (tPlayer.NetworkObject?.OwnerClientId == 0 && tPlayer.name != "Player") continue;

            // Add new PlayerControllers to the Id dict. Try and load models for the steamId
            if (PlayersBySteamID.TryAdd(tPlayer.playerSteamId, tPlayer))
            {
                if (!File.Exists($"{MODEL_PATH}/{tPlayer.playerSteamId}_{tPlayer.playerUsername}.txt"))
                    File.WriteAllText($"{MODEL_PATH}/{tPlayer.playerSteamId}_{tPlayer.playerUsername}.txt",
                        $"{tPlayer.playerSteamId} seen as {tPlayer.playerUsername}");

                // We already have an instance and thus don't need to do anything
                if (Instances.ContainsKey(tPlayer.playerSteamId)) continue;

                var path = $"{MODEL_PATH}/{tPlayer.playerSteamId}.vrm";
                if (File.Exists(path))
                {
                    Debug.Log($"BetterLethalVRM trying to load model for path {path}");
                    LoadModelToPlayer(path, tPlayer);
                }
                else
                {
                    path = $"{MODEL_PATH}/{tPlayer.playerUsername}.vrm";
                    if (File.Exists(path))
                    {
                        Debug.Log($"BetterLethalVRM trying to load model for path {path}");
                        LoadModelToPlayer(path, tPlayer);
                    }
                    else
                    {
                        // Fallback
                        path = $"{MODEL_PATH}/fallback.vrm";
                        if (File.Exists(path))
                        {
                            Debug.Log($"BetterLethalVRM trying to load fallback model for path {path}");
                            LoadModelToPlayer(path, tPlayer);
                        }
                    }
                }
            }
        }
    }

    private void FindMaskEnemies()
    {
        // Disabled until further notice as this seems to be causing major lag when there are masked enemies mimicking a player.
        //var tMaskedEnemies = FindObjectsByType<MaskedPlayerEnemy>(FindObjectsSortMode.None);
        //if (tMaskedEnemies.Any())
        //{
        //    foreach (var tMaskedEnemy in tMaskedEnemies)
        //    {
        //        // If we dont have a transform or its not mimicking a player, we dont do anything
        //        if (tMaskedEnemy.transform == null || tMaskedEnemy.mimickingPlayer == null) continue;

        //        if (Instances.TryGetValue(tMaskedEnemy.mimickingPlayer.playerSteamId, out var tInstance) &&
        //            tMaskedEnemy.transform != tInstance.PlayerControllerB.transform)
        //        {
        //            Debug.Log($"BetterLethalVRM Mask mimicking {tInstance.PlayerControllerB.name}");
        //            tInstance.SetSkeletonMimic(tMaskedEnemy.transform);
        //        }
        //    }
        //}
    }

    private async void LoadModelToPlayer(string Path, PlayerControllerB Player)
    {

        // Let VRM do it's thing, it spits all kinds of errors if things go wrong
        var tInstance = await Vrm10.LoadPathAsync(Path, materialGenerator: new HDRPVrm10MaterialDescriptorGenerator());

        if (tInstance == null)
        {
            enabled = false;

            Debug.LogError($"BetterLethalVRM had an error loading the VRM at {Path}, this mod will not function");

            return;
        }

        tInstance.name = $"BetterLethalVRM Character Model {Player.playerUsername} {Player.playerSteamId}";
        tInstance.transform.position = Player.transform.position;

        // Create instance for BetterLethalVRM
        var tNewInstance = new BetterLethalVRMInstance();

        // Replace VRM materials with Lethal Company shader materials
        if (HDRP_BaseMaterial == null)
        {
            enabled = false;

            Debug.LogError(
                "BetterLethalVRM had some error loading the Lethal Company shader material, this mod will not function");

            return;
        }

        foreach (var tRenderer in tInstance.GetComponentsInChildren<Renderer>(true))
        {
            tNewInstance.Renderers.Add(tRenderer);

            tRenderer.receiveShadows = true;
            tRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

            if (tRenderer is SkinnedMeshRenderer skinnedMeshRenderer) skinnedMeshRenderer.updateWhenOffscreen = true;

            var newMaterials = new Material[tRenderer.materials.Length];
            for (var i = 0; i < tRenderer.materials.Length; i++)
            {
                var m = tRenderer.materials[i];
                var newM = new Material(HDRP_BaseMaterial)
                {
                    name = m.name,
                    mainTexture = m.mainTexture
                };

                if (m.HasProperty("_M_CullMode")) newM.SetFloat("_CullMode", m.GetFloat("_M_CullMode"));
                if (m.HasProperty("_BumpMap")) newM.SetTexture("_NormalMap", m.GetTexture("_BumpMap"));

                newMaterials[i] = newM;
            }

            tRenderer.materials = newMaterials;
        }

        // Disable the VRM animators
        var tAnimator = tInstance.Runtime.ControlRig.ControlRigAnimator;
        tAnimator.enabled = false;
        tInstance.Runtime.VrmAnimation = null;

        // Transform names -> Unity bone names
        (string name, HumanBodyBones bone)[] boneNames =
        {
            ("spine", HumanBodyBones.Hips),
            ("spine.001", HumanBodyBones.Spine),
            ("spine.002", HumanBodyBones.Chest),
            ("spine.003", HumanBodyBones.UpperChest),
            ("spine.004", HumanBodyBones.Neck),

            ("shoulder.R", HumanBodyBones.RightShoulder),
            ("arm.R_upper", HumanBodyBones.RightUpperArm),
            ("arm.R_lower", HumanBodyBones.RightLowerArm),
            ("hand.R", HumanBodyBones.RightHand),

            ("shoulder.L", HumanBodyBones.LeftShoulder),
            ("arm.L_upper", HumanBodyBones.LeftUpperArm),
            ("arm.L_lower", HumanBodyBones.LeftLowerArm),
            ("hand.L", HumanBodyBones.LeftHand),

            ("thigh.R", HumanBodyBones.RightUpperLeg),
            ("shin.R", HumanBodyBones.RightLowerLeg),
            ("foot.R", HumanBodyBones.RightFoot),

            ("thigh.L", HumanBodyBones.LeftUpperLeg),
            ("shin.L", HumanBodyBones.LeftLowerLeg),
            ("foot.L", HumanBodyBones.LeftFoot),

            /////////////////////

            ("finger1.L", HumanBodyBones.LeftThumbProximal),
            ("finger1.L.001", HumanBodyBones.LeftThumbIntermediate),
            ("finger1.L.001_end", HumanBodyBones.LeftThumbDistal),

            ("finger2.L", HumanBodyBones.LeftIndexProximal),
            ("finger2.L.001", HumanBodyBones.LeftIndexIntermediate),
            ("finger2.L.001_end", HumanBodyBones.LeftIndexDistal),

            ("finger3.L", HumanBodyBones.LeftMiddleProximal),
            ("finger3.L.001", HumanBodyBones.LeftMiddleIntermediate),
            ("finger3.L.001_end", HumanBodyBones.LeftMiddleDistal),

            ("finger4.L", HumanBodyBones.LeftRingProximal),
            ("finger4.L.001", HumanBodyBones.LeftRingIntermediate),
            ("finger4.L.001_end", HumanBodyBones.LeftRingDistal),

            ("finger5.L", HumanBodyBones.LeftLittleProximal),
            ("finger5.L.001", HumanBodyBones.LeftLittleIntermediate),
            ("finger5.L.001_end", HumanBodyBones.LeftLittleDistal),

            /////////////////////

            ("finger1.R", HumanBodyBones.RightThumbProximal),
            ("finger1.R.001", HumanBodyBones.RightThumbIntermediate),
            ("finger1.R.001_end", HumanBodyBones.RightThumbDistal),

            ("finger2.R", HumanBodyBones.RightIndexProximal),
            ("finger2.R.001", HumanBodyBones.RightIndexIntermediate),
            ("finger2.R.001_end", HumanBodyBones.RightIndexDistal),

            ("finger3.R", HumanBodyBones.RightMiddleProximal),
            ("finger3.R.001", HumanBodyBones.RightMiddleIntermediate),
            ("finger3.R.001_end", HumanBodyBones.RightMiddleDistal),

            ("finger4.R", HumanBodyBones.RightRingProximal),
            ("finger4.R.001", HumanBodyBones.RightRingIntermediate),
            ("finger4.R.001_end", HumanBodyBones.RightRingDistal),

            ("finger5.R", HumanBodyBones.RightLittleProximal),
            ("finger5.R.001", HumanBodyBones.RightLittleIntermediate),
            ("finger5.R.001_end", HumanBodyBones.RightLittleDistal)
        };

        // Add extra bones to each player bone to use as reference for world angles
        // Better way to do this? Probably but I'm not good at math
        if (PlayerPrefab == null)
        {
            enabled = false;

            Debug.LogError("BetterLethalVRM failed to find the player prefab, this mod will not function");

            return;
        }

        HashSet<(Transform target, Transform source, Quaternion localRotation)> boneTranslation = new();
        foreach (var tBone in boneNames)
        {
            var targetT = tAnimator.GetBoneTransform(tBone.bone);
            if (targetT == null)
            {
                Debug.Log($"BetterLethalVRM {Path} missing bone {tBone.bone} ({tBone.name})");
                continue;
            }

            var srcT = Player.transform.FindDescendant(tBone.name);
            var poseT = PlayerPrefab.transform.FindDescendant(tBone.name);

            var newBone = new GameObject("VRM Rotation Bone").transform;
            newBone.parent = poseT;
            newBone.position = poseT.position;
            newBone.rotation = targetT.rotation;
            var localRotation = newBone.localRotation;
            newBone.parent = srcT;
            newBone.position = srcT.position;
            newBone.localRotation = localRotation;

            boneTranslation.Add((targetT, newBone, localRotation));
        }

        // Calculate VRM height for scaling the player to the correct size
        var p1 = new Vector3(0, tInstance.Humanoid.Head.position.y, 0);
        var p2 = new Vector3(0, tInstance.transform.position.y, 0);
        var height = Vector3.Distance(p1, p2);
        var playerScale = PlayerPrefabHeight / height;
        tInstance.transform.localScale = new Vector3(playerScale, playerScale, playerScale);
        Debug.Log($"BetterLethalVRM {Path} has a height of: {height:0.###}, scaling to {playerScale:0.###}");

        // Calculate distance from feet to hips to offset the player hips for different leg lengths
        var tVRMHipHeight = Vector3.Distance(tInstance.Humanoid.Hips.position,
            (tInstance.Humanoid.LeftFoot.position + tInstance.Humanoid.RightFoot.position) / 2f);
        var tHead = PlayerPrefab.transform.FindDescendant("spine");
        var tLeftFoot = PlayerPrefab.transform.FindDescendant("foot.L");
        var tRightFoot = PlayerPrefab.transform.FindDescendant("foot.R");
        var tLethalHipHeight = Vector3.Distance(tHead.position, (tLeftFoot.position + tRightFoot.position) / 2f);

        // Set player renderer visibility, done by name to prevent hiding special renderers like first-person arms etc
        Player.transform.FindDescendant("LOD1").gameObject.SetActive(false);
        Player.transform.FindDescendant("LOD2").gameObject.SetActive(false);
        Player.transform.FindDescendant("LOD3").gameObject.SetActive(false);
        Player.transform.FindDescendant("LevelSticker").gameObject.SetActive(false);
        Player.transform.FindDescendant("BetaBadge").gameObject.SetActive(false);

        // SpringBone
        foreach (var springBone in tInstance.SpringBone.Springs)
        {
            springBone.Center = null;

            // Springbones are super jiggly and thus we increase the stiffness, perhaps I will make the configurable with an external file
            foreach (var joint in springBone.Joints) joint.m_stiffnessForce *= 10.0f;
        }

        // Since we made changes to the bones, we have to reconstruct them
        tInstance.Runtime.ReconstructSpringBone();

        // Add new player instance to the set
        tNewInstance.Vrm10Instance = tInstance;
        tNewInstance.PlayerControllerB = Player;
        tNewInstance.HipOffset = tVRMHipHeight - tLethalHipHeight;
        tNewInstance.BoneTranslation = boneTranslation;

        Instances.Add(tNewInstance.PlayerControllerB.playerSteamId, tNewInstance);

        Debug.Log($"BetterLethalVRM finished loading {Path}");
    }

    private void AnimateBonePairs()
    {
        var tToRemove = new List<BetterLethalVRMInstance>();

        foreach (var tInstance in Instances.Values)
        {
            // Remove instance if the player is destroyed
            if (tInstance.Vrm10Instance == null || tInstance.PlayerControllerB == null)
            {
                tToRemove.Add(tInstance);
                continue;
            }

            // Remove the dead body if root is destroyed
            if (tInstance.DeadBodyRoot == null)
            {
                tInstance.DeadMap = null;
                tInstance.DeadBodyRoot = null;
            }

            // Remove instance if the player lost some bones
            var exit = false;
            foreach (var tBoneTranslation in tInstance.BoneTranslation)
            {
                if (tBoneTranslation.target == null || tBoneTranslation.source == null)
                {
                    tToRemove.Add(tInstance);
                    exit = true;
                    break;
                }

                // If a dead body bone is missing, clear the dead body
                if (tInstance.DeadMap != null && tInstance.DeadMap[tBoneTranslation.source] == null)
                {
                    tInstance.DeadMap = null;
                    tInstance.DeadBodyRoot = null;
                }
            }

            if (exit) continue;

            // Clear the dead body if the player is alive or the dead body no longer exists
            if (!tInstance.PlayerControllerB.isPlayerDead || tInstance.DeadBodyRoot == null)
            {
                tInstance.DeadMap = null;
                tInstance.DeadBodyRoot = null;
            }

            // Prepare dead body if it changed
            if (tInstance.PlayerControllerB.isPlayerDead && tInstance.DeadBodyRoot == null)
                if (tInstance.PlayerControllerB.deadBody != null)
                    tInstance.SetSkeletonMimic(tInstance.PlayerControllerB.deadBody.transform);

            // Position bones
            foreach (var tBoneTranslation in tInstance.BoneTranslation)
            {
                tBoneTranslation.target.rotation = tInstance.DeadMap != null ? tInstance.DeadMap[tBoneTranslation.source].rotation : tBoneTranslation.source.rotation;
                if (tBoneTranslation.source.parent.name == "spine")
                {
                    tBoneTranslation.target.position = tInstance.DeadMap != null ? tInstance.DeadMap[tBoneTranslation.source].position : tBoneTranslation.source.position;
                    tBoneTranslation.target.position += tBoneTranslation.target.up * tInstance.HipOffset;
                }
            }

            // Set layer and renderer visibility
            tInstance.UpdateVisibility();
        }

        // Remove instances flagged for deletion
        if (tToRemove.Any())
            foreach (var i in tToRemove)
            {
                Instances.Remove(i.PlayerControllerB.playerSteamId);
                PlayersBySteamID.Remove(i.PlayerControllerB.playerSteamId);
            }
    }
}