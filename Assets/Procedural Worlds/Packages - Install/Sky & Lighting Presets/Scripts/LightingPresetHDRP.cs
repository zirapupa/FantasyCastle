using Gaia.GXC.Kronnect;
using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;
#if HDPipeline
using UnityEngine.Rendering.HighDefinition;
using Fog = UnityEngine.Rendering.HighDefinition.Fog;
#endif


namespace Gaia
{
    /// <summary>
    /// Holds all relevant settings and prefab references for setting up lighting in the HD render pipeline
    /// </summary>
    /// 
    [CreateAssetMenu(menuName = "Procedural Worlds/Gaia/Lighting Preset HDRP")]
    public class LightingPresetHDRP : ScriptableObject
    {
        public string m_displayName;
        public GameObject m_directionalLightPrefab;
        public GameObject m_environmentPrefab;
        public GameObject m_globalPostProcessingPrefab;
        public GameObject m_worldDensityPrefab;
#if HDPipeline
        public VolumeProfile m_envVolumeProfile;
        public VolumeProfile m_gppVolumeProfile;
#endif
        public bool m_currentOverwritePermission = false;

        [HideInInspector]
        public bool m_enableDynamicDepthOfField = true;
        [HideInInspector]
        public bool m_autoAdjustFogHeight = true;

        public float m_originTerrainMinHeight = 0.0f;
        public float m_originTerrainMaxHeight = 300.0f;
        public float m_targetTerrainMinHeight = 0.0f;
        public float m_targetTerrainMaxHeight = 300.0f;

        public Camera m_mainCamera;
        // private string m_lastCreatedProfile; // Removed: Will use EditorPrefs instead

        private GaiaSessionManager m_sessionManager;
        private GaiaSessionManager SessionManager
        {
            get
            {
                if (m_sessionManager == null)
                {
                    m_sessionManager = GaiaSessionManager.GetSessionManager(false);
                }
                return m_sessionManager;
            }
        }

#if UNITY_EDITOR
        // Helper method to get last created profile path from EditorPrefs
        private string GetLastCreatedProfilePathForPreset(string originalPresetVolumeProfileAssetPath)
        {
            if (string.IsNullOrEmpty(originalPresetVolumeProfileAssetPath)) return null;
            // Using a slightly more specific key prefix for HDRP
            return EditorPrefs.GetString($"Gaia.LightingPresetHDRP.LastCreated.{originalPresetVolumeProfileAssetPath}", null);
        }

        // Helper method to set last created profile path in EditorPrefs
        private void SetLastCreatedProfilePathForPreset(string originalPresetVolumeProfileAssetPath, string lastCreatedPathInSessionFolder)
        {
            if (string.IsNullOrEmpty(originalPresetVolumeProfileAssetPath)) return;

            if (string.IsNullOrEmpty(lastCreatedPathInSessionFolder))
            {
                EditorPrefs.DeleteKey($"Gaia.LightingPresetHDRP.LastCreated.{originalPresetVolumeProfileAssetPath}");
            }
            else
            {
                EditorPrefs.SetString($"Gaia.LightingPresetHDRP.LastCreated.{originalPresetVolumeProfileAssetPath}", lastCreatedPathInSessionFolder);
            }
        }
#endif

        public void Apply()
        {
#if UNITY_EDITOR && HDPipeline
            GameObject lightingObject = GaiaUtils.GetLightingObject(false);

            RemoveFromScene();
            lightingObject = GaiaUtils.GetLightingObject(true);

            var allLights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < allLights.Length; i++)
            {
                Light light = allLights[i];
                if (light.type == LightType.Directional)
                {
                    light.gameObject.SetActive(false);
                }
            }

            VolumeProfile copiedLightingVolume = null;
            VolumeProfile copiedPostProcessingVolume = null;
            string originalEnvProfilePath = null;
            string originalGppProfilePath = null;

            if (m_envVolumeProfile != null)
            {
                originalEnvProfilePath = AssetDatabase.GetAssetPath(m_envVolumeProfile);
            }
            if (m_gppVolumeProfile != null)
            {
                originalGppProfilePath = AssetDatabase.GetAssetPath(m_gppVolumeProfile);
            }

            string targetFolder = GaiaDirectories.GetHDRPLightingProfilePathForSession();
            if (!AssetDatabase.IsValidFolder(targetFolder))
            {
                // More robust folder creation
                string[] folderParts = targetFolder.Split('/');
                string currentPath = folderParts[0];
                for (int i = 1; i < folderParts.Length; i++)
                {
                    if (!AssetDatabase.IsValidFolder(currentPath + "/" + folderParts[i]))
                    {
                        AssetDatabase.CreateFolder(currentPath, folderParts[i]);
                    }
                    currentPath += "/" + folderParts[i];
                }
                AssetDatabase.Refresh();
            }

