using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicMod
{
    /// <summary>
    /// Puts a converted song into the machine being built, as a selection the
    /// player can then move.
    ///
    /// This is Besiege's own additive load, step for step:
    /// `MachineFileBrowserController.LoadAdditive` is what the load screen's
    /// "add to machine" button runs, and every member it uses is public. Doing the
    /// same thing means joints, clusters, undo and the selection tool all behave
    /// as they do for a machine loaded from a file, rather than as they do for
    /// blocks a mod invented.
    ///
    /// No file is written and nothing is parsed: the blocks go straight in as
    /// `BlockInfo`, which is what a `.bsg` would have been read into anyway.
    /// </summary>
    public static class Drop
    {
        /// <summary>
        /// Adds the plan's blocks to the machine, around <paramref name="origin"/>
        /// in the machine's own space, and leaves them selected. Returns how many
        /// were added, or throws with something worth showing.
        /// </summary>
        public static int Into(SongPlan plan, Vector3 origin)
        {
            Machine machine = Machine.Active();
            if (machine == null)
            {
                throw new Exception("there is no machine to add to");
            }
            if (!machine.CanModify)
            {
                throw new Exception("this machine cannot be changed here");
            }

            List<BlockInfo> blocks = new List<BlockInfo>();
            for (int i = 0; i < plan.Blocks.Count; i++)
            {
                blocks.Add(Info(plan.Blocks[i], origin));
            }

            bool merging = StatMaster.mergeSurfaceTypesOnDeselect;
            Dictionary<Guid, BlockBehaviour> made = null;
            List<UndoAction> undo = new List<UndoAction>();

            machine.isLoadingInfo = true;
            StatMaster.mergeSurfaceTypesOnDeselect = false;
            // What tells the rest of the game that these blocks are arriving as a
            // copy rather than being built one at a time.
            BlockSelectionTool.Duplicating = true;
            try
            {
                machine.AddBlocksFromInfo(blocks, out made, ref undo);
                Select(machine, made, undo);
            }
            finally
            {
                BlockSelectionTool.Duplicating = false;
                StatMaster.mergeSurfaceTypesOnDeselect = merging;
                machine.isLoadingInfo = false;
            }
            return made == null ? 0 : made.Count;
        }

        /// <summary>
        /// Hands the new blocks to the selection tool, so the player is holding
        /// them and can put them where they want. The move tool is chosen for the
        /// same reason the load screen chooses it: a selection nobody can drag is
        /// not one worth making.
        /// </summary>
        private static void Select(Machine machine,
                                   Dictionary<Guid, BlockBehaviour> made,
                                   List<UndoAction> undo)
        {
            if (made == null || made.Count == 0)
            {
                return;
            }
            AdvancedBlockEditor editor = AdvancedBlockEditor.Instance;
            if (editor == null || editor.selectionController == null)
            {
                // Without the editor the blocks are still in the machine, which is
                // most of what was asked for.
                Log.Warn("the blocks were added but could not be selected: "
                         + "the block editor is not up.");
                return;
            }

            BlockSelectionTool picker = editor.selectionController;
            picker.DeselectAll(true, true);
            editor.SetActiveTool(StatMaster.Tool.Translate);
            if (undo != null && undo.Count > 0 && machine.UndoSystem != null)
            {
                machine.UndoSystem.AddActions(undo);
            }

            List<BlockBehaviour> fresh = new List<BlockBehaviour>(made.Values);
            picker.Select(fresh, true, true);

            AddPiece hammer = AddPiece.Instance;
            if (hammer != null)
            {
                if (picker.LastBlock != null)
                {
                    Transform last = picker.LastBlock.transform;
                    hammer.SingleHammerAnimate(last.position, last.position, last.forward);
                }
                hammer.UpdateMiddleOfObject(true);
            }
            if (machine.onBatchOperationComplete != null)
            {
                machine.onBatchOperationComplete();
            }
        }

        /// <summary>One planned block as the game's own description of one.</summary>
        private static BlockInfo Info(SongBlock block, Vector3 origin)
        {
            if (block.Type <= 0)
            {
                // The last gate before a wrong id becomes a wrong block. Nothing
                // should reach here -- Song refuses to plan without ids -- but the
                // failure it guards against is silent and looks like a bug in
                // somebody else's mod.
                throw new Exception("a block in this song has no id");
            }
            BlockInfo info = new BlockInfo();
            info.Guid = Guid.NewGuid();
            info.ID = (BlockType)block.Type;
            info.Position = origin + block.Position;
            info.Rotation = block.Rotation;
            info.Scale = Vector3.one;
            info.BlockData = block.Data;
            return info;
        }
    }
}
