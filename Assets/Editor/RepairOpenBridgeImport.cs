#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DA_Assets.DAO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NavigationSim.EditorTools
{
    /// <summary>
    /// Repairs FCU import artifacts on the scene OpenBridge_FCU workspace:
    /// stretched BoldLine/Ship/Arrow/north-arrow sprites and DAOutlineEffect failures.
    /// Menu: Tools/Ship Bridge/Repair OpenBridge Import Artifacts
    /// </summary>
    internal static class RepairOpenBridgeImport
    {
        private const string ObjectName = "OpenBridge_FCU";
        private const string SessionKey = "NavigationSim.RepairOpenBridgeImport.Done.v2";

        private static readonly HashSet<string> StretchedSpriteNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "north-arrow",
            "BoldLine",
            "Ship",
            "Arrow-5",
            "arrow-medium",
            "Arrow"
        };

        [MenuItem("Tools/Ship Bridge/Repair OpenBridge Import Artifacts", false, 11)]
        private static void RepairFromMenu()
        {
            SessionState.SetBool(SessionKey, false);
            Repair(saveScene: true);
            SessionState.SetBool(SessionKey, true);
        }

        [InitializeOnLoadMethod]
        private static void AutoRepairOnce()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                Repair(saveScene: true);
                SessionState.SetBool(SessionKey, true);
            };
        }

        private static void Repair(bool saveScene)
        {
            GameObject root = FindSceneObject(ObjectName);
            if (root == null)
            {
                return;
            }

            int fixedCount = 0;
            fixedCount += RepairStretchedSprites(root.transform);
            fixedCount += RepairStretchedArrowContainers(root.transform);
            fixedCount += DisableOutlineEffects(root.transform);
            if (fixedCount <= 0)
            {
                return;
            }

            EditorUtility.SetDirty(root);
            Scene scene = root.scene;
            if (saveScene && scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"[OpenBridge Repair] Fixed {fixedCount} import artifact(s) on '{ObjectName}'.");
        }

        private static int RepairStretchedSprites(Transform root)
        {
            int fixedCount = 0;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null || image.sprite.texture == null)
                {
                    continue;
                }

                bool nameMatch = StretchedSpriteNames.Contains(image.name)
                                 || StretchedSpriteNames.Contains(image.sprite.name);
                if (!nameMatch)
                {
                    continue;
                }

                if (TryRepairStretchedRect(
                        image.rectTransform,
                        image.sprite.texture.width * 0.25f,
                        image.sprite.texture.height * 0.25f))
                {
                    EditorUtility.SetDirty(image);
                    fixedCount++;
                }
            }

            return fixedCount;
        }

        private static int RepairStretchedArrowContainers(Transform root)
        {
            int fixedCount = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "Arrow" || child is not RectTransform rect)
                {
                    continue;
                }

                if (!HasOutOfRangeAnchors(rect))
                {
                    continue;
                }

                float width = 36f;
                float height = 46f;
                Image childImage = child.GetComponentInChildren<Image>(true);
                if (childImage != null && childImage.sprite != null && childImage.sprite.texture != null)
                {
                    width = childImage.sprite.texture.width * 0.25f;
                    height = childImage.sprite.texture.height * 0.25f;
                }
                else
                {
                    foreach (Transform nested in child)
                    {
                        if (nested is RectTransform nestedRect && nestedRect.sizeDelta.sqrMagnitude > 1f)
                        {
                            width = nestedRect.sizeDelta.x;
                            height = nestedRect.sizeDelta.y;
                            break;
                        }
                    }
                }

                if (TryRepairStretchedRect(rect, width, height))
                {
                    EditorUtility.SetDirty(child.gameObject);
                    fixedCount++;
                }
            }

            return fixedCount;
        }

        private static bool HasOutOfRangeAnchors(RectTransform rect)
        {
            return rect.anchorMin.x < -0.01f || rect.anchorMin.y < -0.01f
                   || rect.anchorMax.x > 1.01f || rect.anchorMax.y > 1.01f;
        }

        private static bool TryRepairStretchedRect(RectTransform rect, float width, float height)
        {
            bool stretched = HasOutOfRangeAnchors(rect)
                             || !Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x)
                             || !Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y)
                             || rect.rect.width > width * 1.5f
                             || rect.rect.height > height * 1.5f;
            if (!stretched)
            {
                return false;
            }

            // Keep world placement: FCU used out-of-range anchors for tip offsets.
            Vector3 worldPosition = rect.position;
            Quaternion worldRotation = rect.rotation;

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.position = worldPosition;
            rect.rotation = worldRotation;
            return true;
        }

        private static int DisableOutlineEffects(Transform root)
        {
            int disabled = 0;
            foreach (DAOutlineEffect outline in root.GetComponentsInChildren<DAOutlineEffect>(true))
            {
                if (outline == null || !outline.enabled)
                {
                    continue;
                }

                outline.enabled = false;
                EditorUtility.SetDirty(outline);
                disabled++;
            }

            return disabled;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate != null
                    && candidate.name == objectName
                    && candidate.scene.IsValid()
                    && !EditorUtility.IsPersistent(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
#endif