            // Reset permissions for each call to CopyVolumeProfileAsset, if the user gives permission once it will count for both files
            m_currentOverwritePermission = false;


            if (m_envVolumeProfile != null)
            {
                copiedLightingVolume = CopyVolumeProfileAsset(originalEnvProfilePath, targetFolder);
                if (copiedLightingVolume != null)
                {
                    EditorUtility.SetDirty(copiedLightingVolume);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    copiedLightingVolume = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GetAssetPath(copiedLightingVolume));
                     if (copiedLightingVolume == null)
                    {
                        Debug.LogError($"HDRP: Failed to re-load copiedLightingVolume ({originalEnvProfilePath}) after save/refresh.");
                    }
                }
            }

            if (m_gppVolumeProfile != null)
            {
                copiedPostProcessingVolume = CopyVolumeProfileAsset(originalGppProfilePath, targetFolder);
                if (copiedPostProcessingVolume != null)
                {
                    EditorUtility.SetDirty(copiedPostProcessingVolume);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    copiedPostProcessingVolume = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GetAssetPath(copiedPostProcessingVolume));
                    if (copiedPostProcessingVolume == null)
                    {
                        Debug.LogError($"HDRP: Failed to re-load copiedPostProcessingVolume ({originalGppProfilePath}) after save/refresh.");
                    }
                }
            }
           
            if (m_directionalLightPrefab != null)
            {
                GameObject.Instantiate(m_directionalLightPrefab, lightingObject.transform);
            }

            if (m_environmentPrefab != null)
            {
               GameObject envGO =  GameObject.Instantiate(m_environmentPrefab, lightingObject.transform);
                Volume vol = envGO.GetComponent<Volume>();
                if (vol != null && copiedLightingVolume != null)
                {
                    Undo.RecordObject(vol, "Assign Copied Environment Profile");
                    vol.sharedProfile = copiedLightingVolume; // Use sharedProfile
                    EditorUtility.SetDirty(vol);
                    if (PrefabUtility.IsPartOfPrefabInstance(envGO))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(vol);
                    }
                    if (vol.sharedProfile != copiedLightingVolume)
                         Debug.LogError($"HDRP: FAILED to assign Environment profile. Is {vol.sharedProfile?.name}, expected {copiedLightingVolume.name}");
                }

                if (m_autoAdjustFogHeight && SessionManager !=null)
                {
                    float targetTerrainMinHeight = 0;
                    float targetTerrainMaxHeight = 150;

                    SessionManager.GetWorldMinMax(ref targetTerrainMinHeight, ref targetTerrainMaxHeight);
                    MapFogHeight(vol.sharedProfile, m_originTerrainMinHeight, m_originTerrainMaxHeight, targetTerrainMinHeight, targetTerrainMaxHeight);
                }
            }

            if (m_globalPostProcessingPrefab != null)
            {
                GameObject postProcessingGO = GameObject.Instantiate(m_globalPostProcessingPrefab, lightingObject.transform);
                Volume vol = postProcessingGO.GetComponent<Volume>();
                if(vol != null && copiedPostProcessingVolume != null)
                {
                    Undo.RecordObject(vol, "Assign Copied GPP Profile");
                    vol.sharedProfile = copiedPostProcessingVolume; // Use sharedProfile
                    EditorUtility.SetDirty(vol);
                     if (PrefabUtility.IsPartOfPrefabInstance(postProcessingGO))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(vol);
                    }
                    if (vol.sharedProfile != copiedPostProcessingVolume)
                         Debug.LogError($"HDRP: FAILED to assign GPP profile. Is {vol.sharedProfile?.name}, expected {copiedPostProcessingVolume.name}");

                }
                if (m_enableDynamicDepthOfField == true)
                {
                    if(m_mainCamera == null)
                    {
                        m_mainCamera = Camera.main;
                        if (m_mainCamera == null)
                        {
                            Debug.LogError("No camera assigned or found!, Please assign the Camera in the PostProcessing object in the Dynamic Depth of Field script");
                        }
                    }
                    if (m_mainCamera != null && !m_mainCamera.gameObject.GetComponent<DynamicDepthOfField>())
                    {
                        DynamicDepthOfField DDOF = m_mainCamera.gameObject.AddComponent<DynamicDepthOfField>();
                        DDOF.mainCamera = m_mainCamera;
                    }
                }
            }
            if (m_worldDensityPrefab != null)
            {
                GameObject.Instantiate(m_worldDensityPrefab, lightingObject.transform);
            }

            


