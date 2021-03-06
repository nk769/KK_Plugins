﻿using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using MessagePack;
using Studio;
using System.Collections.Generic;
using System.Linq;
using UniRx;
using UnityEngine;

namespace KK_Plugins.StudioCustomMasking
{
    public class SceneController : SceneCustomFunctionController
    {
        /// <summary>
        /// Folders with attached colliders acting as a mask for the object it is attached to
        /// </summary>
        public Dictionary<int, OCIFolder> MaskingFolders = new Dictionary<int, OCIFolder>();

        protected override void OnSceneSave()
        {
            var data = new PluginData();

            if (MaskingFolders.Count > 0)
                data.data.Add(nameof(MaskingFolders), MessagePackSerializer.Serialize(MaskingFolders.Keys.ToList()));
            else
                data.data.Add(nameof(MaskingFolders), null);
            SetExtendedData(data);
        }

        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            if (operation == SceneOperationKind.Clear || operation == SceneOperationKind.Load)
                MaskingFolders = new Dictionary<int, OCIFolder>();

            if (operation == SceneOperationKind.Clear) return;

            var data = GetExtendedData();
            if (data != null)
                if (data.data.TryGetValue(nameof(MaskingFolders), out var folders) && folders != null)
                    foreach (int folderIndex in MessagePackSerializer.Deserialize<List<int>>((byte[])folders))
                        if (loadedItems.TryGetValue(folderIndex, out ObjectCtrlInfo oci))
                            if (oci is OCIFolder ociFolder)
                                AddColliderToFolder(ociFolder);
        }

        /// <summary>
        /// Check if one of the copied items was a masking folder, add collider to the copy and register it in the list
        /// </summary>
        protected override void OnObjectsCopied(ReadOnlyDictionary<int, ObjectCtrlInfo> copiedItems)
        {
            foreach (var copiedItem in copiedItems)
                if (MaskingFolders.ContainsKey(copiedItem.Key))
                    if (copiedItem.Value is OCIFolder ociFolder)
                        AddColliderToFolder(ociFolder);
            base.OnObjectsCopied(copiedItems);
        }

        /// <summary>
        /// Create a new folder and add a collider to it
        /// </summary>
        public void CreateMaskingFolder()
        {
            TreeNodeObject selectedNode = null;
            if (StudioCustomMasking.AddNewMaskToSelected.Value)
                selectedNode = GetNonMaskingTreeNodeObject();

            OCIFolder ociFolder = AddObjectFolder.Add();
            Singleton<UndoRedoManager>.Instance.Clear();

            AddColliderToFolder(ociFolder);

            if (selectedNode != null)
            {
                Studio.Studio.Instance.treeNodeCtrl.SetParent(ociFolder.treeNodeObject, selectedNode);
                selectedNode.SetTreeState(TreeNodeObject.TreeState.Open);
                var workspaceCtrl = (WorkspaceCtrl)Traverse.Create(Studio.Studio.Instance).Field("m_WorkspaceCtrl").GetValue();
                workspaceCtrl.UpdateUI();
            }

            if (Studio.Studio.optionSystem.autoSelect && ociFolder != null)
                Studio.Studio.Instance.treeNodeCtrl.SelectSingle(ociFolder.treeNodeObject);
        }

        /// <summary>
        /// Add a collider to the folder. Also registers it in the list of masking folders.
        /// </summary>
        /// <param name="ociFolder"></param>
        private void AddColliderToFolder(OCIFolder ociFolder)
        {
            ociFolder.objectItem.name = "FolderCollider";
            var collider = ociFolder.objectItem.AddComponent<BoxCollider>();
#if KK
            collider.size = new Vector3(1f, 1f, 1f);
#elif AI || HS2
            collider.size = new Vector3(10f, 10f, 10f);
#endif
            ociFolder.objectItem.layer = StudioCustomMasking.ColliderLayer;
            ociFolder.objectItem.transform.parent = ociFolder.objectItem.transform;
            ociFolder.name = "Mask (hides parent)";

            //Enable scale
            ociFolder.guideObject.enableScale = true;
            GameObject[] roots = (GameObject[])Traverse.Create(ociFolder.guideObject).Field("roots").GetValue();
            //Set scale inactive
            roots[2].SetActive(false);

            MaskingFolders[ociFolder.objectInfo.dicKey] = ociFolder;
        }

        /// <summary>
        /// Returns the selected TreeNodeObject if it is not a mask, or searches its parent objects until a non-mask object is found. Returns null if the object is a mask and has no non-mask parents.
        /// </summary>
        /// <returns></returns>
        public TreeNodeObject GetNonMaskingTreeNodeObject()
        {
            TreeNodeObject treeNodeObject = null;
            TreeNodeObject[] selectNodes = Studio.Studio.Instance.treeNodeCtrl.selectNodes;
            if (selectNodes.Length == 1)
                treeNodeObject = selectNodes[0];
            if (treeNodeObject == null)
                return null;
            return GetNonMaskingTreeNodeObject(treeNodeObject);
        }

        /// <summary>
        /// Returns the TreeNodeObject if it is not a mask, or searches its parent objects until a non-mask object is found. Returns null if the object is a mask and has no non-mask parents.
        /// </summary>
        /// <param name="treeNodeObject"></param>
        /// <returns></returns>
        public TreeNodeObject GetNonMaskingTreeNodeObject(TreeNodeObject treeNodeObject)
        {
            if (treeNodeObject == null) return null;

            if (MaskingFolders.Values.Any(x => x.treeNodeObject == treeNodeObject))
            {
                TreeNodeObject parent = treeNodeObject.parent;
                if (parent == null)
                    return null;
                return GetNonMaskingTreeNodeObject(parent);
            }
            return treeNodeObject;
        }

        internal void ColliderEnterEvent(Collider collider)
        {
            if (collider == null) return;

            var go = collider.gameObject;
            var f = MaskingFolders.Values.FirstOrDefault(x => x.objectItem == go);
            if (f?.treeNodeObject?.parent == null) return;
            if (f.treeNodeObject.parent.visible)
                f.treeNodeObject.parent.SetVisible(false);
        }

        internal void ColliderStayEvent(Collider collider) => ColliderEnterEvent(collider);

        internal void ColliderExitEvent(Collider collider)
        {
            if (collider == null) return;

            var go = collider.gameObject;
            var f = MaskingFolders.Values.FirstOrDefault(x => x.objectItem == go);
            if (f?.treeNodeObject?.parent == null) return;
            if (!f.treeNodeObject.parent.visible)
                f.treeNodeObject.parent.SetVisible(true);
        }

        internal void ItemDeleteEvent(TreeNodeObject node)
        {
            var kvp = MaskingFolders.FirstOrDefault(x => x.Value.treeNodeObject == node);
            if (kvp.Value != null)
                MaskingFolders.Remove(kvp.Key);
        }
    }
}