#endif
        }

#if HDPipeline
        private void MapFogHeight(VolumeProfile yourVolumeProfile, float sourceMin, float sourceMax, float targetMin, float targetMax)
        {
#if UNITY_EDITOR
            if (yourVolumeProfile == null)
            {
                Debug.LogWarning("FogHeightMapper: Provided volume profile is null.");
                return;
            }

            

            VolumeProfile profile = yourVolumeProfile;

            SerializedObject serializedProfile = new SerializedObject(profile);

            // Find the components array
            SerializedProperty componentsProperty = serializedProfile.FindProperty("components");

            // Find or create fog component
            SerializedProperty fogProperty = null;
            for (int i = 0; i < componentsProperty.arraySize; i++)
            {
                SerializedProperty component = componentsProperty.GetArrayElementAtIndex(i);
                if (component.objectReferenceValue is Fog)
                {
                    fogProperty = component;
                    break;
                }
            }

            // If fog component doesn't exist, add it
            if (fogProperty == null)
            {
                profile.Add<Fog>(false);
                serializedProfile.Update();
                // Re-find the fog component
                for (int i = 0; i < componentsProperty.arraySize; i++)
                {
                    SerializedProperty component = componentsProperty.GetArrayElementAtIndex(i);
                    if (component.objectReferenceValue is Fog)
                    {
                        fogProperty = component;
                        break;
                    }
                }
            }

            if (fogProperty != null && fogProperty.objectReferenceValue is Fog fog)
            {
                SerializedObject fogSO = new SerializedObject(fog);

                // Modify the parameters
                SerializedProperty maxHeight = fogSO.FindProperty("maximumHeight.m_Value");
                SerializedProperty baseHeight = fogSO.FindProperty("baseHeight.m_Value");


                float sourceRange = Mathf.Max(0.001f, sourceMax - sourceMin);
                float targetRange = Mathf.Max(0.001f, targetMax - targetMin);

                // Base height offset
                float adjustedBaseHeight = fog.baseHeight.value + (targetMin - sourceMin);

                // Scale fog thickness
                float sourceMaxHeightOffset = fog.maximumHeight.value - fog.baseHeight.value;
                float scaledMaxHeightOffset = sourceMaxHeightOffset * (targetRange / sourceRange);

                float adjustedMaxHeight = adjustedBaseHeight + scaledMaxHeightOffset / 2f;

                maxHeight.floatValue = adjustedMaxHeight;
                baseHeight.floatValue = adjustedBaseHeight;

                fogSO.ApplyModifiedProperties();
            }

            serializedProfile.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
#endif
        }
#endif

#if UNITY_EDITOR && HDPipeline
        // originalPresetVolumeProfileAssetPath is the path of m_envVolumeProfile or m_gppVolumeProfile from this SO.
        private VolumeProfile CopyVolumeProfileAsset(string originalPresetVolumeProfileAssetPath, string targetFolder)
        {
            if (string.IsNullOrEmpty(originalPresetVolumeProfileAssetPath) || !AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(originalPresetVolumeProfileAssetPath))
            {
                Debug.LogError($"HDRP Error: Original asset at path '{originalPresetVolumeProfileAssetPath}' not found or path is invalid. Unable to copy.");
                return null;
            }

            string lastKnownCopiedProfilePath = GetLastCreatedProfilePathForPreset(originalPresetVolumeProfileAssetPath);
            string originalFileName = System.IO.Path.GetFileName(originalPresetVolumeProfileAssetPath);
            string baseDestinationPath = System.IO.Path.Combine(targetFolder, originalFileName);
            string pathToUseForCopy;

            bool baseFileExists = System.IO.File.Exists(baseDestinationPath);
            bool lastKnownCopiedFileExistsAndRelevant = !string.IsNullOrEmpty(lastKnownCopiedProfilePath) &&
                                                        System.IO.Path.GetDirectoryName(lastKnownCopiedProfilePath).Replace("\\", "/") == targetFolder.Replace("\\", "/") &&
                                                        System.IO.File.Exists(lastKnownCopiedProfilePath);
           

            if (baseFileExists || lastKnownCopiedFileExistsAndRelevant)
            {
                string suggestedOverwritePath = baseDestinationPath;
                if (lastKnownCopiedFileExistsAndRelevant)
                {
                    suggestedOverwritePath = lastKnownCopiedProfilePath;
                }

                if (!m_currentOverwritePermission)
                {

                    string overwriteTargetDisplay = System.IO.Path.GetFileName(suggestedOverwritePath);
                    string dialogMessage = $"A lighting profile for '{originalFileName}' already exists in the session folder. Should Gaia overwrite existing files or create a new copy?";
                    if (overwriteTargetDisplay == originalFileName && suggestedOverwritePath == baseDestinationPath)
                    {
                        dialogMessage = $"The lighting profile '{originalFileName}' already exists in the session folder. Overwrite it or create a new copy?";
                    }

                    // Use local dialog decision variables
                    if (EditorUtility.DisplayDialog("Lighting Profile Exists (HDRP)", dialogMessage, "Overwrite Existing", "Create New Copy"))
                    {
                        m_currentOverwritePermission = true;
                    }
                }
                if (m_currentOverwritePermission)
                {
                    pathToUseForCopy = suggestedOverwritePath;
                    AssetDatabase.CopyAsset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);
                }
                else // (currentKeepPermission must be true)
                {
                    pathToUseForCopy = AssetDatabase.GenerateUniqueAssetPath(baseDestinationPath);
                    AssetDatabase.CopyAsset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);
                }
            }
            else
            {
                pathToUseForCopy = baseDestinationPath;
                //Debug.Log($"HDRP: No existing profile at base path '{baseDestinationPath}' or relevant last copy. Copying directly: {pathToUseForCopy}");
                AssetDatabase.CopyAsset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);
            }

            SetLastCreatedProfilePathForPreset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            VolumeProfile loadedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(pathToUseForCopy);
            if (loadedProfile == null)
            {
                Debug.LogError($"HDRP: Failed to load the copied VolumeProfile from path: {pathToUseForCopy}. Original: {originalPresetVolumeProfileAssetPath}");
            }
            //else
            //{
            //    Debug.Log($"HDRP: Successfully copied  and loaded VolumeProfile: {loadedProfile.name} from {pathToUseForCopy}");
            //}
            return loadedProfile;
        }
#endif
        public void RemoveFromScene()
        {
            GameObject lightingObject = GaiaUtils.GetLightingObject(false);
            if (lightingObject != null)
            {
                if (Application.isPlaying)
                {
                    GameObject.Destroy(lightingObject);
                }
                else
                {
                    GameObject.DestroyImmediate(lightingObject);
                }
            }
        }

        public void IngestFromScene()
        {
#if UNITY_EDITOR
            string currentPath = AssetDatabase.GetAssetPath(this);
            currentPath = currentPath.Substring(0, currentPath.LastIndexOf("/"));
            var allLights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < allLights.Length; i++)
            {
                Light light = allLights[i];
                if (light.type == LightType.Directional)
                {
                    string savePath = currentPath + "/" + this.name + " DirectionalLight.prefab";
                    PrefabUtility.SaveAsPrefabAsset(light.gameObject, savePath);
                    m_directionalLightPrefab = (GameObject)AssetDatabase.LoadAssetAtPath(savePath, typeof(GameObject));
                    break;
                }
            }

#if HDPipeline
            HDRPDensityVolumeController dvc = GameObject.FindFirstObjectByType<HDRPDensityVolumeController>();
            if (dvc != null )
            {
                string savePath = currentPath + "/" + this.name + " World Density.prefab";
                PrefabUtility.SaveAsPrefabAsset(dvc.gameObject, savePath);
                m_worldDensityPrefab = (GameObject)AssetDatabase.LoadAssetAtPath(savePath, typeof(GameObject));
            }
            var allPPVolumes = GameObject.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            for (int i = 0; i < allPPVolumes.Length; i++)
            {
                Volume volume = allPPVolumes[i];
                if (volume.name.Equals(GaiaConstants.HDRPEnvironmentObject))
                {
                    string savePath = currentPath + "/" + this.name + " Environment.prefab";
                    PrefabUtility.SaveAsPrefabAsset(volume.gameObject, savePath);
                    m_environmentPrefab = (GameObject)AssetDatabase.LoadAssetAtPath(savePath, typeof(GameObject));
                }

                if (volume.name.Equals(GaiaConstants.HDRPPostProcessingObject))
                {
                    string savePath = currentPath + "/" + this.name + " Post Processing.prefab";
                    PrefabUtility.SaveAsPrefabAsset(volume.gameObject, savePath);
                    m_globalPostProcessingPrefab = (GameObject)AssetDatabase.LoadAssetAtPath(savePath, typeof(GameObject));
                }
            }
#endif
            EditorUtility.SetDirty(this);
#endif
        }
    }
}